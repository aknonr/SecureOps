namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Read-only Active Directory transport primitives for Phase 2 enrichment.</summary>
public interface IActiveDirectoryEnrichmentClient
{
    /// <summary>Returns exact sAMAccountName account evidence.</summary>
    public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalBySamAccountNameAsync(
        string account,
        int maxSpns,
        CancellationToken cancellationToken);

    /// <summary>Returns exact UPN account evidence.</summary>
    public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalByUpnAsync(
        string userPrincipalName,
        int maxSpns,
        CancellationToken cancellationToken);

    /// <summary>Returns direct groups for an exact sAMAccountName.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalMembershipGroupsBySamAccountNameAsync(
        string account,
        int maxResults,
        CancellationToken cancellationToken);

    /// <summary>Returns direct groups for an exact UPN.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalMembershipGroupsByUpnAsync(
        string userPrincipalName,
        int maxResults,
        CancellationToken cancellationToken);

    /// <summary>Returns exact group metadata.</summary>
    public Task<DirectoryGroupRecord?> FindEnrichmentGroupAsync(
        string group,
        CancellationToken cancellationToken);

    /// <summary>Returns groups that directly contain the supplied exact provider record.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
        DirectoryGroupRecord group,
        int maxResults,
        CancellationToken cancellationToken);
}
