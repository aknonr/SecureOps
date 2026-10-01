using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Deterministic read-only directory provider for development and automated tests.</summary>
public sealed class MockDirectoryGroupProvider : IDirectoryGroupProvider
{
    private static readonly IReadOnlyDictionary<string, string[]> _principalGroups =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["pam12356"] = ["primary-domain-users", "ops-read", "dist-universal"],
            ["zero.groups"] = []
        };

    private static readonly IReadOnlyDictionary<string, DirectoryGroupRecord> _groups = CreateGroups();
    private static readonly IReadOnlyDictionary<string, DirectoryMemberRecord[]> _members = CreateMembers();
    private readonly IdentityLookupOptions _identityOptions;

    /// <summary>Initializes deterministic mock behavior.</summary>
    public MockDirectoryGroupProvider(IOptions<IdentityLookupOptions> identityOptions) =>
        _identityOptions = identityOptions.Value;

    /// <inheritdoc />
    public string ProviderName => "Mock";

    /// <inheritdoc />
    public bool SupportsUpnLookup => _identityOptions.EnableUpnLookup;

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(
        string normalizedAccount,
        int offset,
        int pageSize,
        int resultLimit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string account = normalizedAccount;
        if (SupportsUpnLookup && account.Equals("pam12356@contoso.local", StringComparison.OrdinalIgnoreCase))
        {
            account = "pam12356";
        }

        if (!_principalGroups.TryGetValue(account, out string[]? groupKeys))
        {
            return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);
        }

        DirectoryGroupRecord[] records = groupKeys.Select(key => _groups[key])
            .OrderBy(group => group.SamAccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(Page(records, offset, pageSize, resultLimit));
    }

    /// <inheritdoc />
    public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryGroupRecord? group = _groups.Values.FirstOrDefault(item =>
            string.Equals(item.SamAccountName, normalizedGroup, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(group);
    }

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(
        string normalizedGroup,
        int offset,
        int pageSize,
        int resultLimit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryGroupRecord? group = _groups.Values.FirstOrDefault(item =>
            string.Equals(item.SamAccountName, normalizedGroup, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase));
        if (group?.SamAccountName is null || !_members.TryGetValue(group.SamAccountName, out DirectoryMemberRecord[]? members))
        {
            return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null);
        }

        DirectoryMemberRecord[] ordered = members
            .OrderBy(member => member.SamAccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(Page(ordered, offset, pageSize, resultLimit));
    }

    private static DirectoryProviderPage<T> Page<T>(IEnumerable<T> source, int offset, int pageSize, int resultLimit)
    {
        T[] ordered = source.Take(resultLimit + 1).ToArray();
        if (ordered.Length > resultLimit)
        {
            throw new DirectoryQueryLimitExceededException();
        }

        return new DirectoryProviderPage<T>(ordered.Skip(offset).Take(pageSize).ToArray(), offset + pageSize < ordered.Length);
    }

    private static IReadOnlyDictionary<string, DirectoryGroupRecord> CreateGroups() =>
        new Dictionary<string, DirectoryGroupRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ops-read"] = new("S-1-5-21-1001", "Operations Readers", "ops-read", "CN=Operations Readers,OU=Groups,DC=contoso,DC=local", "Read-only operations access", "Security", "Global", "CN=Example Manager,OU=Users,DC=contoso,DC=local", 3),
            ["dist-universal"] = new("S-1-5-21-1002", "Operations Announcements", "dist-universal", "CN=Operations Announcements,OU=Groups,DC=contoso,DC=local", "Operations distribution", "Distribution", "Universal", null, 0),
            ["domain-local-empty"] = new("S-1-5-21-1003", "Domain Local Empty", "domain-local-empty", "CN=Domain Local Empty,OU=Groups,DC=contoso,DC=local", null, "Security", "DomainLocal", null, 0),
            ["primary-domain-users"] = new("S-1-5-21-513", "Primary Domain Users", "primary-domain-users", "CN=Primary Domain Users,OU=Groups,DC=contoso,DC=local", null, "Security", "Global", MembershipKind: "Primary")
        };

    private static IReadOnlyDictionary<string, DirectoryMemberRecord[]> CreateMembers() =>
        new Dictionary<string, DirectoryMemberRecord[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ops-read"] =
            [
                new("S-1-5-21-2001", "Example Admin", "pam12356", "CN=Example Admin,OU=Users,DC=contoso,DC=local", "User"),
                new("S-1-5-21-2002", "Nested Operations", "nested-ops", "CN=Nested Operations,OU=Groups,DC=contoso,DC=local", "Group"),
                new("S-1-5-21-2003", "OPS-WIN-01", "OPS-WIN-01$", "CN=OPS-WIN-01,OU=Computers,DC=contoso,DC=local", "Computer")
            ],
            ["dist-universal"] = [],
            ["domain-local-empty"] = [],
            ["primary-domain-users"] = []
        };
}
