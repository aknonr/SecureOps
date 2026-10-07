using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// gMSA conversion change plans (docs/service-accounts/CHANGE-PLAN-DESIGN.md, migration 033): plan, preview from the accounts'
/// latest Discovery scans, and approval bound to one preview version and its SHA-256. Nothing here connects to a server,
/// converts an account or carries a password; the change itself is made by people under their normal change record.
/// A plan is visible only when every one of its accounts is inside the caller's scope (otherwise 404, same as missing).
/// </summary>
[ApiController]
[Route("api/v1/service-accounts/change-plans")]
[Authorize(Policy = ServiceAccountPolicies.View)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountChangePlansController(ServiceAccountService service) : ControllerBase
{
    /// <summary>Server-paged plans whose every account is in the caller's scope, newest change first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ChangePlanPage), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanPage>> ListAsync([FromQuery] ChangePlanListQuery query, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ChangePlansAsync(User, Context(), query, cancellationToken));

    /// <summary>Plan detail: accounts, current preview summary, approval, events and what the caller may do.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ChangePlanView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanView>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ChangePlanAsync(User, Context(), id, cancellationToken));

    /// <summary>Rows of the plan's current preview, page by page (1–200 per page).</summary>
    [HttpGet("{id:guid}/items")]
    [ProducesResponseType(typeof(ChangePlanItemPage), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanItemPage>> ItemsAsync(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ServiceAccountReplies.Reply(this, await service.ChangePlanItemsAsync(User, Context(), id, page, pageSize, cancellationToken));

    /// <summary>Creates a Draft gMSA conversion plan; any refused account refuses the whole request (per-account results in the 400).</summary>
    [HttpPost]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(ChangePlanView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanView>> CreateAsync(CreateChangePlanRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateChangePlanAsync(User, Context(), request, cancellationToken));

    /// <summary>Replaces the account list at the expected version (Draft or Previewed; a real change returns the plan to Draft).</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(ChangePlanView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanView>> UpdateAsync(Guid id, UpdateChangePlanRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UpdateChangePlanAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Builds a new preview version from each account's latest Discovery scan.</summary>
    [HttpPost("{id:guid}/preview")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(ChangePlanView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanView>> PreviewAsync(Guid id, PreviewChangePlanRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.PreviewChangePlanAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Approves the current preview (Verify; never the planner, the previewer or anyone who changed the plan).</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = ServiceAccountPolicies.Verify)]
    [ProducesResponseType(typeof(ChangePlanView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanView>> ApproveAsync(Guid id, ApproveChangePlanRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ApproveChangePlanAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Cancels an open plan with a reason: the planner (Work) or a verifier (Verify); the service decides which applies.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ChangePlanView), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangePlanView>> CancelAsync(Guid id, CancelChangePlanRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CancelChangePlanAsync(User, Context(), id, request, cancellationToken));

    private Infrastructure.Access.AccessOperationContext Context() => ServiceAccountReplies.Context(this);
}
