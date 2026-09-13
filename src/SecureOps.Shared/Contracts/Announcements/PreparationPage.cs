namespace SecureOps.Shared.Contracts.Announcements;

/// <summary>Owned preparation metadata only; every row is Prepared, never Sent or Queued.</summary>
public sealed record PreparationSummary(Guid Id, string Subject, long Version, DateTimeOffset PreparedAt, string PreparedBy);
/// <summary>One-based SQL page; cap 100, stable timestamp and unique ID ordering.</summary>
public sealed record PreparationPage(IReadOnlyList<PreparationSummary> Items, int Page, int PageSize, int Total);
