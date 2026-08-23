using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Provider-neutral Active Directory group adapter.</summary>
public sealed class ActiveDirectoryDirectoryGroupProvider : IDirectoryGroupProvider
{
    private readonly IdentityLookupOptions _identityOptions;
    private readonly IActiveDirectoryGroupClient _client;

    /// <summary>Initializes the provider.</summary>
    public ActiveDirectoryDirectoryGroupProvider(
        IOptions<IdentityLookupOptions> identityOptions,
        IActiveDirectoryGroupClient client)
    {
        _identityOptions = identityOptions.Value;
        _client = client;
    }

    /// <inheritdoc />
    public string ProviderName => "ActiveDirectory";

    /// <inheritdoc />
    public bool SupportsUpnLookup => _identityOptions.EnableUpnLookup;

    /// <inheritdoc />
    public async Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(
        string normalizedAccount,
        int offset,
        int pageSize,
        int resultLimit,
        CancellationToken cancellationToken)
    {
        IdentityProviderInputGuard.EnsureSafeExactAccount(normalizedAccount, _identityOptions);
        DirectoryProviderPage<DirectoryGroupRecord>? result =
            await _client.GetPrincipalDirectGroupsBySamAccountNameAsync(
                normalizedAccount, offset, pageSize, resultLimit, cancellationToken);

        if (result is null && SupportsUpnLookup && normalizedAccount.Contains('@', StringComparison.Ordinal))
        {
            result = await _client.GetPrincipalDirectGroupsByUpnAsync(
                normalizedAccount, offset, pageSize, resultLimit, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) =>
        _client.FindGroupAsync(normalizedGroup, cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(
        string normalizedGroup,
        int offset,
        int pageSize,
        int resultLimit,
        CancellationToken cancellationToken) =>
        _client.GetDirectMembersAsync(normalizedGroup, offset, pageSize, resultLimit, cancellationToken);
}
