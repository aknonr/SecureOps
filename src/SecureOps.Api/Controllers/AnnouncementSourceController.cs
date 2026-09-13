using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Api.Controllers;

/// <summary>
/// Owner-scoped maintenance-profile and source-job routes. Submission enqueues durable work and returns
/// immediately; no corporate source is contacted and no legacy script is executed inside a request.
/// Applying a snapshot is always an explicit, version-checked, reviewed operation.
/// </summary>
[ApiController, Route("api/v1/announcements"), Authorize(Policy = Policies.CanDraftAnnouncements), Produces("application/json")]
public sealed class AnnouncementSourceController(AnnouncementSourceService service) : ControllerBase
{
    /// <summary>Lists the protected maintenance-profile allowlist and its configuration state.</summary>
    [HttpGet("source/profiles"), ProducesResponseType(typeof(IReadOnlyList<MaintenanceProfileChoice>), 200)]
    public Task<IActionResult> ProfilesAsync(CancellationToken token = default) =>
        RespondAsync(context => service.ProfilesAsync(User, context, token));

    /// <summary>Submits one explicitly authorized retrieval; a repeated submissionKey returns the same job.</summary>
    [HttpPost("{id:guid}/source/jobs"), Consumes("application/json"), RequestSizeLimit(4096)]
    [ProducesResponseType(typeof(AnnouncementSourceJobStatus), 202)]
    public Task<IActionResult> SubmitAsync(Guid id, AnnouncementSourceSubmission submission, CancellationToken token = default) =>
        RespondAsync(context => service.SubmitAsync(User, context, id, submission, token), 202);

    /// <summary>Reads durable job status. Omit jobId to read the latest owned job for this draft.</summary>
    [HttpGet("{id:guid}/source/jobs"), ProducesResponseType(typeof(AnnouncementSourceJobStatus), 200)]
    public Task<IActionResult> StatusAsync(Guid id, Guid jobId = default, CancellationToken token = default) =>
        RespondAsync(context => service.StatusAsync(User, context, id, jobId, token));

    /// <summary>Returns the reviewable difference between one completed snapshot and the live draft.</summary>
    [HttpGet("{id:guid}/source/jobs/{jobId:guid}/proposal"), ProducesResponseType(typeof(AnnouncementSourceProposal), 200)]
    public Task<IActionResult> ProposalAsync(Guid id, Guid jobId, CancellationToken token = default) =>
        RespondAsync(context => service.ProposalAsync(User, context, id, jobId, token));

    /// <summary>Applies reviewed source values as a new draft revision under the expected version.</summary>
    [HttpPost("{id:guid}/source/apply"), Consumes("application/json"), RequestSizeLimit(8192)]
    [ProducesResponseType(typeof(AnnouncementSourceApplyResult), 200)]
    public Task<IActionResult> ApplyAsync(Guid id, AnnouncementSourceApply request, CancellationToken token = default) =>
        RespondAsync(context => service.ApplyAsync(User, context, id, request, token));

    private async Task<IActionResult> RespondAsync(Func<AccessOperationContext, Task<AnnouncementSourceOutcome>> operation, int success = 200)
    {
        Response.Headers.CacheControl = "no-store";
        AnnouncementSourceOutcome result = await operation(
            new AccessOperationContext("announcement-source", HttpContext.TraceIdentifier, null));
        if (result.Error is not null)
        {
            int status = result.Error switch
            {
                "AccessDenied" => 403,
                "AnnouncementNotFound" or "AnnouncementSourceJobNotFound" => 404,
                "AnnouncementConflict" or "AnnouncementSourceStale" or "AnnouncementSourceOverrideConflict" => 409,
                "AnnouncementSourceInvalid" or "AnnouncementInvalid" or "AnnouncementIncomplete" => 400,
                _ => 503
            };
            return new ObjectResult(new ProblemDetails
            {
                Status = status,
                Title = "Announcement source operation could not be completed.",
                Extensions =
                {
                    ["code"] = result.Error,
                    ["fields"] = result.Fields,
                    ["correlationId"] = HttpContext.TraceIdentifier
                }
            })
            { StatusCode = status };
        }
        object payload = (object?)result.Profiles ?? (object?)result.Status ?? (object?)result.Proposal ?? result.Applied!;
        return new ObjectResult(payload) { StatusCode = result.Status is not null ? success : 200 };
    }
}
