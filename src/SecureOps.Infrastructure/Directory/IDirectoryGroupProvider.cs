namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Read-only provider boundary for exact directory group queries.</summary>
public interface IDirectoryGroupProvider
{
    /// <summary>Provider name used only for bounded cache partitioning.</summary>
    public string ProviderName { get; }
    /// <summary>Whether exact UPN principal lookup is effective.</summary>
    public bool SupportsUpnLookup { get; }
    /// <summary>Returns one principal's direct groups or null when the principal is unknown.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(
        string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken);
    /// <summary>Returns exact group metadata or null.</summary>
    public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken);
    /// <summary>Returns one exact group's direct members or null when the group is unknown.</summary>
    public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(
        string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken);
}
