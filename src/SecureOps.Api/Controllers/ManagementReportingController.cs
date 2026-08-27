using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Middleware;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Api.Controllers;

/// <summary>Backend-authoritative, capability-protected management reporting.</summary>
[ApiController]
[Route("api/v1/reporting/management")]
[Authorize(Policy = Policies.CanViewManagementReports)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public sealed class ManagementReportingController : ControllerBase
{
    private readonly IManagementReportingService _reportingService;

    /// <summary>Initializes the controller.</summary>
    public ManagementReportingController(IManagementReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    /// <summary>Returns aggregate identity, workflow, adoption, security, and timing metrics.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ManagementReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ManagementReportResponse>> SummaryAsync(
        [FromQuery(Name = "window")] string? selection,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        ManagementReportingResult<ManagementReportResponse> result = await _reportingService.GetSummaryAsync(
            selection,
            from,
            to,
            Context(),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure<ManagementReportResponse>(result.ErrorCode!);
    }

    /// <summary>Returns a paginated cross-user aggregate without directory profile enrichment.</summary>
    [HttpGet("operators")]
    [ProducesResponseType(typeof(OperatorActivityPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OperatorActivityPageResponse>> OperatorsAsync(
        [FromQuery(Name = "window")] string? selection,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ManagementReportingResult<OperatorActivityPageResponse> result = await _reportingService.GetOperatorsAsync(
            selection,
            from,
            to,
            page,
            pageSize,
            Context(),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure<OperatorActivityPageResponse>(result.ErrorCode!);
    }

    private ManagementReportingContext Context() => new(
        User.Identity?.Name ?? "unknown",
        Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        HttpContext.Connection.RemoteIpAddress?.ToString());

    private ActionResult<T> Failure<T>(string errorCode)
    {
        bool validation = errorCode == OperationalErrorCodes.ReportingValidationFailed;
        bool notConfigured = errorCode == OperationalErrorCodes.ReportingPersistenceNotConfigured;
        int status = validation ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable;
        string stage = validation
            ? "validation"
            : errorCode == OperationalErrorCodes.AuditStoreUnavailable ? "audit" : "reporting";
        return OperationalProblemDetails.Create(
            status,
            errorCode,
            validation
                ? "The reporting request is invalid."
                : notConfigured
                    ? "Management reporting persistence is not configured."
                    : "Management reporting is currently unavailable.",
            Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            stage,
            retryable: !validation && !notConfigured);
    }
}
