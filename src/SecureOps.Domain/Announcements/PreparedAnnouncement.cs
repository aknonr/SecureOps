namespace SecureOps.Domain.Announcements;

/// <summary>Exact immutable final-announcement artifact; never an approval or a send intent.</summary>
public sealed record PreparedAnnouncement(Guid Id, AnnouncementDraft Draft, DateTimeOffset PreparedAt,
    string PreparedBy, string Fingerprint, string Html, byte[] Email, IReadOnlyDictionary<string, string> AssetRevisions,
    string ArtifactType = "FinalAnnouncement", string State = "Prepared");
