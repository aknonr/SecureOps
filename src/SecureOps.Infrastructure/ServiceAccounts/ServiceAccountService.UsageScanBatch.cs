using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    /// <summary>
    /// Attaches one usage-scan file to 1–<see cref="UsageScanBatch.MaxAccounts"/> accounts (ADR-0027). The Work capability is
    /// checked first; the file is validated once and completely (secret guard first) before any account is looked at, so a
    /// refused file stores nothing for anyone. Each account is then checked on its own: in scope, responsible basis (a
    /// participant attaches through its own request on the account page), and searched by the file. One account's refusal
    /// or storage failure never blocks another; the scan is stored once and linked per account in its own transaction, with
    /// history and audit per link. An account outside the caller's scope comes back without its name.
    /// </summary>
    public Task<SaResult<UsageScanBatchResult>> AttachUsageScanToAccountsAsync(ClaimsPrincipal principal, AccessOperationContext context,
        IReadOnlyList<Guid>? accountIds, string fileName, byte[] content, string? runStatement, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            Guid[] ids = [.. accountIds ?? []];
            if (ids.Length is 0 or > UsageScanBatch.MaxAccounts || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length)
            {
                return SaResult<UsageScanBatchResult>.Fail(SaErrors.Invalid, "accountIds");
            }

            if (!ValidRunStatement(runStatement))
            {
                return SaResult<UsageScanBatchResult>.Fail(SaErrors.Invalid, "runStatement");
            }

            ParsedUsageScan parsed;
            try
            {
                parsed = UsageScanParser.Parse(content, _options.MaxUsageScanBytes, clock.GetUtcNow());
            }
            catch (UsageScanFileException rejected)
            {
                // Only the stable code leaves this method; the file content is neither logged nor stored.
                return SaResult<UsageScanBatchResult>.Fail(SaErrors.UsageScanFile, rejected.Code);
            }

            string sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            Guid? scanId = null;
            List<UsageScanBatchAccountResult> results = [];
            foreach (Guid id in ids)
            {
                // One failure boundary per account (same filter as RunAsync): reading, checking and linking this account may
                // fail without undoing earlier links or skipping later accounts; links are committed one by one.
                (string Name, string? Domain)? known = null;
                string? matched = null;
                UsageScanBatchOutcome outcome;
                try
                {
                    (AccountScopeAnchor Anchor, string Version, string Name, string? Domain)? read = await repository!.AnchorAsync(id, cancellationToken);
                    AccountScopeAnchor? anchor = read?.Anchor;
                    known = read is { } r && caller.Scope.Covers(r.Anchor) ? (r.Name, r.Domain) : null;
                    (matched, bool ambiguous) = known is { } k ? UsageScanOutcomes.SearchedName(parsed.Accounts, k.Name, k.Domain) : (null, false);
                    outcome = UsageScanBatch.Precheck(known is not null, anchor is not null && Responsible(caller.Scope, anchor), matched, ambiguous);
                    if (outcome == UsageScanBatchOutcome.Attached)
                    {
                        // The write re-reads scope and responsibility under its own locks; a change after this check refuses it.
                        SaResult<(Guid ScanId, bool Attached)> attached = await repository.AttachUsageScanAsync(id,
                            new UsageScanUpload(parsed, content, sha, SafeName(fileName), runStatement.Trim(), matched!), null, caller.Actor,
                            (scope, current, _) => Responsible(scope, current), cancellationToken);
                        if (attached.Field == "scanTablesMissing")
                        {
                            return SaResult<UsageScanBatchResult>.Fail(SaErrors.Invalid, "scanTablesMissing");
                        }

                        scanId = attached.IsSuccess ? attached.Value.ScanId : scanId;
                        outcome = !attached.IsSuccess ? UsageScanBatchOutcome.Unavailable
                            : attached.Value.Attached ? UsageScanBatchOutcome.Attached : UsageScanBatchOutcome.AlreadyAttached;
                        known = attached.IsSuccess ? known : null;
                    }
                }
                catch (Exception exception) when (exception is DbException or IOException or InvalidOperationException or TimeoutException)
                {
                    logger.LogError("Service Accounts usage-scan link failed for one account of a multi-account upload. FailureType: {FailureType} Number={SqlNumber} Origin={Origin} CorrelationId={CorrelationId}",
                        exception.GetType().Name, (exception as SqlException)?.Number, Origin(exception), context.CorrelationId);
                    outcome = UsageScanBatchOutcome.Failed;
                }

                bool linked = outcome is UsageScanBatchOutcome.Attached or UsageScanBatchOutcome.AlreadyAttached;
                results.Add(new UsageScanBatchAccountResult(id, known?.Name, known?.Domain, outcome.ToString(), UsageScanBatch.Label(outcome),
                    linked ? matched : null));
            }

            return new SaResult<UsageScanBatchResult>(new UsageScanBatchResult(scanId, parsed.Purpose, parsed.Servers.Count, parsed.AnsweredServers, results));
        }, cancellationToken);

    /// <summary>Organization-level scope or the confirmed owner team: may work on the whole account (not only through a request).</summary>
    private static bool Responsible(ServiceAccountScope scope, AccountScopeAnchor anchor) =>
        scope.CoversAtOrganizationLevel(anchor) || scope.CoversTeam(anchor.OwnerTeamId);

    /// <summary>Where and under which authority the scan ran: 5–400 characters, no control characters.</summary>
    private static bool ValidRunStatement([NotNullWhen(true)] string? runStatement) =>
        runStatement is not null && runStatement.Trim().Length >= 5 && runStatement.Length <= 400 && !runStatement.Any(char.IsControl);
}
