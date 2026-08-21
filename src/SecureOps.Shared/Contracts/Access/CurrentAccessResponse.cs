namespace SecureOps.Shared.Contracts.Access;

/// <summary>Safe access projection for the authenticated principal.</summary>
public sealed record CurrentAccessResponse(
    Guid UserId,
    string AccessStatus,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities,
    Guid? PendingRequestId,
    string AuthenticationSource,
    SessionPolicyResponse SessionPolicy,
    AccessIdentityProfileResponse? Profile = null,
    AccessRequestResponse? LatestRequest = null,
    long Version = 0);
