#pragma warning disable CA1416

using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Read-only Active Directory identity provider.
/// </summary>
public sealed class ActiveDirectoryIdentityDirectoryProvider : IIdentityDirectoryProvider
{
    private readonly IdentityLookupOptions _options;
    private readonly IActiveDirectoryLookupClient _client;

    /// <summary>
    /// Initializes a new Active Directory provider.
    /// </summary>
    /// <param name="options">Identity lookup options.</param>
    /// <param name="client">Read-only Active Directory transport.</param>
    public ActiveDirectoryIdentityDirectoryProvider(IOptions<IdentityLookupOptions> options, IActiveDirectoryLookupClient client)
    {
        _options = options.Value;
        _client = client;
    }

    /// <inheritdoc />
    public bool SupportsUpnLookup => _options.EnableUpnLookup;

    /// <inheritdoc />
    public async Task<DirectoryUserRecord?> FindUserAsync(string normalizedAccount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IdentityProviderInputGuard.EnsureSafeExactAccount(normalizedAccount, _options);

        var timeout = TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds <= 0 ? 3 : _options.ProviderTimeoutSeconds);

        DirectoryUserRecord? user = await _client.FindBySamAccountNameAsync(normalizedAccount, cancellationToken).WaitAsync(timeout, cancellationToken);
        return user ?? (SupportsUpnLookup && normalizedAccount.Contains('@', StringComparison.Ordinal)
            ? await _client.FindByUserPrincipalNameAsync(normalizedAccount, cancellationToken).WaitAsync(timeout, cancellationToken)
            : null);
    }
}

#pragma warning restore CA1416
