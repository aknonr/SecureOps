using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Api.Controllers;

/// <summary>Local owned drafts. GET format is draft, html or eml; no sending route exists.</summary>
[ApiController, Route("api/v1/announcements"), Authorize(Policy = Policies.CanDraftAnnouncements), Produces("application/json")]
public sealed partial class AnnouncementsController(AnnouncementService service) : ControllerBase
{
    /// <summary>Lists only the caller's latest draft summaries; page 1..10000 and size 1..100.</summary>
    [HttpGet, ProducesResponseType(typeof(AnnouncementPage), 200)]
    public Task<IActionResult> ListAsync(int page = 1, int pageSize = 25, CancellationToken token = default) =>
        ExecuteAsync(Guid.Empty, 0, "list", null, page, pageSize, token);

    /// <summary>Lists bounded allowlisted banner choices without exposing paths or decoding images.</summary>
    [HttpGet("banners"), ProducesResponseType(typeof(IReadOnlyList<AnnouncementBanner>), 200)]
    public Task<IActionResult> BannersAsync(string templateRevision = "oco-v1", CancellationToken token = default) =>
        ExecuteAsync(Guid.Empty, 0, templateRevision == "oco-v1" ? "banners" : templateRevision == "oco-table-v2" ? "bundles" : "invalid", null, 1, 25, token);

    /// <summary>Reads stored content or a version-bound safe preview/email representation.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnnouncementContent), 200)]
    public Task<IActionResult> GetAsync(Guid id, long version = 0, string format = "draft", CancellationToken token = default) =>
        ExecuteAsync(id, version, format is "draft" or "html" or "eml" ? format : "invalid", null, 1, 25, token);

    /// <summary>Creates at version zero or appends after a matching version; returns new ETag.</summary>
    [HttpPut("{id:guid}"), RequestSizeLimit(131072), Consumes("application/json")]
    [ProducesResponseType(typeof(AnnouncementContent), 200)]
    public Task<IActionResult> SaveAsync(Guid id, AnnouncementContent content, long version = 0, CancellationToken token = default) =>
        ExecuteAsync(id, version, "save", content, 1, 25, token);

    /// <summary>Transforms supplied incomplete content only; no stored draft read, save or send.</summary>
    [HttpPost("preview"), RequestSizeLimit(131072), Consumes("application/json"), Produces("text/html")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Security.ApiRateLimits.AnnouncementPreview)]
    [ProducesResponseType(typeof(string), 200)]
    public Task<IActionResult> PreviewAsync(AnnouncementContent content, CancellationToken token = default) =>
        ExecuteAsync(Guid.Empty, 0, "live", content, 1, 25, token);

    private async Task<IActionResult> ExecuteAsync(Guid id, long version, string format, AnnouncementContent? content, int page, int pageSize, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        AnnouncementOutcome result = await service.ExecuteAsync(User, new AccessOperationContext("announcement", HttpContext.TraceIdentifier, null), id, version, format, content, page, pageSize, token);
        if (result.Error is not null)
        {
            int status = result.Error switch
            {
                "AccessDenied" => 403,
                "AnnouncementNotFound" => 404,
                "AnnouncementConflict" or "AnnouncementAssetChanged" or "AnnouncementAssetMissing" => 409,
                "AnnouncementInvalid" or "AnnouncementIncomplete" or "AnnouncementSenderUnavailable" => 400,
                "AnnouncementSenderChanged" => 409,
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
        if (result.Page is not null)
        { return Ok(result.Page); }
        if (result.Banners is not null)
        { return Ok(result.Banners); }
        if (result.Draft is not null)
        {
            Response.Headers.ETag = $"\"{result.Draft.Version}\"";
            Response.Headers["X-Announcement-Origin"] = result.Draft.Origin;
        }
        if (result.Email is not null)
        { return File(result.Email, "message/rfc822", $"announcement-{id:N}-v{result.Draft!.Version}.eml"); }
        if (result.Html is not null)
        {
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; sandbox";
            if (format == "live")
            { Response.Headers["X-Announcement-Incomplete"] = string.Join(",", result.Fields ?? []); }
            return Content(result.Html, "text/html", System.Text.Encoding.UTF8);
        }
        return Ok(result.Draft!.Content);
    }
}
