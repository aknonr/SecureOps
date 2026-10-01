using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MimeKit;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Domain.Commands;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements.Mail;

/// <summary>Typed mail operation result, separate from preparation/download.</summary>
public sealed record AnnouncementMailResult(AnnouncementMailPreview? Preview = null,
    AnnouncementMailStatus? Status = null, IReadOnlyList<AnnouncementMailStatus>? History = null, string? Error = null);

/// <summary>Resolves all actors and destinations server-side, then freezes an explicit expiring confirmation.</summary>
public sealed class AnnouncementMailService(IApplicationAccessService access, SqlAnnouncementStore preparations,
    SqlAnnouncementMailStore commands, AnnouncementMailPolicy policy, IAnnouncementMailDispatcher dispatcher,
    IAnnouncementMailPreviewCodec protection, IOptions<AnnouncementOptions> announcements)
{
    private readonly IAnnouncementMailPreviewCodec _protector = protection;

    /// <summary>Reads a current immutable preparation and returns an exact, protected ten-minute preview.</summary>
    public async Task<AnnouncementMailResult> PreviewAsync(ClaimsPrincipal principal, AccessOperationContext context,
        AnnouncementMailPreviewRequest request, CancellationToken token)
    {
        try
        {
            ApplicationUser? actor = await ActorAsync(principal, context, request.Kind, token);
            if (actor is null)
            { return new(Error: "AccessDenied"); }
            if (!dispatcher.IsConfigured)
            { return new(Error: "AnnouncementMailWorkerUnavailable"); }
            var commandId = Guid.NewGuid();
            DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(10);
            BuildResult built = await BuildAsync(actor, request.PreparationId, request.Kind, commandId, context.CorrelationId, token);
            if (built.Error is not null)
            { return new(Error: built.Error); }
            AnnouncementMailIntent intent = built.Intent!;
            string protectedToken = _protector.Protect(JsonSerializer.Serialize(new PreviewEnvelope(commandId,
                request.PreparationId, request.Kind, expires, Hash(JsonSerializer.Serialize(intent)), context.CorrelationId)));
            return new(Preview: new(commandId, intent.PreparationId, intent.DraftId, intent.DraftVersion, intent.Kind,
                intent.Sender, intent.EnvelopeSender, intent.To, intent.Cc, intent.Subject, intent.MessageId,
                intent.MessageHash, expires, protectedToken)
            {
                OcoReference = built.Prepared!.Draft.Content.OcoReference,
                WorkStart = built.Prepared.Draft.Content.WorkStart,
                WorkEnd = built.Prepared.Draft.Content.WorkEnd,
                PreparedAt = built.Prepared.PreparedAt
            });
        }
        catch (Exception exception) when (Unavailable(exception)) { return new(Error: "AnnouncementMailUnavailable"); }
    }

    /// <summary>Persist intent/audit before enqueue; a repeated token reads the original result without another send.</summary>
    public async Task<AnnouncementMailResult> ConfirmAsync(ClaimsPrincipal principal, AccessOperationContext context,
        AnnouncementMailConfirmation request, CancellationToken token)
    {
        try
        {
            if (request.PreviewToken is not { Length: > 0 and <= 8192 })
            { return new(Error: "AnnouncementMailInvalid"); }
            PreviewEnvelope? preview;
            try
            { preview = JsonSerializer.Deserialize<PreviewEnvelope>(_protector.Unprotect(request.PreviewToken)); }
            catch (Exception exception) when (exception is CryptographicException or JsonException) { return new(Error: "AnnouncementMailPreviewExpired"); }
            if (preview is null)
            { return new(Error: "AnnouncementMailInvalid"); }
            ApplicationUser? actor = await ActorAsync(principal, context, preview.Kind, token);
            if (actor is null)
            { return new(Error: "AccessDenied"); }
            AnnouncementMailCommand? prior = await commands.GetAsync(preview.CommandId, actor.Id, token);
            string tokenHash = Hash(request.PreviewToken);
            if (prior is not null)
            { return prior.Intent.PreviewToken == tokenHash ? new(Status: Status(prior)) : new(Error: "AnnouncementMailInvalid"); }
            if (preview.ExpiresAt <= DateTimeOffset.UtcNow)
            { return new(Error: "AnnouncementMailPreviewExpired"); }
            if (!dispatcher.IsConfigured)
            { return new(Error: "AnnouncementMailWorkerUnavailable"); }
            BuildResult built = await BuildAsync(actor, preview.PreparationId, preview.Kind, preview.CommandId, preview.CorrelationId, token);
            if (built.Error is not null)
            { return new(Error: built.Error); }
            if (Hash(JsonSerializer.Serialize(built.Intent)) != preview.InputHash)
            { return new(Error: "AnnouncementMailPreviewChanged"); }
            (AnnouncementMailCommand? command, string? error) = await commands.CreateAsync(built.Intent! with { PreviewToken = tokenHash }, built.Bytes!, token);
            if (error is not null)
            { return new(Status: command is null ? null : Status(command), Error: error); }
            try
            { dispatcher.Enqueue(command!.Intent.CommandId); }
            catch (Exception exception) when (exception is DbException or InvalidOperationException or Hangfire.BackgroundJobClientException)
            { /* Durable Queued intent is recovered by the same Worker; never repeat the SMTP effect here. */ }
            return new(Status: Status(command!));
        }
        catch (Exception exception) when (Unavailable(exception)) { return new(Error: "AnnouncementMailUnavailable"); }
    }

    /// <summary>Only the current approved draft owner can inspect bounded mail history.</summary>
    public async Task<AnnouncementMailResult> HistoryAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid draftId, CancellationToken token)
    {
        try
        {
            ApplicationUser? actor = await ActorAsync(principal, context, null, token);
            if (actor is null)
            { return new(Error: "AccessDenied"); }
            return new(History: (await commands.ListAsync(actor.Id, draftId, token, context.CorrelationId)).Select(Status).ToArray());
        }
        catch (Exception exception) when (Unavailable(exception)) { return new(Error: "AnnouncementMailUnavailable"); }
    }

    private async Task<ApplicationUser?> ActorAsync(ClaimsPrincipal principal, AccessOperationContext context, string? kind, CancellationToken token)
    {
        AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, token);
        ApplicationUser? actor = current.IsSuccess ? current.Value!.User : null;
        string? capability = kind switch { "SelfTest" => Capabilities.AnnouncementSelfTest, "Send" => Capabilities.AnnouncementSend, null => null, _ => "invalid" };
        return announcements.Value.Enabled && actor is { Status: AccessStatus.Approved }
            && actor.Capabilities.Contains(Capabilities.AnnouncementDrafts)
            && (capability is null || actor.Capabilities.Contains(capability)) ? actor : null;
    }

    private async Task<BuildResult> BuildAsync(ApplicationUser actor, Guid preparationId, string kind, Guid commandId, string correlation, CancellationToken token)
    {
        PreparedAnnouncement? prepared = await preparations.PreparedAsync(preparationId, actor.Id, token);
        if (prepared is null)
        { return new(Error: "AnnouncementNotFound"); }
        if (prepared.Fingerprint != AnnouncementService.PreparationFingerprint(prepared))
        { return new(Error: "AnnouncementUnavailable"); }
        if (!AnnouncementValidation.Address(actor.Mail ?? ""))
        { return new(Error: "AnnouncementSenderUnavailable"); }
        if (prepared.Draft.Sender != actor.Mail)
        { return new(Error: "AnnouncementSenderChanged"); }
        AnnouncementDraft? latest = await preparations.GetAsync(prepared.Draft.Id, actor.Id, token);
        if (latest?.Version != prepared.Draft.Version)
        { return new(Error: "AnnouncementConflict"); }
        string[] to = kind == "SelfTest" ? [actor.Mail!] : prepared.Draft.Content.To.ToArray();
        string[] cc = kind == "SelfTest" ? [] : prepared.Draft.Content.Cc.ToArray();
        string? error = policy.Validate(kind, actor.Mail!, [.. to, .. cc]);
        if (error is not null)
        { return new(Error: error); }
        using var original = new MemoryStream(prepared.Email, false);
        using MimeMessage message = await MimeMessage.LoadAsync(original, token);
        message.From.Clear();
        message.From.Add(MailboxAddress.Parse(actor.Mail));
        message.Sender = null;
        message.ReplyTo.Clear();
        message.Bcc.Clear();
        message.ResentFrom.Clear();
        message.ResentTo.Clear();
        message.ResentCc.Clear();
        message.ResentBcc.Clear();
        message.ResentSender = null;
        message.To.Clear();
        message.To.AddRange(to.Select(MailboxAddress.Parse));
        message.Cc.Clear();
        message.Cc.AddRange(cc.Select(MailboxAddress.Parse));
        message.MessageId = commandId.ToString("N") + "@wasas.invalid";
        message.Date = prepared.PreparedAt;
        message.Subject = (kind == "SelfTest" ? "[WASAS DENEME] " : "") + prepared.Draft.Content.Subject;
        using var output = new MemoryStream();
        await message.WriteToAsync(FormatOptions.Default, output, token);
        byte[] bytes = output.ToArray();
        if (bytes.Length > 3_000_000)
        { return new(Error: "AnnouncementMailInvalid"); }
        var intent = new AnnouncementMailIntent(commandId, preparationId, prepared.Draft.Id, prepared.Draft.Version, prepared.Fingerprint,
            kind, new OperationActor(actor.Id, "Human", actor.DisplayName, actor.LoginName, actor.Mail), actor.Version, actor.Mail!, policy.Envelope(actor.Mail!),
            to, cc, message.Subject, message.MessageId, Convert.ToHexString(SHA256.HashData(bytes)), policy.Fingerprint(), correlation, "");
        return new(intent, bytes, Prepared: prepared);
    }

    private static AnnouncementMailStatus Status(AnnouncementMailCommand command) => new(command.Intent.CommandId, command.Intent.PreparationId,
        command.Intent.DraftId, command.Intent.DraftVersion, command.Intent.Kind, command.State, command.Version, command.CreatedAt,
        command.UpdatedAt, command.Intent.Sender, command.Intent.To, command.Intent.Cc, command.Accepted, command.Rejected, command.Intent.MessageId, command.ErrorCode);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Unavailable(Exception exception) => exception is DbException or IOException or InvalidOperationException or JsonException;
    private sealed record PreviewEnvelope(Guid CommandId, Guid PreparationId, string Kind, DateTimeOffset ExpiresAt, string InputHash, string CorrelationId);
    private sealed record BuildResult(AnnouncementMailIntent? Intent = null, byte[]? Bytes = null, string? Error = null,
        PreparedAnnouncement? Prepared = null);
}

/// <summary>Host-owned purpose-isolated protection using the API's existing persistent key ring.</summary>
public interface IAnnouncementMailPreviewCodec
{
    /// <summary>Protects server-created preview state.</summary>
    public string Protect(string value);
    /// <summary>Unprotects state or throws CryptographicException; never trusts browser-authored fields.</summary>
    public string Unprotect(string value);
}
