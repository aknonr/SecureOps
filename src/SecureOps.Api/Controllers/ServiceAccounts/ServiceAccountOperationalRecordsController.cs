using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// Opening a Turuncu Hat OR from an approved gMSA change plan (ADR-0030, PROPOSED; OR-2 ships a Fake adapter only and the
/// server default is Disabled; migration 035). One OR per plan account: a preview that shows every field that will be sent, a
/// separate confirmation that sends exactly that once (fingerprint, idempotency key), manual reconciliation of an unclear
/// outcome and a live, read-only tracking summary. Nothing is ever retried automatically and nothing contacts a managed server.
/// A plan is visible only when every one of its accounts is inside the caller's scope (otherwise 404, same as missing).
/// </summary>
[ApiController]
[Route("api/v1/service-accounts/change-plans/{id:guid}")]
[Authorize(Policy = ServiceAccountPolicies.View)]
[RequestSizeLimit(_maxRequestBytes)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountOperationalRecordsController(ServiceAccountService service) : ControllerBase
{
    // A description is at most 4,000 characters; the bound leaves room for JSON escaping of Turkish text and line breaks.
    private const long _maxRequestBytes = 64L * 1024;

    /// <summary>Every plan account's OR attempts and what the caller may do now.</summary>
    [HttpGet("operational-records")]
    [ProducesResponseType(typeof(PlanOperationalRecordsView), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlanOperationalRecordsView>> ListAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.OperationalRecordsAsync(User, Context(), id, cancellationToken));

    /// <summary>What a confirmation would send for one account (writes nothing). Edited text is previewed again before it can be sent.</summary>
    [HttpPost("accounts/{accountId:guid}/operational-record/preview")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(OperationalRecordPreview), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationalRecordPreview>> PreviewAsync(Guid id, Guid accountId, OperationalRecordPreviewRequest request,
        CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.PreviewOperationalRecordAsync(User, Context(), id, accountId, request, cancellationToken));

    /// <summary>
    /// Sends the previewed OR once (the preview's fingerprint and a fresh idempotency key). An unclear answer is stored as
    /// OutcomeUnknown and is reconciled by a person; it is never sent again automatically.
    /// </summary>
    [HttpPost("accounts/{accountId:guid}/operational-record")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(OperationalRecordSubmitResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationalRecordSubmitResult>> SubmitAsync(Guid id, Guid accountId, OperationalRecordSubmitRequest request,
        CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SubmitOperationalRecordAsync(User, Context(), id, accountId, request, cancellationToken));

    /// <summary>Looks up an OR number found in Turuncu Hat during reconciliation (writes nothing).</summary>
    [HttpPost("accounts/{accountId:guid}/operational-record/candidate")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(OperationalRecordCandidate), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationalRecordCandidate>> CandidateAsync(Guid id, Guid accountId, OperationalRecordCandidateRequest request,
        CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.OperationalRecordCandidateAsync(User, Context(), id, accountId, request, cancellationToken));

    /// <summary>Links an OR number to the account's unresolved submission with the person's reason (verified only when the lookup finds it).</summary>
    [HttpPost("accounts/{accountId:guid}/operational-record/link")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(PlanOperationalRecordsView), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlanOperationalRecordsView>> LinkAsync(Guid id, Guid accountId, OperationalRecordLinkRequest request,
        CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.LinkOperationalRecordAsync(User, Context(), id, accountId, request, cancellationToken));

    /// <summary>States that no OR was created for the account's unresolved submission (reason mandatory; a new attempt is then allowed).</summary>
    [HttpPost("accounts/{accountId:guid}/operational-record/not-created")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(PlanOperationalRecordsView), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlanOperationalRecordsView>> NotCreatedAsync(Guid id, Guid accountId, OperationalRecordNotCreatedRequest request,
        CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ConfirmOperationalRecordNotCreatedAsync(User, Context(), id, accountId, request, cancellationToken));

    /// <summary>Live, read-only tracking summary of the account's OR; every unknown value is null.</summary>
    [HttpGet("accounts/{accountId:guid}/operational-record/tracking")]
    [ProducesResponseType(typeof(OperationalRecordTrackingView), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperationalRecordTrackingView>> TrackingAsync(Guid id, Guid accountId, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.OperationalRecordTrackingAsync(User, Context(), id, accountId, cancellationToken));

    private Infrastructure.Access.AccessOperationContext Context() => ServiceAccountReplies.Context(this);
}
