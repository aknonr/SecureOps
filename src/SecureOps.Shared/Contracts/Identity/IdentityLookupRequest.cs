namespace SecureOps.Shared.Contracts.Identity;

/// <summary>
/// Request for exact privileged account identity lookup.
/// </summary>
/// <param name="Account">Exact PAM account or AD username.</param>
/// <param name="Purpose">Optional operational context for the read-only lookup.</param>
/// <param name="AlertId">Deprecated optional legacy event reference; new clients should omit it.</param>
/// <param name="TuruncuhatEvtId">Deprecated optional legacy event reference; new clients should omit it.</param>
public sealed record IdentityLookupRequest(
    string Account,
    string? Purpose = null,
    Guid? AlertId = null,
    string? TuruncuhatEvtId = null);
