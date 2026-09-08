using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Api.Controllers;

/// <summary>Independent local In Use workspace; only explicit refresh may read the source.</summary>
[ApiController]
[Route("api/v1/in-use")]
[Authorize(Policy = Policies.CanViewInUse)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class InUseController(InUseService service) : ControllerBase
{
    /// <summary>TEST-only exact-record read-only relationship contract diagnostic; returns aliases, never source values.</summary>
    [HttpPost("{id:guid}/relationship-evidence")]
    [EnableRateLimiting(ApiRateLimits.OperationalRecordRefresh)]
    [Authorize(Policy = Policies.CanRefreshInUse)]
    [Authorize(Policy = Policies.CanViewOperationalRecordDiagnostics)]
    public async Task<ActionResult<System.Text.Json.JsonElement>> DiagnoseAsync(Guid id, InUseDiagnosticRequest request,
        [FromServices] IHostEnvironment environment, CancellationToken token) => !environment.IsEnvironment("Test")
        ? NotFound() : Reply(await service.DiagnoseAsync(User, Context(), id, request, token));
    /// <summary>Queries a bounded persisted page.</summary>
    [HttpGet]
    public async Task<ActionResult<InUsePage>> QueryAsync([FromQuery] InUseQuery query, CancellationToken token) =>
        Reply(await service.QueryAsync(User, Context(), query, token));
    /// <summary>Reads a stored record.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InUseRecord>> GetAsync(Guid id, CancellationToken token) =>
        Reply(await service.GetAsync(User, Context(), id, token));
    /// <summary>Returns approved local assignment candidates.</summary>
    [HttpGet("assignees")]
    [Authorize(Policy = Policies.CanAssignInUse)]
    public async Task<ActionResult<IReadOnlyList<InUseAssignee>>> AssigneesAsync(CancellationToken token) =>
        Reply(await service.AssigneesAsync(User, Context(), token));
    /// <summary>Explicit read-only discovery; never routes records into Jira.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting(ApiRateLimits.OperationalRecordRefresh)]
    [Authorize(Policy = Policies.CanRefreshInUse)]
    public async Task<ActionResult<InUseRefreshState>> RefreshAsync(RefreshInUseRequest request, CancellationToken token) =>
        Reply(await service.RefreshAsync(User, Context(), request, token));
    /// <summary>Version-protected local assignment.</summary>
    [HttpPut("{id:guid}/assignment")]
    [Authorize(Policy = Policies.CanAssignInUse)]
    public async Task<ActionResult<InUseRecord>> AssignAsync(Guid id, AssignInUseRequest request, CancellationToken token) =>
        Reply(await service.AssignAsync(User, Context(), id, request, token));
    /// <summary>Version-protected local review draft.</summary>
    [HttpPut("{id:guid}/draft")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<InUseRecord>> DraftAsync(Guid id, SaveInUseDraftRequest request, CancellationToken token) =>
        Reply(await service.SaveDraftAsync(User, Context(), id, request, token));
    /// <summary>Audited preview and XLSX download bytes for the exact saved review version.</summary>
    [HttpPost("{id:guid}/report")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<InUseReport>> ReportAsync(Guid id, ExportInUseRequest request, CancellationToken token) =>
        Reply(await service.ExportAsync(User, Context(), id, request, token));
    private AccessOperationContext Context() => new("in-use-caller", HttpContext.TraceIdentifier, null);
    private ActionResult<T> Reply<T>(InUseResult<T> result)
    {
        if (result.Error is null)
        { return Ok(result.Value); }
        int status = result.Error switch
        {
            "InUseInvalid" or "InUseAssigneeUnavailable" => 400,
            "InUseNotFound" => 404,
            "InUseConflict" => 409,
            "AccessDenied" or "AccessPending" or "AccessDisabled" or "InUseAssignmentRequired" => 403,
            _ => 503
        };
        return OperationalProblemDetails.Create(status, result.Error, "The In Use operation could not be completed.",
            HttpContext.TraceIdentifier, status == 409 ? "concurrency" : "in-use", status is 409 or 503);
    }
}
