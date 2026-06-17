namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Read-only identity directory provider.
/// </summary>
public interface IIdentityDirectoryProvider
{
    /// <summary>
    /// Looks up a user by exact account value.
    /// </summary>
    /// <param name="normalizedAccount">Normalized account value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Directory user when found.</returns>
    public Task<DirectoryUserRecord?> FindUserAsync(string normalizedAccount, CancellationToken cancellationToken);
}
