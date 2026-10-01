using Microsoft.AspNetCore.Mvc;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Api.Controllers;

public sealed partial class AnnouncementsController
{
    /// <summary>Lists only the caller's immutable preparation metadata, never send results.</summary>
    [HttpGet("preparations"), ProducesResponseType(typeof(PreparationPage), 200)]
    public Task<IActionResult> PreparationsAsync(int page = 1, int pageSize = 25, CancellationToken token = default) =>
        PreparationResultAsync(Guid.Empty, null, 0, page, pageSize, token);
    /// <summary>Creates an exact snapshot of a complete saved revision; ID is an idempotency key, not confirmation.</summary>
    [HttpPut("preparations/{id:guid}"), ProducesResponseType(typeof(PreparedAnnouncement), 200)]
    public Task<IActionResult> PrepareAsync(Guid id, Guid draftId, long version, CancellationToken token = default) =>
        PreparationResultAsync(id, draftId, version, 1, 25, token);
    /// <summary>Reads owned historical content and exact MIME without reloading current assets or sending.</summary>
    [HttpGet("preparations/{id:guid}"), ProducesResponseType(typeof(PreparedAnnouncement), 200)]
    public Task<IActionResult> PreparedAsync(Guid id, CancellationToken token = default) =>
        id == Guid.Empty ? Task.FromResult<IActionResult>(BadRequest(new ProblemDetails { Status = 400, Extensions = { ["code"] = "AnnouncementInvalid" } }))
            : PreparationResultAsync(id, null, 0, 1, 25, token);
    private async Task<IActionResult> PreparationResultAsync(Guid id, Guid? draftId, long version, int page, int pageSize, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        PreparationOutcome result = await service.PreparationAsync(User, new AccessOperationContext("announcement-preparation", HttpContext.TraceIdentifier, null), id, draftId, version, page, pageSize, token);
        if (result.Error is null)
        { return result.Page is not null ? Ok(result.Page) : Ok(result.Snapshot); }
        int status = result.Error switch
        {
            "AccessDenied" => 403,
            "AnnouncementNotFound" => 404,
            "AnnouncementConflict" or "AnnouncementPreparationConflict" or "AnnouncementAssetChanged" or "AnnouncementAssetMissing" => 409,
            "AnnouncementInvalid" or "AnnouncementIncomplete" or "AnnouncementSenderUnavailable" => 400,
            "AnnouncementSenderChanged" => 409,
            _ => 503
        };
        return new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = "Announcement preparation unavailable.",
            Extensions = { ["code"] = result.Error, ["fields"] = result.Fields, ["correlationId"] = HttpContext.TraceIdentifier }
        })
        { StatusCode = status };
    }
}
