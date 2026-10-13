namespace SecureOps.Domain.ServiceAccounts;

/// <summary>
/// The one gMSA name length rule, shared by the account-registration hint, the requested gMSA name hint and the server
/// validation. Active Directory accepts at most <see cref="Limit"/> characters for a gMSA name, counted without a
/// <c>DOMAIN\</c> prefix, a UPN suffix (<c>@domain</c>) or the trailing <c>$</c>.
/// </summary>
public static class ServiceAccountGmsaName
{
    /// <summary>Longest gMSA name Active Directory accepts, without the trailing <c>$</c>.</summary>
    public const int Limit = 15;

    /// <summary>Longest stored requested-name text (prefix and suffix included); bounded by the column.</summary>
    public const int MaxStoredLength = 256;

    /// <summary>The counted part of a name: trimmed, without a domain prefix, UPN suffix or trailing <c>$</c>.</summary>
    public static string Bare(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        string bare = trimmed[(trimmed.LastIndexOf('\\') + 1)..];
        return (bare.IndexOf('@', StringComparison.Ordinal) is var at and >= 0 ? bare[..at] : bare).TrimEnd('$');
    }

    /// <summary>Counted length (see <see cref="Bare"/>).</summary>
    public static int Length(string? name) => Bare(name).Length;

    /// <summary>True when the counted length is above <see cref="Limit"/>.</summary>
    public static bool ExceedsLimit(string? name) => Length(name) > Limit;

    /// <summary>True when the typed name ends with <c>$</c> (a gMSA / computer-style account name).</summary>
    public static bool HasGmsaSuffix(string? name) => name?.Trim().EndsWith('$') == true;
}
