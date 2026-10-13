using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Deterministic graph-shaped enrichment provider for local and automated use.</summary>
public sealed class MockDirectoryEnrichmentProvider : IDirectoryEnrichmentProvider
{
    private static readonly IReadOnlyDictionary<string, DirectoryGroupRecord> _groups = CreateGroups();
    private static readonly IReadOnlyDictionary<string, string[]> _directGroups =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["pam12356"] = ["primary-domain-users", "ops-read", "dist-universal"],
            ["normal.user"] = ["primary-domain-users", "ops-read"],
            ["pam.zero"] = ["primary-domain-users", "ops-read"],
            ["service.zero"] = ["primary-domain-users", "ops-read"],
            ["syn.gmsa$"] = ["primary-domain-users", "ops-read"],
            ["syn.msa$"] = ["primary-domain-users", "ops-read"],
            ["zero.groups"] = []
        };
    private static readonly IReadOnlyDictionary<string, string[]> _parents =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ops-read"] = ["nested-ops"],
            ["dist-universal"] = ["nested-ops"],
            ["nested-ops"] = ["platform-privileged"],
            ["platform-privileged"] = ["ops-read"]
        };
    private readonly IdentityLookupOptions _identityOptions;

    /// <summary>Initializes deterministic effective UPN behavior.</summary>
    public MockDirectoryEnrichmentProvider(IOptions<IdentityLookupOptions> identityOptions) =>
        _identityOptions = identityOptions.Value;

    /// <inheritdoc />
    public string ProviderName => "Mock";

    /// <inheritdoc />
    public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(
        string normalizedAccount,
        int maxSpns,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string account = Account(normalizedAccount);
        if (!_directGroups.ContainsKey(account))
        {
            return Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(null);
        }

        string[] allSpns = [];
        bool passwordNeverExpires = account == "service.zero";
        DirectoryPrincipalEnrichmentRecord record = new(
            "S-1-5-21-2001",
            "Sample Operations Account",
            account,
            account.EndsWith('$') ? null : $"{account}@example.invalid",
            true,
            false,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            passwordNeverExpires,
            null,
            false,
            new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
            "CN=Sample Owner,OU=Users,DC=example,DC=invalid",
            allSpns.Take(maxSpns).ToArray(),
            allSpns.Length,
            allSpns.Length > maxSpns,
            account switch
            {
                "syn.gmsa$" => "GroupManagedServiceAccount",
                "syn.msa$" => "ManagedServiceAccount",
                _ => "User"
            });
        return Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(record);
    }

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
        string normalizedAccount,
        int maxResults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string account = Account(normalizedAccount);
        if (!_directGroups.TryGetValue(account, out string[]? groupKeys))
        {
            return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);
        }

        return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(Page(groupKeys, maxResults));
    }

    /// <inheritdoc />
    public Task<DirectoryGroupRecord?> FindGroupAsync(
        string normalizedGroup,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryGroupRecord? group = _groups.Values.FirstOrDefault(item =>
            string.Equals(item.StableIdentifier, normalizedGroup, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.SamAccountName, normalizedGroup, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(group);
    }

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
        DirectoryGroupRecord group,
        int maxResults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (group.SamAccountName is null || !_parents.TryGetValue(group.SamAccountName, out string[]? parentKeys))
        {
            return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(
                new DirectoryProviderPage<DirectoryGroupRecord>([], false));
        }

        return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(Page(parentKeys, maxResults));
    }

    private string Account(string account) =>
        _identityOptions.EnableUpnLookup
            && (account.Equals("pam12356@example.invalid", StringComparison.OrdinalIgnoreCase)
                || account.Equals("pam12356@contoso.local", StringComparison.OrdinalIgnoreCase))
                ? "pam12356"
                : account;

    private static DirectoryProviderPage<DirectoryGroupRecord> Page(IEnumerable<string> keys, int maxResults)
    {
        string[] ordered = keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        return new DirectoryProviderPage<DirectoryGroupRecord>(
            ordered.Take(maxResults).Select(key => _groups[key]).ToArray(),
            ordered.Length > maxResults);
    }

    private static IReadOnlyDictionary<string, DirectoryGroupRecord> CreateGroups() =>
        new Dictionary<string, DirectoryGroupRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["ops-read"] = Group(1001, "Operations Readers", "ops-read"),
            ["dist-universal"] = Group(1002, "Operations Announcements", "dist-universal", "Distribution", "Universal"),
            ["nested-ops"] = Group(1003, "Nested Operations", "nested-ops"),
            ["platform-privileged"] = Group(1004, "Sample Privileged Tier", "platform-privileged"),
            ["unrelated-group"] = Group(1005, "Unrelated Group", "unrelated-group"),
            ["primary-domain-users"] = Group(513, "Primary Domain Users", "primary-domain-users", membershipKind: "Primary")
        };

    private static DirectoryGroupRecord Group(
        int sid,
        string name,
        string account,
        string category = "Security",
        string scope = "Global",
        string? membershipKind = null) => new(
            $"S-1-5-21-{sid}",
            name,
            account,
            $"CN={name},OU=Groups,DC=example,DC=invalid",
            null,
            category,
            scope,
            MembershipKind: membershipKind);
}
