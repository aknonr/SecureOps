namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Normalizes and validates identity lookup account input.
/// </summary>
public interface IIdentityAccountNormalizer
{
    /// <summary>
    /// Normalizes the supplied account value.
    /// </summary>
    /// <param name="account">Raw account value.</param>
    /// <returns>Normalization result.</returns>
    public IdentityAccountNormalizationResult Normalize(string? account);
}
