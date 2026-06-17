namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Result from account normalization.
/// </summary>
/// <param name="IsValid">Whether normalization succeeded.</param>
/// <param name="NormalizedAccount">Normalized account when valid.</param>
/// <param name="ErrorCode">Machine-readable validation code.</param>
/// <param name="ErrorMessage">Human-readable validation message.</param>
public sealed record IdentityAccountNormalizationResult(
    bool IsValid,
    string? NormalizedAccount,
    string? ErrorCode,
    string? ErrorMessage);
