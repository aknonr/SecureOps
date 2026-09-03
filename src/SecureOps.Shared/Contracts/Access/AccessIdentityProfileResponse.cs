namespace SecureOps.Shared.Contracts.Access;

/// <summary>Safe nullable identity enrichment supplied by the configured exact-match provider.</summary>
public sealed record AccessIdentityProfileResponse(
    string? DisplayName,
    string? Account,
    string? Email,
    string? Department,
    string? Title,
    string? Uid = null);
