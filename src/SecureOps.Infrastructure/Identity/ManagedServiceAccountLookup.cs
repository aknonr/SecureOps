#pragma warning disable CA1416
using System.DirectoryServices;
using System.Globalization;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

// Only this explicit projection crosses the LDAP boundary; never hydrate a DirectoryEntry result.
internal static class ManagedServiceAccountLookup
{
    internal static IReadOnlyList<string> Attributes { get; } = Array.AsReadOnly(new[]
    {
        "objectClass", "sAMAccountName", "displayName", "userPrincipalName", "mail", "department", "title",
        "objectSid", "userAccountControl", "msDS-User-Account-Control-Computed", "pwdLastSet",
        "accountExpires", "lastLogonTimestamp", "managedBy", "servicePrincipalName", "memberOf", "primaryGroupID"
    });

    internal static string Filter(string account, IdentityLookupOptions options)
    {
        IdentityProviderInputGuard.EnsureSafeExactAccount(account, options);
        if (!account.EndsWith('$'))
        {
            throw new IdentityProviderInputRejectedException("AccountPatternRejected", "Managed account requires an exact sAMAccountName suffix.");
        }

        return $"(&(sAMAccountName={account})(|(objectClass=msDS-GroupManagedServiceAccount)(objectClass=msDS-ManagedServiceAccount)))";
    }

    internal static ManagedServiceAccountRecord? Find(string account, IdentityLookupOptions options)
    {
        string filter = Filter(account, options);
        if (string.IsNullOrWhiteSpace(options.DomainName))
        {
            throw new InvalidOperationException("IdentityLookup:DomainName is required for managed account lookup.");
        }

        string path = "LDAP://" + options.DomainName;
        if (!string.IsNullOrWhiteSpace(options.Container))
        {
            path += "/" + options.Container;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.ProviderTimeoutSeconds));
        using DirectoryEntry root = new(path);
        using DirectorySearcher searcher = new(root, filter, Attributes.ToArray(), SearchScope.Subtree)
        {
            SizeLimit = 2,
            ClientTimeout = timeout,
            ServerTimeLimit = timeout,
            ReferralChasing = ReferralChasingOption.None,
            CacheResults = false
        };
        using SearchResultCollection results = searcher.FindAll();
        if (results.Count > 1)
        {
            throw new InvalidOperationException("Ambiguous managed account identity.");
        }

        if (results.Count == 0)
        {
            return null;
        }

        SearchResult result = results[0];
        var values = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string attribute in Attributes)
        {
            values[attribute] = result.Properties[attribute].Cast<object>().ToArray();
        }

        return Match(account, values);
    }

    internal static ManagedServiceAccountRecord? Match(string account, IReadOnlyDictionary<string, object[]> values)
    {
        var record = new ManagedServiceAccountRecord(values);
        return string.Equals(record.Text("sAMAccountName"), account, StringComparison.OrdinalIgnoreCase)
            && record.AccountTypeEvidence is not null ? record : null;
    }
}

internal sealed class ManagedServiceAccountRecord(IReadOnlyDictionary<string, object[]> values)
{
    internal object? Value(string name) => values.TryGetValue(name, out object[]? items) ? items.FirstOrDefault() : null;
    internal string? Text(string name) => Value(name) as string;
    internal string[] Strings(string name) => values.TryGetValue(name, out object[]? items) ? items.OfType<string>().ToArray() : [];
    internal long? Number(string name) => Value(name) is object value ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
    internal bool? Flag(string name, long mask) => Number(name) is long value ? (value & mask) != 0 : null;
    internal bool? Enabled => Flag("userAccountControl", 2) is bool disabled ? !disabled : null;
    internal bool? Locked => Flag("msDS-User-Account-Control-Computed", 16);
    internal DateTimeOffset? Time(string name)
    {
        long? value = Number(name);
        return value is null or <= 0 or long.MaxValue ? null : new DateTimeOffset(DateTime.FromFileTimeUtc(value.Value));
    }

    internal string? AccountTypeEvidence => Strings("objectClass").Contains("msDS-GroupManagedServiceAccount", StringComparer.OrdinalIgnoreCase)
        ? "GroupManagedServiceAccount"
        : Strings("objectClass").Contains("msDS-ManagedServiceAccount", StringComparer.OrdinalIgnoreCase) ? "ManagedServiceAccount" : null;

    internal DirectoryUserRecord Identity => new(Text("displayName"), Text("sAMAccountName")!, Text("userPrincipalName"),
        Text("mail"), Text("department"), Text("title"), null, Enabled, Locked, "ActiveDirectory", AccountTypeEvidence!);
}
#pragma warning restore CA1416
