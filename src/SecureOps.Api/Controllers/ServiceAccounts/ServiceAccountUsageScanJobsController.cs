using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Security;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>
/// Web usage scans (ADR-0024 R2-4, USAGE-SCAN-JOB-CONTRACT.md): a person with Work and the responsible basis on every account starts a
/// read-only scan that the Worker runs through the JEA endpoint. The API only stores and queues the job and reads its progress; it never
/// contacts a server. Each server's document is validated by the same parser as an uploaded scan; the result is ordinary scan evidence
/// (<c>tool = Jea</c>). "Bulunamadı" never means "kullanılmıyor"; a server without an answer is "bilgi yok".
/// </summary>
[ApiController]
[Route("api/v1/service-accounts")]
[Authorize(Policy = ServiceAccountPolicies.View)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountUsageScanJobsController(ServiceAccountService service) : ControllerBase
{
    /// <summary>Whether a web scan can start on this installation and, if not, why (fail closed: no collector means off).</summary>
    [HttpGet("usage-scan-jobs/availability")]
    [ProducesResponseType(typeof(UsageScanJobAvailabilityView), StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageScanJobAvailabilityView>> AvailabilityAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.UsageScanJobAvailabilityAsync(User, Context(), cancellationToken));

    /// <summary>
    /// Starts a web scan of 1–20 accounts on 1–500 planned servers (optional expected gMSA = gMSA check). At most one queued or running scan
    /// per account (409 <c>ServiceAccountUsageScanAlreadyRunning</c>); out of scope is 404.
    /// </summary>
    [HttpPost("usage-scan-jobs")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(UsageScanJobView), StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageScanJobView>> StartAsync(StartUsageScanJobRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.StartUsageScanJobAsync(User, Context(), request, cancellationToken));

    /// <summary>Live state of a scan; <c>after</c> = the last <c>lastSeq</c> received, so only newly found items come back.</summary>
    [HttpGet("usage-scan-jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(UsageScanJobView), StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageScanJobView>> GetAsync(Guid jobId, [FromQuery] int after = 0, CancellationToken cancellationToken = default) =>
        ServiceAccountReplies.Reply(this, await service.UsageScanJobAsync(User, Context(), jobId, after, cancellationToken));

    /// <summary>Asks a running scan to stop (the starter, or an administrator); finished servers stay as evidence.</summary>
    [HttpPost("usage-scan-jobs/{jobId:guid}/cancel")]
    [ProducesResponseType(typeof(UsageScanJobView), StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageScanJobView>> CancelAsync(Guid jobId, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CancelUsageScanJobAsync(User, Context(), jobId, cancellationToken));

    /// <summary>Starts a new scan of only the servers a finished scan could not read completely.</summary>
    [HttpPost("usage-scan-jobs/{jobId:guid}/retry")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(UsageScanJobView), StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageScanJobView>> RetryAsync(Guid jobId, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RetryUsageScanJobAsync(User, Context(), jobId, cancellationToken));

    /// <summary>The newest web scans of one account (same scope as the account detail).</summary>
    [HttpGet("accounts/{id:guid}/usage-scan-jobs")]
    [ProducesResponseType(typeof(IReadOnlyList<UsageScanJobSummaryView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UsageScanJobSummaryView>>> AccountJobsAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.AccountUsageScanJobsAsync(User, Context(), id, cancellationToken));

    private Infrastructure.Access.AccessOperationContext Context() => ServiceAccountReplies.Context(this);
}
