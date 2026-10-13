#pragma warning disable CA1416
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Globalization;
using System.Security.Principal;
using SecureOps.Infrastructure.Identity;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Phase 2 read-only enrichment operations on the existing AccountManagement client.</summary>
public sealed partial class ActiveDirectoryGroupClient : IActiveDirectoryEnrichmentClient
{
    /// <inheritdoc />
    public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalBySamAccountNameAsync(
        string account,
        int maxSpns,
        CancellationToken cancellationToken) =>
        RunAsync(() => FindPrincipalRecord(account, IdentityType.SamAccountName, maxSpns), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalByUpnAsync(
        string userPrincipalName,
        int maxSpns,
        CancellationToken cancellationToken) =>
        RunAsync(() => FindPrincipalRecord(userPrincipalName, IdentityType.UserPrincipalName, maxSpns), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalMembershipGroupsBySamAccountNameAsync(
        string account,
        int maxResults,
        CancellationToken cancellationToken) =>
        RunAsync(
            () => GetPrincipalMembershipGroups(account, IdentityType.SamAccountName, maxResults, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalMembershipGroupsByUpnAsync(
        string userPrincipalName,
        int maxResults,
        CancellationToken cancellationToken) =>
        RunAsync(
            () => GetPrincipalMembershipGroups(userPrincipalName, IdentityType.UserPrincipalName, maxResults, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryGroupRecord?> FindEnrichmentGroupAsync(
        string group,
        CancellationToken cancellationToken) =>
        RunAsync(() => FindEnrichmentGroupRecord(group), cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
        DirectoryGroupRecord group,
        int maxResults,
        CancellationToken cancellationToken) =>
        RunAsync(() => GetParentGroups(group, maxResults, cancellationToken), cancellationToken);

    private DirectoryPrincipalEnrichmentRecord? FindPrincipalRecord(
        string value,
        IdentityType identityType,
        int maxSpns)
    {
        if (value.EndsWith('$'))
        {
            ManagedServiceAccountRecord? account = identityType == IdentityType.SamAccountName
                ? ManagedServiceAccountLookup.Find(value, _options) : null;
            if (account is null)
            {
                return null;
            }

            string[] spns = account.Strings("servicePrincipalName").Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(spn => spn, StringComparer.OrdinalIgnoreCase).ToArray();
            return new DirectoryPrincipalEnrichmentRecord(
                account.Value("objectSid") is byte[] sid ? new SecurityIdentifier(sid, 0).Value : null,
                account.Text("displayName"), account.Text("sAMAccountName"), account.Text("userPrincipalName"),
                account.Enabled, account.Locked, account.Time("pwdLastSet"), account.Flag("userAccountControl", 65536),
                account.Time("accountExpires"), account.Number("pwdLastSet") is long lastSet ? lastSet == 0 : null,
                account.Time("lastLogonTimestamp"), account.Text("managedBy"), spns.Take(maxSpns).ToArray(),
                spns.Length, spns.Length > maxSpns, account.AccountTypeEvidence!);
        }

        using PrincipalContext context = CreateContext();
        using var user = UserPrincipal.FindByIdentity(context, identityType, value);
        if (user is null || !ExactUser(user, value, identityType))
        {
            return null;
        }

        using var entry = user.GetUnderlyingObject() as DirectoryEntry;
        string[] allSpns = Values(entry, "servicePrincipalName")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(spn => spn, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        long? passwordLastSet = FileTimeValue(entry, "pwdLastSet");
        return new DirectoryPrincipalEnrichmentRecord(
            user.Sid?.Value,
            user.DisplayName,
            user.SamAccountName,
            user.UserPrincipalName,
            user.Enabled,
            user.IsAccountLockedOut(),
            ToUtc(user.LastPasswordSet),
            user.PasswordNeverExpires,
            ToUtc(user.AccountExpirationDate),
            passwordLastSet is null ? null : passwordLastSet == 0,
            FileTimeUtc(entry, "lastLogonTimestamp"),
            Property(entry, "managedBy"),
            allSpns.Take(maxSpns).ToArray(),
            allSpns.Length,
            allSpns.Length > maxSpns,
            AccountTypeEvidence(entry));
    }

    private DirectoryProviderPage<DirectoryGroupRecord>? GetPrincipalMembershipGroups(
        string value,
        IdentityType identityType,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (value.EndsWith('$'))
        {
            ManagedServiceAccountRecord? account = identityType == IdentityType.SamAccountName
                ? ManagedServiceAccountLookup.Find(value, _options) : null;
            if (account is null)
            {
                return null;
            }

            using PrincipalContext managedContext = CreateContext();
            PrincipalMembershipSet managedMemberships = ReadManagedMemberships(managedContext, account, maxResults, cancellationToken);
            return new DirectoryProviderPage<DirectoryGroupRecord>(managedMemberships.Groups, false, managedMemberships.IsPartial);
        }

        using PrincipalContext context = CreateContext();
        using var user = UserPrincipal.FindByIdentity(context, identityType, value);
        if (user is null || !ExactUser(user, value, identityType))
        {
            return null;
        }

        PrincipalMembershipSet memberships = ReadPrincipalMemberships(
            context, user, maxResults, cancellationToken);
        return new DirectoryProviderPage<DirectoryGroupRecord>(memberships.Groups, false, memberships.IsPartial);
    }

    private DirectoryGroupRecord? FindEnrichmentGroupRecord(string value)
    {
        using PrincipalContext context = CreateContext();
        using GroupPrincipal? group = FindExactEnrichmentGroup(context, value);
        return group is null ? null : MapGroup(group);
    }

    private DirectoryProviderPage<DirectoryGroupRecord>? GetParentGroups(
        DirectoryGroupRecord record,
        int maxResults,
        CancellationToken cancellationToken)
    {
        using PrincipalContext context = CreateContext();
        using GroupPrincipal? group = FindExactEnrichmentGroup(context, record);
        return group is null ? null : BoundedParentGroups(group, maxResults, cancellationToken);
    }

    private static DirectoryProviderPage<DirectoryGroupRecord> BoundedParentGroups(
        Principal principal,
        int maxResults,
        CancellationToken cancellationToken)
    {
        PrincipalContext context = principal.Context;
        using var entry = principal.GetUnderlyingObject() as DirectoryEntry;
        List<DirectoryGroupRecord> records = [];
        bool hasMore = false;
        bool partial = false;
        int resolutionFailures = 0;
        foreach (string distinguishedName in Values(entry, "memberOf"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (records.Count >= maxResults)
            {
                hasMore = true;
                break;
            }

            using GroupPrincipal? group = FindGroupByDistinguishedName(context, distinguishedName);
            if (group is null)
            {
                partial = true;
                resolutionFailures++;
                continue;
            }

            records.Add(MapGroup(group, "Direct"));
        }

        if (resolutionFailures > 0 && records.Count == 0)
        {
            throw new DirectoryProviderUnavailableException();
        }

        return new DirectoryProviderPage<DirectoryGroupRecord>(
            records.OrderBy(DirectoryMembershipGraph.GroupKey, StringComparer.Ordinal).ToArray(),
            hasMore,
            partial);
    }

    private static PrincipalMembershipSet ReadPrincipalMemberships(
        PrincipalContext context,
        UserPrincipal user,
        int maxResults,
        CancellationToken cancellationToken)
    {
        using var entry = user.GetUnderlyingObject() as DirectoryEntry;
        return ReadMemberships(context, FindPrimaryGroup(context, entry), entry?.Properties["primaryGroupID"]?.Value is not null,
            Values(entry, "memberOf"), maxResults, cancellationToken);
    }

    private static PrincipalMembershipSet ReadManagedMemberships(
        PrincipalContext context, ManagedServiceAccountRecord account, int maxResults, CancellationToken cancellationToken) =>
        ReadMemberships(context, FindPrimaryGroup(context, account.Value("objectSid") as byte[], account.Value("primaryGroupID")),
            account.Value("primaryGroupID") is not null, account.Strings("memberOf"), maxResults, cancellationToken);

    private static PrincipalMembershipSet ReadMemberships(
        PrincipalContext context, GroupPrincipal? primaryGroup, bool hasPrimaryGroup,
        IEnumerable<string> memberOf, int maxResults, CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, DirectoryGroupRecord>(StringComparer.OrdinalIgnoreCase);
        bool partial = false;
        int resolutionFailures = 0;

        if (primaryGroup is not null)
        {
            using (primaryGroup)
            {
                DirectoryGroupRecord record = MapGroup(primaryGroup, "Primary");
                string? key = DirectoryMembershipGraph.GroupKey(record);
                if (key is not null)
                {
                    records[key] = record;
                }
            }
        }
        else if (hasPrimaryGroup)
        {
            partial = true;
            resolutionFailures++;
        }

        foreach (string distinguishedName in memberOf)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (records.Count >= maxResults)
            {
                partial = true;
                break;
            }

            using GroupPrincipal? group = FindGroupByDistinguishedName(context, distinguishedName);
            if (group is null)
            {
                partial = true;
                resolutionFailures++;
                continue;
            }

            DirectoryGroupRecord record = MapGroup(group, "Direct");
            string? key = DirectoryMembershipGraph.GroupKey(record);
            if (key is not null && !records.ContainsKey(key))
            {
                records[key] = record;
            }
        }

        if (resolutionFailures > 0 && records.Count == 0)
        {
            throw new DirectoryProviderUnavailableException();
        }

        return new PrincipalMembershipSet(
            records.Values.OrderBy(DirectoryMembershipGraph.GroupKey, StringComparer.Ordinal).ToList(),
            partial);
    }

    private static GroupPrincipal? FindGroupByDistinguishedName(PrincipalContext context, string distinguishedName)
    {
        var group = GroupPrincipal.FindByIdentity(
            context, IdentityType.DistinguishedName, distinguishedName);
        if (group is not null
            && string.Equals(group.DistinguishedName, distinguishedName, StringComparison.OrdinalIgnoreCase))
        {
            return group;
        }

        group?.Dispose();
        return null;
    }

    private static GroupPrincipal? FindPrimaryGroup(PrincipalContext context, DirectoryEntry? entry) =>
        FindPrimaryGroup(context, entry?.Properties["objectSid"]?.Value as byte[], entry?.Properties["primaryGroupID"]?.Value);

    private static GroupPrincipal? FindPrimaryGroup(PrincipalContext context, byte[]? objectSid, object? primaryGroupId)
    {
        if (objectSid is null || primaryGroupId is null)
        {
            return null;
        }

        try
        {
            var principalSid = new SecurityIdentifier(objectSid, 0);
            SecurityIdentifier? domainSid = principalSid.AccountDomainSid;
            int primaryGroupRid = Convert.ToInt32(
                primaryGroupId, CultureInfo.InvariantCulture);
            return domainSid is null
                ? null
                : GroupPrincipal.FindByIdentity(
                    context, IdentityType.Sid, $"{domainSid.Value}-{primaryGroupRid}");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static GroupPrincipal? FindExactEnrichmentGroup(PrincipalContext context, string value)
    {
        if (value.StartsWith("s-1-", StringComparison.OrdinalIgnoreCase))
        {
            var sidGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, value);
            if (sidGroup is not null && string.Equals(sidGroup.Sid?.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return sidGroup;
            }

            sidGroup?.Dispose();
        }

        return FindExactGroup(context, value);
    }

    private static GroupPrincipal? FindExactEnrichmentGroup(
        PrincipalContext context,
        DirectoryGroupRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.StableIdentifier))
        {
            var sidGroup = GroupPrincipal.FindByIdentity(
                context,
                IdentityType.Sid,
                record.StableIdentifier);
            if (sidGroup is not null
                && string.Equals(sidGroup.Sid?.Value, record.StableIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return sidGroup;
            }

            sidGroup?.Dispose();
        }

        string? exact = !string.IsNullOrWhiteSpace(record.SamAccountName)
            ? record.SamAccountName
            : record.Name;
        return exact is null ? null : FindExactGroup(context, exact);
    }

    private static IEnumerable<string> Values(DirectoryEntry? entry, string propertyName)
    {
        if (entry?.Properties[propertyName] is not PropertyValueCollection values)
        {
            yield break;
        }

        foreach (object value in values)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
    }

    private static long? FileTimeValue(DirectoryEntry? entry, string propertyName)
    {
        object? value = entry?.Properties[propertyName]?.Value;
        try
        {
            return value is null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static DateTimeOffset? FileTimeUtc(DirectoryEntry? entry, string propertyName)
    {
        long? value = FileTimeValue(entry, propertyName);
        if (value is null or <= 0)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(value.Value));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ToUtc(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(value.Value.ToUniversalTime());

    private static string AccountTypeEvidence(DirectoryEntry? entry)
    {
        string[] objectClasses = Values(entry, "objectClass").ToArray();
        if (objectClasses.Contains("msDS-GroupManagedServiceAccount", StringComparer.OrdinalIgnoreCase))
        {
            return "GroupManagedServiceAccount";
        }

        return objectClasses.Contains("msDS-ManagedServiceAccount", StringComparer.OrdinalIgnoreCase)
            ? "ManagedServiceAccount"
            : "User";
    }

    private sealed record PrincipalMembershipSet(List<DirectoryGroupRecord> Groups, bool IsPartial);
}
#pragma warning restore CA1416
