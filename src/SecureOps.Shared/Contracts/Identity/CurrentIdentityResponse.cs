namespace SecureOps.Shared.Contracts.Identity;

/// <summary>
/// Safe metadata about the current authenticated API caller.
/// </summary>
/// <param name="Name">Authenticated principal name when available.</param>
/// <param name="IsAuthenticated">Whether the caller is authenticated.</param>
/// <param name="CanLookupIdentity">Whether the caller can perform privileged identity lookup.</param>
public sealed record CurrentIdentityResponse(
    string? Name,
    bool IsAuthenticated,
    bool CanLookupIdentity);
