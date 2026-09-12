using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Auth;

namespace SecureOps.Api.Controllers;

/// <summary>Local owned drafts. GET format is draft, html or eml; no sending route exists.</summary>
[ApiController, Route("api/v1/announcements"), Authorize(Policy = Policies.CanDraftAnnouncements), Produces("application/json")]
public sealed class AnnouncementsController(AnnouncementService service) : ControllerBase
{
    /// <summary>Reads stored content or a version-bound safe preview/email representation.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnnouncementContent), 200)]
    public Task<IActionResult> GetAsync(Guid id, long version = 0, string format = "draft", CancellationToken token = default) =>
        ExecuteAsync(id, version, format, null, token);

    /// <summary>Creates at version zero or appends after a matching version; returns new ETag.</summary>
    [HttpPut("{id:guid}"), RequestSizeLimit(131072), Consumes("application/json")]
    [ProducesResponseType(typeof(AnnouncementContent), 200)]
    public Task<IActionResult> SaveAsync(Guid id, AnnouncementContent content, long version = 0, CancellationToken token = default) =>
        ExecuteAsync(id, version, "save", content, token);

    private async Task<IActionResult> ExecuteAsync(Guid id, long version, string format, AnnouncementContent? content, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        AnnouncementOutcome result = await service.ExecuteAsync(User, new AccessOperationContext("announcement", HttpContext.TraceIdentifier, null), id, version, format, content, token);
        if (result.Error is not null)
        {
            int status = result.Error switch
            {
                "AccessDenied" => 403,
                "AnnouncementNotFound" => 404,
                "AnnouncementConflict" or "AnnouncementAssetChanged" => 409,
                "AnnouncementInvalid" or "AnnouncementIncomplete" => 400,
                _ => 503
            };
            return new ObjectResult(new ProblemDetails
            {
                Status = status,
                Title = "Announcement operation could not be completed.",
                Extensions = { ["code"] = result.Error, ["fields"] = result.Fields, ["correlationId"] = HttpContext.TraceIdentifier }
            })
            { StatusCode = status };
        }
        Response.Headers.ETag = $"\"{result.Draft!.Version}\"";
        Response.Headers["X-Announcement-Origin"] = result.Draft.Origin;
        if (result.Email is not null)
        { return File(result.Email, "message/rfc822", $"announcement-{id:N}-v{result.Draft.Version}.eml"); }
        if (result.Html is not null)
        {
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; sandbox";
            return Content(result.Html, "text/html", System.Text.Encoding.UTF8);
        }
        return Ok(result.Draft.Content);
    }
}
