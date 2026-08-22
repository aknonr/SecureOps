namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Read-only Active Directory transport for exact group queries.</summary>
public interface IActiveDirectoryGroupClient
{
    /// <summary>Returns direct groups for an exact sAMAccountName.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsBySamAccountNameAsync(
        string account, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken);
    /// <summary>Returns direct groups for an exact UPN.</summary>
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsByUpnAsync(
        string userPrincipalName, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken);
    /// <summary>Returns exact group metadata.</summary>
    public Task<DirectoryGroupRecord?> FindGroupAsync(string group, CancellationToken cancellationToken);
    /// <summary>Returns one exact group's direct members.</summary>
    public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(
        string group, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken);
}
