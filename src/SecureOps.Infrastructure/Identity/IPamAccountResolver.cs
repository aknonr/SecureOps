namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Resolves PAM account metadata before directory lookup.
/// </summary>
public interface IPamAccountResolver
{
    /// <summary>
    /// Resolves a normalized account to a directory account.
    /// </summary>
    /// <param name="normalizedAccount">Normalized account from the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>PAM account resolution.</returns>
    public Task<PamAccountResolution> ResolveAsync(string normalizedAccount, CancellationToken cancellationToken);
}
