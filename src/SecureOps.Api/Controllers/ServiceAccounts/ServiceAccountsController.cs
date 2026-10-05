using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Security;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// Scoped account list/detail and explicit workflow commands. Every command revalidates capability and data scope;
/// stale versions return 409 with the caller's current view. Nothing here deletes accounts, rotates passwords,
/// converts to gMSA, scans a server or contacts a source system; the only directory access is the bounded read-only name
/// search, and usage scans arrive only as files a person uploads (ADR-0027).
/// </summary>
[ApiController]
[Route("api/v1/service-accounts")]
[Authorize(Policy = ServiceAccountPolicies.View)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountsController(ServiceAccountService service) : ControllerBase
{
    private const long _maxEvidenceRequestBytes = 11L * 1024 * 1024;
    private const long _maxUsageScanRequestBytes = 5L * 1024 * 1024;

    /// <summary>Entry summary: open work targeted at the caller's teams and coordinator attention counts (scope-filtered).</summary>
    [HttpGet("work-summary")]
    [ProducesResponseType(typeof(ServiceAccountWorkSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceAccountWorkSummary>> WorkSummaryAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.WorkSummaryAsync(User, Context(), cancellationToken));

    /// <summary>XLSX export of the caller's filtered account list (Report capability, same scope and filters, capped, audited, rate limited).</summary>
    [HttpGet("accounts/export")]
    [Authorize(Policy = ServiceAccountPolicies.Report)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(ServiceAccountsApiModule.ExportRateLimit)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync([FromQuery] AccountListQuery query, CancellationToken cancellationToken)
    {
        SaResult<ReportExport> result = await service.ExportAccountsAsync(User, Context(), query, cancellationToken);
        return result.IsSuccess ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : ServiceAccountReplies.Reply(this, result).Result!;
    }

    /// <summary>Server-paged, scope-filtered account list with stable ordering.</summary>
    [HttpGet("accounts")]
    [ProducesResponseType(typeof(AccountPage), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountPage>> ListAsync([FromQuery] AccountListQuery query, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.AccountsAsync(User, Context(), query, cancellationToken));

    /// <summary>Creates an account explicitly (no automatic provisioning from names).</summary>
    [HttpPost("accounts")]
    [Authorize(Policy = ServiceAccountPolicies.Assign)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateAccountAsync(User, Context(), request, cancellationToken));

    /// <summary>Account detail: ownership, requests, actions, communications, findings, sources, handover and history.</summary>
    [HttpGet("accounts/{id:guid}")]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.AccountAsync(User, Context(), id, cancellationToken));

    /// <summary>Updates account attributes at the expected version.</summary>
    [HttpPatch("accounts/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UpdateAsync(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UpdateAccountAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Proposes or confirms ownership (confirmation requires assignment authority in scope).</summary>
    [HttpPost("accounts/{id:guid}/ownership")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> OwnershipAsync(Guid id, OwnershipChangeRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ChangeOwnershipAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Confirms or rejects a proposed ownership.</summary>
    [HttpPost("accounts/{id:guid}/ownership/{assignmentId:guid}/decision")]
    [Authorize(Policy = ServiceAccountPolicies.Assign)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> DecideOwnershipAsync(Guid id, Guid assignmentId, OwnershipDecisionRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.DecideOwnershipAsync(User, Context(), id, assignmentId, request, cancellationToken));

    /// <summary>Creates a request (expected work) for the account.</summary>
    [HttpPost("accounts/{id:guid}/requests")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> CreateRequestAsync(Guid id, CreateWorkRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateRequestAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Updates a request at the expected version (null keeps a value; ClearFields clears with a reason).</summary>
    [HttpPatch("requests/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UpdateRequestAsync(Guid id, UpdateWorkRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UpdateRequestAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Explicitly closes exactly this request after checking completion conditions.</summary>
    [HttpPost("requests/{id:guid}/close")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> CloseRequestAsync(Guid id, CloseWorkRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CloseRequestAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Reports a planned or performed action; the related request stays open.</summary>
    [HttpPost("accounts/{id:guid}/actions")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> ReportActionAsync(Guid id, ReportActionRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ReportActionAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Updates the same action identity (plan → performed).</summary>
    [HttpPatch("actions/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UpdateActionAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Verifies an action with date, verifier and evidence; never creates a second action.</summary>
    [HttpPost("actions/{id:guid}/verify")]
    [Authorize(Policy = ServiceAccountPolicies.Verify)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> VerifyActionAsync(Guid id, VerifyActionRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.VerifyActionAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Voids an action with a reason.</summary>
    [HttpPost("actions/{id:guid}/void")]
    [Authorize(Policy = ServiceAccountPolicies.Verify)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> VoidActionAsync(Guid id, VoidActionRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.VoidActionAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Records one mail linked to any number of in-scope accounts.</summary>
    [HttpPost("communications")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(CommunicationSaveResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<CommunicationSaveResult>> CommunicationAsync(CreateCommunicationRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateCommunicationAsync(User, Context(), request, cancellationToken));

    /// <summary>Records a technical finding.</summary>
    [HttpPost("findings")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> FindingAsync(CreateFindingRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateFindingAsync(User, Context(), request, cancellationToken));

    /// <summary>Updates a finding status.</summary>
    [HttpPatch("findings/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UpdateFindingAsync(Guid id, UpdateFindingRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UpdateFindingAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Proposes a handover (proposal is not acceptance).</summary>
    [HttpPost("accounts/{id:guid}/handovers")]
    [Authorize(Policy = ServiceAccountPolicies.Assign)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> CreateHandoverAsync(Guid id, CreateHandoverRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateHandoverAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Accepts or rejects a handover (authorized target team, real date, evidence note).</summary>
    [HttpPost("handovers/{id:guid}/decision")]
    [Authorize(Policy = ServiceAccountPolicies.Assign)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> DecideHandoverAsync(Guid id, HandoverDecisionRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.DecideHandoverAsync(User, Context(), id, request, cancellationToken));

    /// <summary>
    /// Bounded read-only directory search by first name or full name (ADR-0025): at least three letters, at most ten results,
    /// minimal fields, scope-checked Service Accounts links. Uses the platform identity-lookup capability and rate limit.
    /// </summary>
    [HttpPost("directory/name-search")]
    [Authorize(Policy = Policies.CanIdentityLookup)]
    [EnableRateLimiting(ApiRateLimits.IdentityLookup)]
    [ProducesResponseType(typeof(DirectoryNameSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<DirectoryNameSearchResponse>> DirectoryNameSearchAsync(DirectoryNameSearchRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.DirectoryNameSearchAsync(User, Context(), request, cancellationToken));

    /// <summary>Records where the account is used (knowledge-base rule input).</summary>
    [HttpPost("accounts/{id:guid}/usages")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> CreateUsageAsync(Guid id, CreateUsageRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateUsageAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Updates an active usage at the expected version.</summary>
    [HttpPatch("usages/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UpdateUsageAsync(Guid id, UpdateUsageRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UpdateUsageAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Removes a usage with a reason (kept for history).</summary>
    [HttpPost("usages/{id:guid}/remove")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> RemoveUsageAsync(Guid id, RemoveUsageRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RemoveUsageAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Records or clears a reasoned rule exception (verifier).</summary>
    [HttpPost("usages/{id:guid}/exception")]
    [Authorize(Policy = ServiceAccountPolicies.Verify)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UsageExceptionAsync(Guid id, UsageExceptionRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SetUsageExceptionAsync(User, Context(), id, request, cancellationToken));

    /// <summary>
    /// Attaches a usage scan a person ran under their own authority (ADR-0027) to the account as evidence. The whole file is
    /// checked (secret-like fields refuse it before anything is stored); a participant names one of its own open requests.
    /// Nothing here contacts a server or changes the account, its requests or its actions.
    /// </summary>
    [HttpPost("accounts/{id:guid}/usage-scans")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [RequestSizeLimit(_maxUsageScanRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = _maxUsageScanRequestBytes)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UsageScanUploadAsync(Guid id, [FromForm] IFormFile file, [FromForm] string? runStatement,
        [FromForm] Guid? requestId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length is 0 or > _maxUsageScanRequestBytes)
        {
            return ServiceAccountReplies.Reply(this, SaResult<AccountDetail>.Fail(SaErrors.UsageScanFile,
                file is { Length: > 0 } ? Infrastructure.ServiceAccounts.UsageScans.UsageScanFileCodes.TooLarge
                    : Infrastructure.ServiceAccounts.UsageScans.UsageScanFileCodes.Empty));
        }

        using MemoryStream buffer = new();
        await file.CopyToAsync(buffer, cancellationToken);
        return ServiceAccountReplies.Reply(this, await service.AttachUsageScanAsync(User, Context(), id, file.FileName, buffer.ToArray(), runStatement,
            requestId, cancellationToken));
    }

    /// <summary>
    /// One page of the scans attached to the account (newest first, <see cref="UsageScanPaging.ScanPageSize"/> per page), read
    /// only and under the same scope as the account detail. Coverage and outcomes are computed over every matched item.
    /// </summary>
    [HttpGet("accounts/{id:guid}/usage-scans")]
    [ProducesResponseType(typeof(UsageScanPage), StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageScanPage>> UsageScanPageAsync(Guid id, [FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        ServiceAccountReplies.Reply(this, await service.UsageScanPageAsync(User, Context(), id, page, cancellationToken));

    /// <summary>
    /// One page of a linked scan's matched items for this account: <c>role=Former</c> (default, undecided first; <c>pending=true</c>
    /// keeps only undecided items) or <c>role=Expected</c>. Read only; paging never changes coverage or outcomes.
    /// </summary>
    [HttpGet("accounts/{id:guid}/usage-scans/{linkId:guid}/items")]
    [ProducesResponseType(typeof(UsageScanItemPage), StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageScanItemPage>> UsageScanItemsAsync(Guid id, Guid linkId, [FromQuery] string? role = null, [FromQuery] bool pending = false,
        [FromQuery] int page = 1, [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        ServiceAccountReplies.Reply(this, await service.UsageScanItemsAsync(User, Context(), id, linkId, role, pending, page, pageSize, cancellationToken));

    /// <summary>Records a matched component of an attached scan as a usage (a person's decision, never automatic).</summary>
    [HttpPost("accounts/{id:guid}/usage-scan-items/{itemId:guid}/usage")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> RecordScanUsageAsync(Guid id, Guid itemId, RecordScanUsageRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RecordScanUsageAsync(User, Context(), id, itemId, request, cancellationToken));

    /// <summary>Leaves a matched component out of the usage records, with a reason.</summary>
    [HttpPost("accounts/{id:guid}/usage-scan-items/{itemId:guid}/dismiss")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> DismissScanItemAsync(Guid id, Guid itemId, DismissScanItemRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.DismissScanItemAsync(User, Context(), id, itemId, request, cancellationToken));

    /// <summary>Updates gMSA suitability, plan or completion reference.</summary>
    [HttpPatch("transitions/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Assign)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> UpdateTransitionAsync(Guid id, TransitionUpdateRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UpdateTransitionAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Uploads evidence for an in-scope entity (type/signature/size checked).</summary>
    [HttpPost("accounts/{id:guid}/evidence")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [RequestSizeLimit(_maxEvidenceRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = _maxEvidenceRequestBytes)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetail>> EvidenceUploadAsync(Guid id, [FromForm] IFormFile file, [FromForm] string ownerType, [FromForm] Guid ownerId,
        [FromForm] string? label, CancellationToken cancellationToken)
    {
        if (file is null || file.Length is 0 or > _maxEvidenceRequestBytes)
        {
            return ServiceAccountReplies.Reply(this, SaResult<AccountDetail>.Fail(SaErrors.Invalid, "file"));
        }

        using MemoryStream buffer = new();
        await file.CopyToAsync(buffer, cancellationToken);
        return ServiceAccountReplies.Reply(this, await service.AddEvidenceAsync(User, Context(), id, ownerType, ownerId, file.FileName, file.ContentType,
            buffer.ToArray(), label, cancellationToken));
    }

    /// <summary>Downloads evidence under the same scope policy (audited).</summary>
    [HttpGet("evidence/{id:guid}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> EvidenceAsync(Guid id, CancellationToken cancellationToken)
    {
        SaResult<EvidenceDownload> result = await service.EvidenceAsync(User, Context(), id, cancellationToken);
        return result.IsSuccess
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : ServiceAccountReplies.Reply(this, result).Result!;
    }

    private Infrastructure.Access.AccessOperationContext Context() => ServiceAccountReplies.Context(this);
}
