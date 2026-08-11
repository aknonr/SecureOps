namespace SecureOps.Shared.Contracts.Identity;

/// <summary>Bounded request for multiple exact identity lookups.</summary>
public sealed record BulkIdentityLookupRequest(
    IReadOnlyCollection<string>? Accounts,
    string Purpose,
    Guid? AlertId = null,
    string? TuruncuhatEvtId = null);
