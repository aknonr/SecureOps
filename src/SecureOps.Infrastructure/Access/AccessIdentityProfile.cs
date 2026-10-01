namespace SecureOps.Infrastructure.Access;

/// <summary>Safe identity fields resolved by an existing exact-match directory provider.</summary>
public sealed record AccessIdentityProfile(
    string? DisplayName,
    string? Account,
    string? Email,
    string? Department,
    string? Title,
    string? Uid = null);
