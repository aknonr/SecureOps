using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Announcements.Mail;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Api.Controllers;

public sealed partial class AnnouncementsController
{
    /// <summary>Exact preparation-bound sender/audience preview, authorized separately for self-test or distribution.</summary>
    [HttpPost("mail/preview"), RequestSizeLimit(16384)]
    [EnableRateLimiting(ApiRateLimits.AnnouncementPreview)]
    [ProducesResponseType(typeof(AnnouncementMailPreview), 200)]
    public async Task<IActionResult> MailPreviewAsync(AnnouncementMailPreviewRequest request,
        [FromServices] AnnouncementMailService mail, CancellationToken token) =>
        MailResult(await mail.PreviewAsync(User, MailContext(), request, token));

    /// <summary>Explicit protected confirmation. Replay returns stored status; it never creates a second send.</summary>
    [HttpPost("mail/confirm"), RequestSizeLimit(16384)]
    [EnableRateLimiting("AnnouncementMailConfirm")]
    [ProducesResponseType(typeof(AnnouncementMailStatus), 200)]
    public async Task<IActionResult> MailConfirmAsync(AnnouncementMailConfirmation request,
        [FromServices] AnnouncementMailService mail, CancellationToken token) =>
        MailResult(await mail.ConfirmAsync(User, MailContext(), request, token));

    /// <summary>Bounded owner-only submission history. SMTP acknowledgment is not inbox delivery.</summary>
    [HttpGet("{draftId:guid}/mail")]
    [ProducesResponseType(typeof(IReadOnlyList<AnnouncementMailStatus>), 200)]
    public async Task<IActionResult> MailHistoryAsync(Guid draftId, [FromServices] AnnouncementMailService mail, CancellationToken token) =>
        MailResult(await mail.HistoryAsync(User, MailContext(), draftId, token));

    private AccessOperationContext MailContext() => new("announcement-mail", HttpContext.TraceIdentifier, null);
    private IActionResult MailResult(AnnouncementMailResult result)
    {
        Response.Headers.CacheControl = "no-store";
        if (result.Error is null)
        { return Ok((object?)result.Preview ?? (object?)result.Status ?? result.History); }
        int status = result.Error switch
        {
            "AccessDenied" => 403,
            "AnnouncementNotFound" => 404,
            "AnnouncementSenderUnavailable" or "AnnouncementMailInvalid" or "AnnouncementMailAudienceInvalid" => 400,
            "AnnouncementMailPreviewExpired" or "AnnouncementMailPreviewChanged" or "AnnouncementConflict"
                or "AnnouncementSenderChanged" or "AnnouncementMailAlreadyRequested" => 409,
            _ => 503
        };
        return new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = "Mail command could not be completed.",
            Extensions = { ["code"] = result.Error, ["correlationId"] = HttpContext.TraceIdentifier }
        })
        { StatusCode = status };
    }
}
