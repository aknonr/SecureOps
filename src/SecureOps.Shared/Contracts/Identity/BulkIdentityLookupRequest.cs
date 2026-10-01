namespace SecureOps.Shared.Contracts.Identity;

/// <summary>Bounded request for multiple exact identity lookups.</summary>
/// <param name="Accounts">Exact account identifiers.</param>
/// <param name="Purpose">Optional operational context for the read-only lookup.</param>
/// <param name="AlertId">Deprecated optional legacy event reference; new clients should omit it.</param>
/// <param name="TuruncuhatEvtId">Deprecated optional legacy event reference; new clients should omit it.</param>
public sealed record BulkIdentityLookupRequest(
    IReadOnlyCollection<string>? Accounts,
    string? Purpose = null,
    Guid? AlertId = null,
    string? TuruncuhatEvtId = null);
