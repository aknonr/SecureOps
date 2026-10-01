using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Provider-neutral Active Directory enrichment adapter.</summary>
public sealed class ActiveDirectoryDirectoryEnrichmentProvider : IDirectoryEnrichmentProvider
{
    private readonly IdentityLookupOptions _options;
    private readonly IActiveDirectoryEnrichmentClient _client;

    /// <summary>Initializes the adapter.</summary>
    public ActiveDirectoryDirectoryEnrichmentProvider(
        IOptions<IdentityLookupOptions> options,
        IActiveDirectoryEnrichmentClient client)
    {
        _options = options.Value;
        _client = client;
    }

    /// <inheritdoc />
    public string ProviderName => "ActiveDirectory";

    /// <inheritdoc />
    public async Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(
        string normalizedAccount,
        int maxSpns,
        CancellationToken cancellationToken)
    {
        IdentityProviderInputGuard.EnsureSafeExactAccount(normalizedAccount, _options);
        DirectoryPrincipalEnrichmentRecord? result = await _client.FindPrincipalBySamAccountNameAsync(
            normalizedAccount, maxSpns, cancellationToken);
        if (result is null && SupportsUpn(normalizedAccount))
        {
            result = await _client.FindPrincipalByUpnAsync(normalizedAccount, maxSpns, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
        string normalizedAccount,
        int maxResults,
        CancellationToken cancellationToken)
    {
        IdentityProviderInputGuard.EnsureSafeExactAccount(normalizedAccount, _options);
        DirectoryProviderPage<DirectoryGroupRecord>? result =
            await _client.GetPrincipalMembershipGroupsBySamAccountNameAsync(
                normalizedAccount, maxResults, cancellationToken);
        if (result is null && SupportsUpn(normalizedAccount))
        {
            result = await _client.GetPrincipalMembershipGroupsByUpnAsync(
                normalizedAccount, maxResults, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<DirectoryGroupRecord?> FindGroupAsync(
        string normalizedGroup,
        CancellationToken cancellationToken) =>
        _client.FindEnrichmentGroupAsync(normalizedGroup, cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
        DirectoryGroupRecord group,
        int maxResults,
        CancellationToken cancellationToken) =>
        _client.GetParentGroupsAsync(group, maxResults, cancellationToken);

    private bool SupportsUpn(string account) =>
        _options.EnableUpnLookup && account.Contains('@', StringComparison.Ordinal);
}
