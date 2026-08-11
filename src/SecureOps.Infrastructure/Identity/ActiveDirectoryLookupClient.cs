#pragma warning disable CA1416
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>Production read-only Active Directory transport using the process identity.</summary>
public sealed class ActiveDirectoryLookupClient : IActiveDirectoryLookupClient
{
    private readonly IdentityLookupOptions _options;
    /// <summary>Initializes the read-only Active Directory transport.</summary>
    public ActiveDirectoryLookupClient(IOptions<IdentityLookupOptions> options) => _options = options.Value;

    /// <inheritdoc />
    public Task<DirectoryUserRecord?> FindBySamAccountNameAsync(string account, CancellationToken cancellationToken) =>
        Task.Run(() => Find(account, IdentityType.SamAccountName), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryUserRecord?> FindByUserPrincipalNameAsync(string userPrincipalName, CancellationToken cancellationToken) =>
        Task.Run(() => Find(userPrincipalName, IdentityType.UserPrincipalName), cancellationToken);

    private DirectoryUserRecord? Find(string value, IdentityType identityType)
    {
        using PrincipalContext context = string.IsNullOrWhiteSpace(_options.Container)
            ? new PrincipalContext(ContextType.Domain, _options.DomainName)
            : new PrincipalContext(ContextType.Domain, _options.DomainName, _options.Container);
        var user = UserPrincipal.FindByIdentity(context, identityType, value);
        if (user is null || !IsExactMatch(user, value, identityType))
        {
            return null;
        }
        using (user)
        {
            return MapUser(user);
        }
    }

    private static bool IsExactMatch(UserPrincipal user, string value, IdentityType type) => type == IdentityType.SamAccountName
        ? string.Equals(user.SamAccountName, value, StringComparison.OrdinalIgnoreCase)
        : string.Equals(user.UserPrincipalName, value, StringComparison.OrdinalIgnoreCase);

    private static DirectoryUserRecord MapUser(UserPrincipal user)
    {
        var entry = user.GetUnderlyingObject() as DirectoryEntry;
        return new DirectoryUserRecord(user.DisplayName, user.SamAccountName ?? string.Empty, user.UserPrincipalName, user.EmailAddress,
            GetProperty(entry, "department"), GetProperty(entry, "title"), ManagerName(entry), user.Enabled, Locked(user), "ActiveDirectory");
    }
    private static string? GetProperty(DirectoryEntry? entry, string name) => entry?.Properties[name]?.Value?.ToString();
    private static bool? Locked(UserPrincipal user) { try { return user.IsAccountLockedOut(); } catch (PrincipalOperationException) { return null; } }
    private static string? ManagerName(DirectoryEntry? entry)
    {
        string? dn = GetProperty(entry, "manager");
        if (string.IsNullOrWhiteSpace(dn))
        {
            return null;
        }
        try { using DirectoryEntry manager = new($"LDAP://{dn}"); return GetProperty(manager, "displayName"); } catch (DirectoryServicesCOMException) { return null; }
    }
}
#pragma warning restore CA1416
