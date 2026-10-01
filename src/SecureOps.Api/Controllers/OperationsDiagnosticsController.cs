using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Api.Controllers;

/// <summary>Protected operational diagnostics, not configuration mutation or source connectivity tests.</summary>
[ApiController, Route("api/v1/diagnostics/operations")]
public sealed class OperationsDiagnosticsController(OperationsDiagnostics diagnostics) : ControllerBase
{
    /// <summary>Returns allowlisted effective composition and read-only file/queue observations.</summary>
    [HttpGet, Authorize(Policy = Policies.CanSystemDiagnostics), ProducesResponseType(typeof(OperationsReadiness), 200)]
    public async Task<ActionResult<OperationsReadiness>> GetAsync(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await diagnostics.InspectAsync(token));
    }
    /// <summary>Safe availability without physical paths, account names, provider hosts or profile contents.</summary>
    [HttpGet("/api/v1/announcements/source/readiness"), Authorize(Policy = Policies.CanDraftAnnouncements)]
    [ProducesResponseType(typeof(AnnouncementSourceReadiness), 200)]
    public async Task<ActionResult<AnnouncementSourceReadiness>> SourceAsync(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await diagnostics.SourceAsync(token));
    }
}
