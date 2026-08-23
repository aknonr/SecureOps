namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Read-only provider primitives for bounded directory enrichment.</summary>
public interface IDirectoryEnrichmentProvider
{
    /// <summary>Provider name used for cache partitioning.</summary>
    public string ProviderName { get; }

    /// <summary>Returns exact account evidence or null when the principal is unknown.</summary>
    public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(
        string normalizedAccount,
        int maxSpns,
        CancellationToken cancellationToken);

    /// <summary>Returns bounded one-edge groups for an exact principal.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
        string normalizedAccount,
        int maxResults,
        CancellationToken cancellationToken);

    /// <summary>Returns exact group metadata or null.</summary>
    public Task<DirectoryGroupRecord?> FindGroupAsync(
        string normalizedGroup,
        CancellationToken cancellationToken);

    /// <summary>Returns bounded groups that directly contain the supplied provider record.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
        DirectoryGroupRecord group,
        int maxResults,
        CancellationToken cancellationToken);
}
