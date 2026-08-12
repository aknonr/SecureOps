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
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities);
