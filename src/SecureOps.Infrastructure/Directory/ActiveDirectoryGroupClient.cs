#pragma warning disable CA1416
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Production read-only group transport using the API process identity.</summary>
public sealed partial class ActiveDirectoryGroupClient : IActiveDirectoryGroupClient
{
    private readonly IdentityLookupOptions _options;

    /// <summary>Initializes the Active Directory transport.</summary>
    public ActiveDirectoryGroupClient(IOptions<IdentityLookupOptions> options) => _options = options.Value;

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsBySamAccountNameAsync(
        string account, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
        RunAsync(() => GetPrincipalGroups(account, IdentityType.SamAccountName, offset, pageSize, resultLimit, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsByUpnAsync(
        string userPrincipalName, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
        RunAsync(() => GetPrincipalGroups(userPrincipalName, IdentityType.UserPrincipalName, offset, pageSize, resultLimit, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryGroupRecord?> FindGroupAsync(string group, CancellationToken cancellationToken) =>
        RunAsync(() => FindGroupRecord(group), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(
        string group, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
        RunAsync(() => GetMembers(group, offset, pageSize, resultLimit, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersForAnalysisAsync(
        string group, int maxResults, CancellationToken cancellationToken) =>
        RunAsync(() => GetMembersForAnalysis(group, maxResults, cancellationToken), cancellationToken);

    private DirectoryProviderPage<DirectoryGroupRecord>? GetPrincipalGroups(
        string value,
        IdentityType identityType,
        int offset,
        int pageSize,
        int resultLimit,
        CancellationToken cancellationToken)
    {
        using PrincipalContext context = CreateContext();
        using var user = UserPrincipal.FindByIdentity(context, identityType, value);
        if (user is null || !ExactUser(user, value, identityType))
        {
            return null;
        }

        PrincipalMembershipSet memberships = ReadPrincipalMemberships(
            context, user, resultLimit, cancellationToken);
        return Page(memberships.Groups, offset, pageSize, memberships.IsPartial);
    }

    private DirectoryGroupRecord? FindGroupRecord(string value)
    {
        using PrincipalContext context = CreateContext();
        using GroupPrincipal? group = FindExactGroup(context, value);
        return group is null ? null : MapGroup(group);
    }

    private DirectoryProviderPage<DirectoryMemberRecord>? GetMembers(
        string value,
        int offset,
        int pageSize,
        int resultLimit,
        CancellationToken cancellationToken)
    {
        using PrincipalContext context = CreateContext();
        using GroupPrincipal? group = FindExactGroup(context, value);
        if (group is null)
        {
            return null;
        }

        List<DirectoryMemberRecord> records = [];
        foreach (Principal principal in group.Members)
        {
            using (principal)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddBounded(records, MapMember(principal), resultLimit);
            }
        }

        return Page(records, offset, pageSize);
    }

    private DirectoryProviderPage<DirectoryMemberRecord>? GetMembersForAnalysis(
        string value,
        int maxResults,
        CancellationToken cancellationToken)
    {
        using PrincipalContext context = CreateContext();
        using GroupPrincipal? group = FindExactGroup(context, value);
        if (group is null)
        {
            return null;
        }

        List<DirectoryMemberRecord> records = [];
        bool hasMore = false;
        foreach (Principal principal in group.Members)
        {
            using (principal)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (records.Count >= maxResults)
                {
                    hasMore = true;
                    break;
                }

                records.Add(MapMember(principal));
            }
        }

        return Page(records, 0, maxResults) with { HasMore = hasMore };
    }

    private PrincipalContext CreateContext() => string.IsNullOrWhiteSpace(_options.Container)
        ? new PrincipalContext(ContextType.Domain, _options.DomainName)
        : new PrincipalContext(ContextType.Domain, _options.DomainName, _options.Container);

    private static GroupPrincipal? FindExactGroup(PrincipalContext context, string value)
    {
        var group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, value);
        if (group is not null && ExactGroup(group, value))
        {
            return group;
        }

        group?.Dispose();
        group = GroupPrincipal.FindByIdentity(context, IdentityType.Name, value);
        if (group is not null && ExactGroup(group, value))
        {
            return group;
        }

        group?.Dispose();
        return null;
    }

    private static bool ExactUser(UserPrincipal user, string value, IdentityType type) =>
        type == IdentityType.SamAccountName
            ? string.Equals(user.SamAccountName, value, StringComparison.OrdinalIgnoreCase)
            : string.Equals(user.UserPrincipalName, value, StringComparison.OrdinalIgnoreCase);

    private static bool ExactGroup(GroupPrincipal group, string value) =>
        string.Equals(group.SamAccountName, value, StringComparison.OrdinalIgnoreCase)
        || string.Equals(group.Name, value, StringComparison.OrdinalIgnoreCase);

    private static DirectoryGroupRecord MapGroup(GroupPrincipal group, string? membershipKind = null)
    {
        using var entry = group.GetUnderlyingObject() as DirectoryEntry;
        string? managedBy = Property(entry, "managedBy");
        return new DirectoryGroupRecord(
            StableIdentifier: group.Sid?.Value,
            Name: group.Name,
            SamAccountName: group.SamAccountName,
            DistinguishedName: group.DistinguishedName,
            Description: group.Description,
            Category: group.IsSecurityGroup == true ? "Security" : group.IsSecurityGroup == false ? "Distribution" : "Unknown",
            Scope: Scope(group.GroupScope),
            ManagedBy: managedBy,
            ManagedByDisplayName: ManagedByDisplayName(managedBy),
            CreatedAtUtc: DateTimeProperty(entry, "whenCreated"),
            ChangedAtUtc: DateTimeProperty(entry, "whenChanged"),
            MembershipKind: membershipKind);
    }

    private static DirectoryMemberRecord MapMember(Principal principal) => new(
        principal.Sid?.Value,
        principal.Name,
        principal.SamAccountName,
        principal.DistinguishedName,
        principal switch
        {
            UserPrincipal => "User",
            GroupPrincipal => "Group",
            ComputerPrincipal => "Computer",
            _ => "Other"
        });

    private static string Scope(GroupScope? scope) => scope switch
    {
        GroupScope.Global => "Global",
        GroupScope.Universal => "Universal",
        GroupScope.Local => "DomainLocal",
        _ => "Unknown"
    };

    private static string? Property(DirectoryEntry? entry, string name) =>
        entry?.Properties[name]?.Value?.ToString();

    private static void AddBounded<T>(List<T> records, T record, int resultLimit)
    {
        if (records.Count >= resultLimit)
        {
            throw new DirectoryQueryLimitExceededException();
        }

        records.Add(record);
    }

    private static DirectoryProviderPage<T> Page<T>(List<T> records, int offset, int pageSize, bool isPartial = false)
    {
        IEnumerable<T> ordered = typeof(T) == typeof(DirectoryGroupRecord)
            ? records.Cast<DirectoryGroupRecord>()
                .OrderBy(item => item.SamAccountName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .Cast<T>()
            : records.Cast<DirectoryMemberRecord>()
                .OrderBy(item => item.SamAccountName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .Cast<T>();
        T[] all = ordered.ToArray();
        return new DirectoryProviderPage<T>(all.Skip(offset).Take(pageSize).ToArray(), offset + pageSize < all.Length, isPartial);
    }

    private static string? ManagedByDisplayName(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return null;
        }

        try
        {
            using var entry = new DirectoryEntry($"LDAP://{distinguishedName}");
            return Property(entry, "displayName") ?? Property(entry, "name");
        }
        catch (Exception exception) when (exception is DirectoryServicesCOMException or COMException)
        {
            return null;
        }
    }

    private static DateTimeOffset? DateTimeProperty(DirectoryEntry? entry, string name)
    {
        object? value = entry?.Properties[name]?.Value;
        return value switch
        {
            DateTime dateTime => new DateTimeOffset(dateTime.ToUniversalTime()),
            _ => null
        };
    }

    private static async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(operation, cancellationToken);
        }
        catch (Exception exception) when (exception is PrincipalException or DirectoryServicesCOMException or COMException)
        {
            throw new DirectoryProviderUnavailableException(exception);
        }
    }
}
#pragma warning restore CA1416
