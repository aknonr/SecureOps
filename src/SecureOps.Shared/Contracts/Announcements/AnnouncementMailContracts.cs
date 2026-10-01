namespace SecureOps.Shared.Contracts.Announcements;

/// <summary>A preparation and an explicit operation kind; sender/audience never come from this request.</summary>
public sealed record AnnouncementMailPreviewRequest(Guid PreparationId, string Kind);
/// <summary>Protected, expiring preview reviewed by a human; replay returns the original command.</summary>
public sealed record AnnouncementMailConfirmation(string PreviewToken);
/// <summary>Exact frozen destination and identity of a reviewed message. Acceptance is not inbox delivery.</summary>
public sealed record AnnouncementMailPreview(Guid CommandId, Guid PreparationId, Guid DraftId, long DraftVersion,
    string Kind, string Sender, string EnvelopeSender, string[] To, string[] Cc, string Subject,
    string MessageId, string MessageHash, DateTimeOffset ExpiresAt, string PreviewToken)
{
    /// <summary>OCO identity from the immutable preparation, not editable send input.</summary>
    public string OcoReference { get; init; } = "";
    /// <summary>Original prepared work-start text, retaining seconds and explicit offset.</summary>
    public string WorkStart { get; init; } = "";
    /// <summary>Original prepared work-end text, retaining seconds and explicit offset.</summary>
    public string WorkEnd { get; init; } = "";
    /// <summary>Time the reviewed immutable preparation was created.</summary>
    public DateTimeOffset PreparedAt { get; init; }
    /// <summary>Distinct actual envelope recipients; self-test is always one.</summary>
    public int RecipientCount => To.Concat(Cc).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}
/// <summary>Owner-visible durable state; no transport credentials or message bytes.</summary>
public sealed record AnnouncementMailStatus(Guid CommandId, Guid PreparationId, Guid DraftId, long DraftVersion,
    string Kind, string State, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string Sender, string[] To, string[] Cc, string[] Accepted, string[] Rejected, string MessageId, string? ErrorCode);
