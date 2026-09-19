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
    /// <summary>Verified RFC reporter suggestion; opening it never assigns anyone.</summary>
    [HttpGet("{id:guid}/reporter-suggestion")]
    [Authorize(Policy = Policies.CanAssignInUse)]
    public async Task<ActionResult<InUseReporterSuggestion>> ReporterAsync(Guid id, CancellationToken token) =>
        Reply(await service.ReporterAsync(User, Context(), id, token));
    /// <summary>Authorized bounded search over verified immutable archive metadata.</summary>
    [HttpGet("reports")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<InUseReportPage>> ReportsAsync([FromQuery] InUseReportQuery query, CancellationToken token) =>
        Reply(await service.ReportsAsync(User, Context(), query, token));
    /// <summary>Explicit bounded historical indexing; preserves original bytes and metadata.</summary>
    [HttpPost("{id:guid}/reports/index")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<IndexInUseReportsResult>> IndexReportsAsync(Guid id, IndexInUseReportsRequest request, CancellationToken token) =>
        Reply(await service.IndexReportsAsync(User, Context(), id, request, token));
    /// <summary>Explicit local reset/discard/restart; never deletes a source record or archive.</summary>
    [HttpPost("{id:guid}/draft-lifecycle")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<InUseRecord>> ChangeDraftAsync(Guid id, ChangeInUseDraftRequest request, CancellationToken token) =>
        Reply(await service.ChangeDraftAsync(User, Context(), id, request, token));
    /// <summary>Structured completion readiness and exact durable step results.</summary>
    [HttpGet("{id:guid}/execution")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<InUseExecutionStatus>> ExecutionAsync(Guid id, CancellationToken token) =>
        Reply(await service.ExecutionStatusAsync(User, Context(), id, token));
    /// <summary>Fresh narrow-authorized durable completion; never a browser-reported outcome.</summary>
    [HttpPost("{id:guid}/execution")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    [Authorize(Policy = Policies.CanCompleteInUse)]
    public async Task<ActionResult<InUseExecution>> ExecuteAsync(Guid id, StartInUseExecutionRequest request, CancellationToken token) =>
        Reply(await service.StartExecutionAsync(User, Context(), id, request, token));
    /// <summary>Immutable server review history and explicit reuse proposals, resolved from trusted source identity.</summary>
    [HttpGet("{id:guid}/servers/{serverId}/history")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<InUseServerHistory>> HistoryAsync(Guid id, string serverId, CancellationToken token,
        string? search = null, int page = 1, int pageSize = 10) =>
        Reply(await service.HistoryAsync(User, Context(), id, serverId, search, page, pageSize, token));
    /// <summary>Management-authorized OR-based summary of stored records.</summary>
    [HttpGet("overview")]
    [Authorize(Policy = Policies.CanViewManagementReports)]
    public async Task<ActionResult<InUseOverview>> OverviewAsync(CancellationToken token) =>
        Reply(await service.OverviewAsync(User, Context(), token));
    /// <summary>Explicit local confirmation of an archived version; external execution remains blocked.</summary>
    [HttpPost("{id:guid}/completion-intent")]
    [Authorize(Policy = Policies.CanReviewInUse)]
    public async Task<ActionResult<InUseRecord>> ConfirmAsync(Guid id, ConfirmInUseRequest request, CancellationToken token) =>
        Reply(await service.ConfirmAsync(User, Context(), id, request, token));
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
    public async Task<ActionResult<IReadOnlyList<InUseAssignee>>> AssigneesAsync(CancellationToken token, string? search = null) =>
        Reply(await service.AssigneesAsync(User, Context(), token, search));
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
    [ProducesResponseType(typeof(InUseReport), StatusCodes.Status200OK)]
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
            "InUseInvalid" or "InUseAssigneeUnavailable" or "InUseIncomplete" => 400,
            "InUseNotFound" => 404,
            "InUseConflict" or "InUsePolicyChanged" => 409,
            "AccessDenied" or "AccessPending" or "AccessDisabled" or "InUseAssignmentRequired" => 403,
            _ => 503
        };
        return OperationalProblemDetails.Create(status, result.Error, result.Detail ?? "The In Use operation could not be completed.",
            HttpContext.TraceIdentifier, result.Error.StartsWith("InUseArchive", StringComparison.Ordinal) ? "report-archive" : status == 409 ? "concurrency" : "in-use", status is 409 or 503);
    }
}
