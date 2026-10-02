using System.Security.Claims;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    /// <summary>
    /// Bounded, read-only directory search by first name or full name (ADR-0025). Requires module View and the existing
    /// platform identity-lookup capability. Audit is written before the directory is touched (fail closed) and records only
    /// a hash and counts, never the name. A result is linked to a Service Accounts record only when exactly one record with
    /// that account name is inside the caller's scope; nothing is granted, confirmed or written to the directory.
    /// </summary>
    public Task<SaResult<DirectoryNameSearchResponse>> DirectoryNameSearchAsync(ClaimsPrincipal principal, AccessOperationContext context,
        DirectoryNameSearchRequest request, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            if (!caller.Can(Capabilities.IdentityLookup))
            {
                return SaResult<DirectoryNameSearchResponse>.Fail(SaErrors.Forbidden, "identityLookup");
            }

            if (DirectoryNameQuery.TryCreate(request.Query, out string? error) is not { } query)
            {
                return SaResult<DirectoryNameSearchResponse>.Fail(SaErrors.Invalid, error);
            }

            string queryHash = AuditAccountHasher.HashAccountInput(query.Text) ?? string.Empty;
            if (directory is null)
            {
                await repository!.AuditReadAsync("DirectoryNameSearchFailed", new { QueryHash = queryHash, Failure = "NotConfigured" }, caller.Actor, cancellationToken);
                return SaResult<DirectoryNameSearchResponse>.Fail(SaErrors.DirectoryUnavailable);
            }

            await repository!.AuditReadAsync("DirectoryNameSearchRequested", new { QueryHash = queryHash, QueryLength = query.Text.Length, Words = query.Tokens.Count },
                caller.Actor, cancellationToken);
            DirectoryNameSearchResult found;
            try
            {
                found = await directory.SearchAsync(query, DirectoryNameQuery.MaximumResults, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Provider messages may echo the filter (a name); only the failure type is logged and audited.
                string failure = exception is TimeoutException or OperationCanceledException ? "Timeout" : "ProviderUnavailable";
                logger.LogError("Service Accounts directory name search failed. FailureType: {FailureType} Outcome: {Outcome} CorrelationId={CorrelationId}",
                    exception.GetType().Name, failure, context.CorrelationId);
                await repository.AuditReadAsync("DirectoryNameSearchFailed", new { QueryHash = queryHash, Failure = failure }, caller.Actor, cancellationToken);
                return SaResult<DirectoryNameSearchResponse>.Fail(SaErrors.DirectoryUnavailable);
            }

            DirectoryNameCandidate[] matches = [.. DirectoryNameMatching.Order(query, found.Candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.SamAccountName) && DirectoryNameMatching.Matches(query, c)))
                .Take(DirectoryNameQuery.MaximumResults)];
            ILookup<string, Guid> linked = (await repository.AccessibleAccountsByNameAsync(caller.Scope,
                    [.. matches.Select(m => ServiceAccountText.AccountKey(m.SamAccountName)!).Distinct(StringComparer.Ordinal)], cancellationToken))
                .ToLookup(a => a.NameKey, a => a.Id, StringComparer.Ordinal);
            var sameName = matches.GroupBy(m => DirectoryNameMatching.Fold(m.DisplayName ?? m.SamAccountName)).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            DirectoryNameMatch[] results = [.. matches.Select(m =>
            {
                Guid[] ids = [.. linked[ServiceAccountText.AccountKey(m.SamAccountName)!]];
                return new DirectoryNameMatch(m.DisplayName ?? m.SamAccountName, m.SamAccountName, m.Department,
                    sameName.Contains(DirectoryNameMatching.Fold(m.DisplayName ?? m.SamAccountName)), ids.Length == 1 ? ids[0] : null);
            })];
            bool truncated = found.Truncated || found.Candidates.Count > DirectoryNameQuery.MaximumResults;
            await repository.AuditReadAsync("DirectoryNameSearchCompleted", new
            {
                QueryHash = queryHash,
                Results = results.Length,
                Truncated = truncated,
                Linked = results.Count(r => r.ServiceAccountId is not null)
            }, caller.Actor, cancellationToken);
            return new SaResult<DirectoryNameSearchResponse>(new DirectoryNameSearchResponse(results, truncated, DirectoryNameQuery.MinimumLetters,
                DirectoryNameQuery.MaximumResults));
        }, cancellationToken);
}
