using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Middleware;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Api.Controllers;

/// <summary>Snapshot extension of existing management reporting; no external operations.</summary>
[ApiController]
[Route("api/v1/reporting/management/workflows")]
[Authorize(Policy = Policies.CanViewManagementReports)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class WorkflowReportsController(WorkflowReportService service) : ControllerBase
{
    /// <summary>Creates an audited local report snapshot, not a source refresh or business command.</summary>
    [HttpPost]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Security.ApiRateLimits.WorkflowReport)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(WorkflowReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowReport>> CaptureAsync(WorkflowReportRequest request, CancellationToken token) =>
        Reply(await service.CaptureAsync(User, Context(), request, token));

    /// <summary>Returns scoped SQL aggregates and a bounded page from an existing cut.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowReport>> ReadAsync(Guid id, [FromQuery] WorkflowReportFilter filter, CancellationToken token) =>
        Reply(await service.ReadAsync(User, Context(), id, filter, false, token));

    /// <summary>Exports the same authorized filtered snapshot as safe XLSX strings.</summary>
    [HttpGet("{id:guid}/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(Guid id, [FromQuery] WorkflowReportFilter filter, CancellationToken token)
    {
        ManagementReportingResult<WorkflowReport> result = await service.ReadAsync(User, Context(), id, filter, true, token);
        return result.IsSuccess ? File(WorkflowReportWorkbook.Create(result.Value!),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"WASAS_IsAkislari_{id:N}.xlsx") : Failure(result.ErrorCode!);
    }
    private AccessOperationContext Context() => new("report-caller", HttpContext.TraceIdentifier, null);
    private ActionResult<WorkflowReport> Reply(ManagementReportingResult<WorkflowReport> result) => result.IsSuccess ? Ok(result.Value) : Failure(result.ErrorCode!);
    private ObjectResult Failure(string error)
    {
        int status = error switch { "AccessDenied" => 403, "ReportingValidationFailed" or "ReportingLimitExceeded" => 400, "ReportingSnapshotExpired" => 409, _ => 503 };
        string detail = error == "ReportingLimitExceeded" ? "Rapor kapsamı sınırı aşıldı. Yönetici incelemesi gerekiyor; sonuçlar kısaltılmadı."
            : "Rapor okunamadı. Yetkiyi, dönemi ve rapor kesitini kontrol edin; hata sıfır sonuç değildir.";
        return OperationalProblemDetails.Create(status, error, detail,
            HttpContext.TraceIdentifier, "reporting", status is 409 or 503);
    }
}
