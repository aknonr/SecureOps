#pragma warning disable CA1416

using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Read-only Active Directory identity provider.
/// </summary>
public sealed class ActiveDirectoryIdentityDirectoryProvider : IIdentityDirectoryProvider
{
    private readonly IdentityLookupOptions _options;

    /// <summary>
    /// Initializes a new Active Directory provider.
    /// </summary>
    /// <param name="options">Identity lookup options.</param>
    public ActiveDirectoryIdentityDirectoryProvider(IOptions<IdentityLookupOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<DirectoryUserRecord?> FindUserAsync(string normalizedAccount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IdentityProviderInputGuard.EnsureSafeExactAccount(normalizedAccount, _options);

        var timeout = TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds <= 0 ? 3 : _options.ProviderTimeoutSeconds);

        return await Task
            .Run(() => FindUserCore(normalizedAccount), cancellationToken)
            .WaitAsync(timeout, cancellationToken);
    }

    private DirectoryUserRecord? FindUserCore(string normalizedAccount)
    {
        using PrincipalContext context = string.IsNullOrWhiteSpace(_options.Container)
            ? new PrincipalContext(ContextType.Domain, _options.DomainName)
            : new PrincipalContext(ContextType.Domain, _options.DomainName, _options.Container);

        var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, normalizedAccount);
        if (user is not null && IsExactSamAccountMatch(user, normalizedAccount))
        {
            return MapUser(user);
        }

        if (_options.EnableUpnLookup && normalizedAccount.Contains('@', StringComparison.Ordinal))
        {
            user = UserPrincipal.FindByIdentity(context, IdentityType.UserPrincipalName, normalizedAccount);
            if (user is not null && IsExactUpnMatch(user, normalizedAccount))
            {
                return MapUser(user);
            }
        }

        return null;
    }

    private static bool IsExactSamAccountMatch(UserPrincipal user, string normalizedAccount)
    {
        return string.Equals(user.SamAccountName, normalizedAccount, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactUpnMatch(UserPrincipal user, string normalizedAccount)
    {
        return string.Equals(user.UserPrincipalName, normalizedAccount, StringComparison.OrdinalIgnoreCase);
    }

    private static DirectoryUserRecord MapUser(UserPrincipal user)
    {
        var directoryEntry = user.GetUnderlyingObject() as DirectoryEntry;

        return new DirectoryUserRecord(
            user.DisplayName,
            user.SamAccountName ?? string.Empty,
            user.UserPrincipalName,
            user.EmailAddress,
            GetProperty(directoryEntry, "department"),
            GetProperty(directoryEntry, "title"),
            TryGetManagerDisplayName(directoryEntry),
            user.Enabled,
            SafeLockedState(user),
            "ActiveDirectory");
    }

    private static bool? SafeLockedState(UserPrincipal user)
    {
        try
        {
            return user.IsAccountLockedOut();
        }
        catch (PrincipalOperationException)
        {
            return null;
        }
    }

    private static string? GetProperty(DirectoryEntry? entry, string name)
    {
        object? value = entry?.Properties[name]?.Value;
        return value?.ToString();
    }

    private static string? TryGetManagerDisplayName(DirectoryEntry? entry)
    {
        string? managerDn = GetProperty(entry, "manager");
        if (string.IsNullOrWhiteSpace(managerDn))
        {
            return null;
        }

        try
        {
            using DirectoryEntry manager = new($"LDAP://{managerDn}");
            return GetProperty(manager, "displayName");
        }
        catch (DirectoryServicesCOMException)
        {
            return null;
        }
    }
}

#pragma warning restore CA1416
