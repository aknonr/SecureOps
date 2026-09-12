namespace SecureOps.Domain.Announcements;

/// <summary>Bounded operator-entered plain text, never verified source evidence.</summary>
public sealed record AnnouncementContent(string OcoReference, string Scope, string Subject,
    string AnnouncementDate, string WorkStart, string WorkEnd, string Description, string Impact,
    string Checks, string Notes, string[] To, string[] Cc, string BannerRevision,
    string? RestartStart = null, string? RestartEnd = null);

/// <summary>Immutable local revision; downloading never changes its state into Sent.</summary>
public sealed record AnnouncementDraft(Guid Id, Guid OwnerId, long Version, DateTimeOffset SavedAt,
    AnnouncementContent Content, string Sender, string BannerHash,
    string Origin = "Manual", string TemplateRevision = "oco-v1");
