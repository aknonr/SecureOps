using System.Security.Claims;
using System.Security.Cryptography;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    /// <summary>Highest page number accepted for scan and item pages (keeps the offset arithmetic bounded).</summary>
    private const int _maxScanPage = 10_000;

    /// <summary>
    /// One page of the scans attached to an account (newest first), read only, under the same scope rule as the account detail:
    /// out of scope and missing are indistinguishable. Coverage and outcomes do not depend on the page.
    /// </summary>
    public Task<SaResult<UsageScanPage>> UsageScanPageAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, int page,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            if (page is < 1 or > _maxScanPage)
            {
                return SaResult<UsageScanPage>.Fail(SaErrors.Invalid, "page");
            }

            if (!await AccountVisibleAsync(caller, accountId, cancellationToken))
            {
                return SaResult<UsageScanPage>.Fail(SaErrors.NotFound);
            }

            (IReadOnlyList<UsageScanView>? scans, int total, int pending) = await repository!.UsageScansAsync(accountId, page, cancellationToken);
            return scans is null ? SaResult<UsageScanPage>.Fail(SaErrors.Invalid, "scanTablesMissing")
                : new SaResult<UsageScanPage>(new UsageScanPage(scans, total, page, UsageScanPaging.ScanPageSize, pending));
        }, cancellationToken);

    /// <summary>
    /// One page of a linked scan's matched items for this account, read only, same scope rule as the detail. <c>Former</c> items come
    /// undecided first and can be narrowed to the undecided ones; <c>Expected</c> items are the expected gMSA.
    /// </summary>
    public Task<SaResult<UsageScanItemPage>> UsageScanItemsAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, Guid linkId,
        string? role, bool pendingOnly, int page, int? pageSize, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            string kind = role ?? "Former";
            int size = pageSize ?? UsageScanPaging.DefaultItemPageSize;
            string? invalid = kind is not ("Former" or "Expected") ? "role"
                : pendingOnly && kind != "Former" ? "pending"
                : page is < 1 or > _maxScanPage ? "page"
                : size is < 1 or > UsageScanPaging.MaxItemPageSize ? "pageSize"
                : null;
            if (invalid is not null)
            {
                return SaResult<UsageScanItemPage>.Fail(SaErrors.Invalid, invalid);
            }

            if (!await AccountVisibleAsync(caller, accountId, cancellationToken))
            {
                return SaResult<UsageScanItemPage>.Fail(SaErrors.NotFound);
            }

            (bool missing, UsageScanItemPage? items) = await repository!.UsageScanItemsAsync(accountId, linkId, kind, pendingOnly, page, size, cancellationToken);
            return missing ? SaResult<UsageScanItemPage>.Fail(SaErrors.Invalid, "scanTablesMissing")
                : items is null ? SaResult<UsageScanItemPage>.Fail(SaErrors.NotFound, "linkId")
                : new SaResult<UsageScanItemPage>(items);
        }, cancellationToken);

    private async Task<bool> AccountVisibleAsync(SaCaller caller, Guid accountId, CancellationToken cancellationToken) =>
        await repository!.AnchorAsync(accountId, cancellationToken) is { } anchor && caller.Scope.Covers(anchor.Anchor);

    /// <summary>
    /// Attaches a usage scan a person ran under their own authority to an account (ADR-0027). Capability and scope are checked
    /// before the file is read; the file is validated completely (secret guard first) before anything is stored. The
    /// responsible basis attaches to the account; a participant attaches only through one of its own open requests. The file
    /// must have searched this account. Evidence only: nothing about the account, its requests or its actions changes.
    /// </summary>
    public Task<SaResult<AccountDetail>> AttachUsageScanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, string fileName,
        byte[] content, string? runStatement, Guid? requestId, CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Work, p => p.CanWorkAnyRequest, async (caller, detail) =>
        {
            if (requestId is { } request ? !detail.Permissions.CanWorkRequest(request) || !detail.Requests.Any(r => r.Id == request && r.Status == "Open")
                : !detail.Permissions.Work)
            {
                return SaResult<Guid>.Fail(SaErrors.Forbidden, "requestId");
            }

            if (!ValidRunStatement(runStatement))
            {
                return SaResult<Guid>.Fail(SaErrors.Invalid, "runStatement");
            }

            ParsedUsageScan parsed;
            try
            {
                parsed = UsageScanParser.Parse(content, _options.MaxUsageScanBytes, clock.GetUtcNow());
            }
            catch (UsageScanFileException rejected)
            {
                // Only the stable code leaves this method; the file content is neither logged nor stored.
                return SaResult<Guid>.Fail(SaErrors.UsageScanFile, rejected.Code);
            }

            (string? matched, bool ambiguous) = UsageScanOutcomes.SearchedName(parsed.Accounts, detail.Summary.AccountName, detail.Summary.Domain);
            if (matched is null)
            {
                return SaResult<Guid>.Fail(SaErrors.Invalid, ambiguous ? "accountAmbiguousInScan" : "accountNotInScan");
            }

            string sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            UsageScanUpload upload = new(parsed, content, sha, SafeName(fileName), runStatement.Trim(), matched);
            SaResult<(Guid ScanId, bool Attached)> attached = await repository!.AttachUsageScanAsync(accountId, upload, requestId, caller.Actor, cancellationToken);
            return attached.IsSuccess ? attached.Value.ScanId : SaResult<Guid>.Fail(attached.ErrorCode!, attached.Field);
        }, cancellationToken);

    /// <summary>Records a matched component of an attached scan as a usage (responsible basis; a person's decision).</summary>
    public Task<SaResult<AccountDetail>> RecordScanUsageAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, Guid itemId,
        RecordScanUsageRequest request, CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Work, p => p.Work, (caller, _) =>
        {
            (UsageKind kind, DatabaseEngine? engine, bool? needVerified, string? invalid) = UsageFields(request.Kind, request.DatabaseEngine, request.NeedVerified);
            invalid ??= !ValidText(request.Notes, 1000) ? "notes" : null;
            return invalid is not null
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, invalid))
                : repository!.RecordScanUsageAsync(accountId, itemId, kind, engine, needVerified, request.Notes, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Leaves a matched component out of the usage records with a reason (responsible basis).</summary>
    public Task<SaResult<AccountDetail>> DismissScanItemAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, Guid itemId,
        DismissScanItemRequest request, CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Work, p => p.Work, (caller, _) =>
            !ValidText(request.Reason, 1000, true)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "reason"))
                : repository!.DismissScanItemAsync(accountId, itemId, request.Reason.Trim(), caller.Actor, cancellationToken), cancellationToken);
}
