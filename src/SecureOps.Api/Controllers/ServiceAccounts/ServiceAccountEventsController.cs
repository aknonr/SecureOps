using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// "What happened?" event guidance (docs/service-accounts/EVENT-GUIDANCE-DESIGN.md §5, migration 036). A person picks the event;
/// the server derives which records it produces, shows them in a preview that writes nothing, and records them all in one
/// transaction on a separate commit. An event never verifies, never closes an account and never decides ownership; a reported
/// action is always "reported, not verified". Nothing here contacts PAM, AD, ITSM or a server.
/// </summary>
[ApiController]
[Route("api/v1/service-accounts/accounts/{id:guid}/events")]
[Authorize(Policy = ServiceAccountPolicies.Work)]
[RequestSizeLimit(_maxEventRequestBytes)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountEventsController(ServiceAccountService service) : ControllerBase
{
    // A draft is a few short fields and at most ten references; the summary is bounded at 1 000 characters.
    private const long _maxEventRequestBytes = 64L * 1024;

    /// <summary>What a commit of the draft would record and what it would not; writes and audits nothing.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(EventPreview), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventPreview>> PreviewAsync(Guid id, EventDraft draft, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.PreviewEventAsync(User, Context(), id, draft, cancellationToken));

    /// <summary>
    /// Records the previewed draft in one transaction (idempotencyKey and previewDigest required). 409 when the preview is stale or
    /// the key was used with another draft; 422 when the requested close is not allowed; in every refusal nothing is written.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(EventCommitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EventCommitResult>> CommitAsync(Guid id, EventDraft draft, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CommitEventAsync(User, Context(), id, draft, cancellationToken));

    private Infrastructure.Access.AccessOperationContext Context() => ServiceAccountReplies.Context(this);
}
