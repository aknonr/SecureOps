namespace SecureOps.Shared.Contracts.Announcements;

/// <summary>Latest owned revision metadata, never body/recipients/assets. Null count means legacy unknown.</summary>
public sealed record AnnouncementSummary(Guid Id, long Version, string OcoReference, string Subject,
    string WorkStart, string WorkEnd, DateTimeOffset UpdatedAt, int? MissingFieldCount);

/// <summary>One-based, SQL-filtered page; different requests are not a frozen snapshot.</summary>
public sealed record AnnouncementPage(IReadOnlyList<AnnouncementSummary> Items, int Page, int PageSize, int Total);

/// <summary>Allowlisted choice. Presence does not attest image validity or a draft's stored hash.</summary>
public sealed record AnnouncementBanner(string Revision, string Label, string State);
