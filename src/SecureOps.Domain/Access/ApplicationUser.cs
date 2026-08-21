namespace SecureOps.Domain.Access;

/// <summary>Authentication-independent SecureOps application user.</summary>
public sealed record ApplicationUser(
    Guid Id,
    string CorporateIdentity,
    string AuthenticationSource,
    AccessStatus Status,
    DateTimeOffset FirstAuthenticatedAt,
    DateTimeOffset LastAuthenticatedAt,
    DateTimeOffset? DisabledAt,
    long Version,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities);
