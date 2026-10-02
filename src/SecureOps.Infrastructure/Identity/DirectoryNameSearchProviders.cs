#pragma warning disable CA1416
using System.DirectoryServices;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>Synthetic in-memory directory for the Mock identity provider: common Turkish name forms with synthetic <c>syn.*</c> accounts only.</summary>
public sealed class MockDirectoryNameSearchProvider(IEnumerable<DirectoryNameCandidate>? users = null) : IDirectoryNameSearchProvider
{
    private readonly DirectoryNameCandidate[] _users = [.. users ?? DefaultUsers()];

    /// <inheritdoc />
    public Task<DirectoryNameSearchResult> SearchAsync(DirectoryNameQuery query, int limit, CancellationToken cancellationToken)
    {
        DirectoryNameCandidate[] matches = [.. DirectoryNameMatching.Order(query, _users.Where(u => DirectoryNameMatching.Matches(query, u)))];
        return Task.FromResult(new DirectoryNameSearchResult([.. matches.Take(limit)], matches.Length > limit));
    }

    /// <summary>Synthetic Turkish names, including a same-named pair and dotted/dotless I spellings.</summary>
    public static IReadOnlyList<DirectoryNameCandidate> DefaultUsers() =>
    [
        new("İsmail Işık", "İsmail", "Işık", "syn.ismail.isik", "SYN Uygulama"),
        new("Şükrü Öztürk", "Şükrü", "Öztürk", "syn.sukru.ozturk", "SYN Veritabanı"),
        new("Ayşe Yılmaz", "Ayşe", "Yılmaz", "syn.ayse.yilmaz", "SYN Altyapı"),
        new("Ayşe Yılmaz", "Ayşe", "Yılmaz", "syn.ayse.yilmaz2", "SYN Uygulama"),
        new("Çağrı Güneş", "Çağrı", "Güneş", "syn.cagri.gunes", null)
    ];
}

/// <summary>
/// Read-only Active Directory name search with the process identity: one bounded subtree search, minimal attributes,
/// size and time limits, no referral chasing. Never binds with other credentials and never writes.
/// </summary>
public sealed class ActiveDirectoryNameSearchProvider(IOptions<IdentityLookupOptions> options) : IDirectoryNameSearchProvider
{
    private static readonly string[] _attributes = ["displayName", "givenName", "sn", "sAMAccountName", "department"];
    private readonly IdentityLookupOptions _options = options.Value;

    /// <inheritdoc />
    public Task<DirectoryNameSearchResult> SearchAsync(DirectoryNameQuery query, int limit, CancellationToken cancellationToken) =>
        Task.Run(() => Search(query, limit), cancellationToken);

    private DirectoryNameSearchResult Search(DirectoryNameQuery query, int limit)
    {
        string root = string.IsNullOrWhiteSpace(_options.Container) ? _options.DomainName ?? string.Empty : _options.Container;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("IdentityLookup:DomainName or Container is required for name search.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.ProviderTimeoutSeconds));
        using DirectoryEntry entry = new("LDAP://" + root);
        using DirectorySearcher searcher = new(entry, DirectoryNameFilter.Build(query), _attributes, SearchScope.Subtree)
        {
            SizeLimit = limit * 3 + 1,
            ClientTimeout = timeout,
            ServerTimeLimit = timeout,
            ReferralChasing = ReferralChasingOption.None,
            CacheResults = false
        };
        using SearchResultCollection results = searcher.FindAll();
        List<DirectoryNameCandidate> candidates = [];
        foreach (SearchResult result in results)
        {
            string? account = Value(result, "sAMAccountName");
            if (!string.IsNullOrWhiteSpace(account))
            {
                candidates.Add(new DirectoryNameCandidate(Value(result, "displayName"), Value(result, "givenName"), Value(result, "sn"), account,
                    Value(result, "department")));
            }
        }

        DirectoryNameCandidate[] matches = [.. DirectoryNameMatching.Order(query, candidates.Where(c => DirectoryNameMatching.Matches(query, c)))];
        return new DirectoryNameSearchResult([.. matches.Take(limit)], matches.Length > limit || results.Count > limit * 3);
    }

    private static string? Value(SearchResult result, string name) =>
        result.Properties.Contains(name) && result.Properties[name].Count > 0 ? result.Properties[name][0]?.ToString() : null;
}
#pragma warning restore CA1416
