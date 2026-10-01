using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Reminders;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// In-app reminders and coordinator drafts from the deduplicated outbox. Nothing here sends mail, Teams or Jira
/// messages; drafts are copied and sent outside the application by the coordinator.
/// </summary>
[ApiController]
[Route("api/v1/service-accounts/reminders")]
[Authorize(Policy = ServiceAccountPolicies.View)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountRemindersController(ServiceAccountService service) : ControllerBase
{
    /// <summary>Reminders within the caller's scope, optionally only for the caller's direct teams.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReminderView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ReminderView>>> ListAsync([FromQuery] string? status, [FromQuery] bool myTeam, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RemindersAsync(User, ServiceAccountReplies.Context(this), status, myTeam, cancellationToken));

    /// <summary>Dismisses a delivered or dead-lettered reminder at its expected version.</summary>
    [HttpPost("{id:guid}/dismiss")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> DismissAsync(Guid id, DismissReminderRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.DismissReminderAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));

    /// <summary>Runs the same idempotent evaluation as the scheduled job (administrators only).</summary>
    [HttpPost("run")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(ReminderRunResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReminderRunResult>> RunAsync([FromServices] ServiceAccountReminderJob job, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RunRemindersAsync(User, ServiceAccountReplies.Context(this), job, cancellationToken));
}
