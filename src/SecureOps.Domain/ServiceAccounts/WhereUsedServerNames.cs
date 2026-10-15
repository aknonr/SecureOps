using System.Net;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>One server name as it was written by a source and its comparison key.</summary>
/// <param name="Original">Cleaned text as the source wrote it.</param>
/// <param name="Key">Lower-case, no trailing dot, no UNC prefix; the text itself when it does not look like a host name.</param>
/// <param name="IsHostName">Letters, digits, hyphen, underscore and dots only (not an IP address).</param>
public readonly record struct ServerNameKey(string Original, string Key, bool IsHostName)
{
    /// <summary>First label of a dotted host name (the short name), or null for a short name, an IP address or free text.</summary>
    public string? ShortLabel => IsHostName && Key.Contains('.', StringComparison.Ordinal) ? Key[..Key.IndexOf('.', StringComparison.Ordinal)] : null;
}

/// <summary>
/// Safe server-name normalisation for the where-used view. It never guesses: case, whitespace, a UNC prefix and trailing dots are
/// removed; joining a short name with an FQDN is a separate, explicit step that only accepts an exact one-to-one match.
/// </summary>
public static class WhereUsedServerNames
{
    /// <summary>Longest name accepted (the column holds 256).</summary>
    public const int MaxLength = 256;

    /// <summary>Key of a server name, or null for an empty one.</summary>
    public static ServerNameKey? Normalize(string? value)
    {
        string? cleaned = ServiceAccountText.Clean(value);
        if (cleaned is null)
        {
            return null;
        }

        string text = cleaned.TrimStart((char)92).TrimEnd('.');
        if (text.Length is 0 or > MaxLength)
        {
            return null;
        }

        string key = text.ToLowerInvariant();
        bool host = key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.') && !key.StartsWith('.')
            && !key.Contains("..", StringComparison.Ordinal) && !IPAddress.TryParse(key, out _);
        return new ServerNameKey(text, key, host);
    }

    /// <summary>
    /// Maps every distinct key to the key it is shown under. A short name joins an FQDN only when exactly one distinct FQDN in
    /// the data starts with that label; two FQDNs with the same label (different domains) stay separate rows and the short name
    /// stays its own row too, because it cannot be told which one it meant.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Merge(IEnumerable<ServerNameKey> names)
    {
        ServerNameKey[] distinct = [.. names.GroupBy(n => n.Key, StringComparer.Ordinal).Select(g => g.First())];
        Dictionary<string, string> map = distinct.ToDictionary(n => n.Key, n => n.Key, StringComparer.Ordinal);
        ILookup<string, ServerNameKey> byLabel = distinct.Where(n => n.ShortLabel is not null).ToLookup(n => n.ShortLabel!, StringComparer.Ordinal);
        foreach (ServerNameKey shortName in distinct.Where(n => n.IsHostName && !n.Key.Contains('.', StringComparison.Ordinal)))
        {
            ServerNameKey[] candidates = [.. byLabel[shortName.Key]];
            if (candidates.Length == 1)
            {
                map[shortName.Key] = candidates[0].Key;
            }
        }

        return map;
    }
}

/// <summary>An SPN host the directory names for the account.</summary>
/// <param name="Host">Host part as written.</param>
/// <param name="Class">Service class (for example the first segment).</param>
public sealed record SpnHost(string Host, string Class);

/// <summary>Extracts hosts from <c>serviceClass/host[:port][/serviceName]</c> service principal names.</summary>
public static class ServicePrincipalNameHosts
{
    /// <summary>Hosts of the valid SPNs (de-duplicated by host and class) and how many SPNs could not be read.</summary>
    public static (IReadOnlyList<SpnHost> Hosts, int Unreadable) Parse(IEnumerable<string> spns)
    {
        List<SpnHost> hosts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int unreadable = 0;
        foreach (string spn in spns)
        {
            if (TryParse(spn) is not { } host)
            {
                unreadable++;
            }
            else if (seen.Add($"{host.Class}/{host.Host}"))
            {
                hosts.Add(host);
            }
        }

        return (hosts, unreadable);
    }

    /// <summary>One SPN; null when it has no service class or host. A port or instance after <c>:</c> and a service name after a second <c>/</c> are dropped.</summary>
    public static SpnHost? TryParse(string? spn)
    {
        string? cleaned = ServiceAccountText.Clean(spn);
        if (cleaned is null)
        {
            return null;
        }

        int slash = cleaned.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == cleaned.Length - 1)
        {
            return null;
        }

        string serviceClass = cleaned[..slash];
        string rest = cleaned[(slash + 1)..];
        int end = rest.IndexOf('/', StringComparison.Ordinal);
        string authority = end < 0 ? rest : rest[..end];
        int colon = authority.IndexOf(':', StringComparison.Ordinal);
        string host = colon < 0 ? authority : authority[..colon];
        return host.Length == 0 || host.StartsWith('[') ? null : new SpnHost(host, serviceClass);
    }
}
