using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>Weekly/manager reporting from one metric implementation; exports only from immutable snapshots.</summary>
[ApiController]
[Route("api/v1/service-accounts/reports")]
[Authorize(Policy = ServiceAccountPolicies.Report)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountReportsController(ServiceAccountService service) : ControllerBase
{
    /// <summary>Live report for the Istanbul week containing weekStart, cut at asOf, filtered in SQL by scope/organization/team.</summary>
    [HttpGet("weekly")]
    [ProducesResponseType(typeof(ServiceAccountReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceAccountReport>> WeeklyAsync([FromQuery] WeeklyReportQuery query, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.WeeklyReportAsync(User, ServiceAccountReplies.Context(this), query, cancellationToken));

    /// <summary>Creates an immutable snapshot (the copy that is sent).</summary>
    [HttpPost("snapshots")]
    [ProducesResponseType(typeof(SnapshotItem), StatusCodes.Status200OK)]
    public async Task<ActionResult<SnapshotItem>> CreateSnapshotAsync(CreateSnapshotRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateSnapshotAsync(User, ServiceAccountReplies.Context(this), request, cancellationToken));

    /// <summary>Snapshots within the caller's scope.</summary>
    [HttpGet("snapshots")]
    [ProducesResponseType(typeof(IReadOnlyList<SnapshotItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SnapshotItem>>> SnapshotsAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SnapshotsAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>The unchanged stored payload of a snapshot.</summary>
    [HttpGet("snapshots/{id:guid}")]
    [ProducesResponseType(typeof(ServiceAccountReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceAccountReport>> SnapshotAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SnapshotAsync(User, ServiceAccountReplies.Context(this), id, cancellationToken));

    /// <summary>XLSX or PDF rendered from the same stored snapshot payload.</summary>
    [HttpGet("snapshots/{id:guid}/{format}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(Guid id, string format, CancellationToken cancellationToken)
    {
        SaResult<ReportExport> result = await service.ExportSnapshotAsync(User, ServiceAccountReplies.Context(this), id, format, cancellationToken);
        return result.IsSuccess ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : ServiceAccountReplies.Reply(this, result).Result!;
    }
}
