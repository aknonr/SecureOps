namespace SecureOps.Shared.Contracts.Access;

/// <summary>Backend-owned administrative access-user projection.</summary>
public sealed record AccessUserResponse(
    Guid UserId,
    string CorporateIdentity,
    AccessIdentityProfileResponse? Profile,
    string AccessStatus,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities,
    AccessRequestResponse? LatestRequest,
    IReadOnlyList<AccessRequestResponse> RequestHistory,
    long Version,
    string AuthenticationSource,
    DateTimeOffset FirstAuthenticatedAt,
    DateTimeOffset LastAuthenticatedAt,
    DateTimeOffset? DisabledAt);
