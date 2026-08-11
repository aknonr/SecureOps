namespace SecureOps.Infrastructure.Identity;

/// <summary>Read-only transport boundary for exact Active Directory identity queries.</summary>
public interface IActiveDirectoryLookupClient
{
    /// <summary>Finds one exact sAMAccountName.</summary>
    public Task<DirectoryUserRecord?> FindBySamAccountNameAsync(string account, CancellationToken cancellationToken);

    /// <summary>Finds one exact user principal name.</summary>
    public Task<DirectoryUserRecord?> FindByUserPrincipalNameAsync(string userPrincipalName, CancellationToken cancellationToken);
}
