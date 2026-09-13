using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Preparation outcome, without transport state.</summary>
public sealed record PreparationOutcome(PreparedAnnouncement? Snapshot = null, PreparationPage? Page = null, string? Error = null, string[]? Fields = null);
public sealed partial class AnnouncementService
{
    /// <summary>Owner/capability checks precede every snapshot read, list or explicit creation.</summary>
    public async Task<PreparationOutcome> PreparationAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, Guid? draftId, long version, int page, int pageSize, CancellationToken token)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, token);
            if (!current.IsSuccess || current.Value!.User.Status != AccessStatus.Approved
                || !current.Value.User.Capabilities.Contains(Capabilities.AnnouncementDrafts))
            { return new(Error: "AccessDenied"); }
            if (!options.Value.Enabled)
            { return new(Error: "AnnouncementsDisabled"); }
            ApplicationUser owner = current.Value.User;
            if (draftId is not null)
            {
                if (id == Guid.Empty || draftId == Guid.Empty || version < 1)
                { return new(Error: "AnnouncementInvalid"); }
                AnnouncementOutcome saved = await ExecuteAsync(principal, context, draftId.Value, version, "prepare", null, 1, 25, token);
                if (saved.Error is not null)
                { return new(Error: saved.Error, Fields: saved.Fields); }
                if (saved.Draft!.Sender != options.Value.Sender)
                { return new(Error: "AnnouncementConflict"); }
                string label = new[] { owner.DisplayName, owner.LoginName }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "WASAS kullanıcısı";
                Dictionary<string, string> revisions = saved.Draft.TemplateRevision == "oco-v1"
                    ? new() { ["banner"] = saved.Draft.Content.BannerRevision } : new(options.Value.Bundles[saved.Draft.Content.BannerRevision].Assets);
                var snapshot = new PreparedAnnouncement(id, saved.Draft, DateTimeOffset.UtcNow, label, "", saved.Html!, saved.Email!, revisions);
                snapshot = snapshot with { Fingerprint = PreparationFingerprint(snapshot) };
                return await store.PrepareAsync(snapshot, context.CorrelationId, token);
            }
            if (id == Guid.Empty)
            {
                if (page is < 1 or > 10000 || pageSize is < 1 or > 100)
                { return new(Error: "AnnouncementInvalid"); }
                PreparationPage found = await store.PreparationPageAsync(owner.Id, page, pageSize, context.CorrelationId, token);
                return new(Page: found);
            }
            PreparedAnnouncement? stored = await store.PreparedAsync(id, owner.Id, token);
            if (stored is null)
            { return new(Error: "AnnouncementNotFound"); }
            if (stored.Fingerprint != PreparationFingerprint(stored))
            { return new(Error: "AnnouncementUnavailable"); }
            await store.PreparationReadAuditAsync(stored, context.CorrelationId, token);
            return new(stored);
        }
        catch (Exception ex) when (ex is DbException or IOException or InvalidOperationException or JsonException)
        { return new(Error: "AnnouncementUnavailable"); }
    }
    /// <summary>Server snapshot identity includes exact MIME, HTML, metadata and original draft; not a browser assertion.</summary>
    public static string PreparationFingerprint(PreparedAnnouncement snapshot) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot with { Fingerprint = "" })));
}
