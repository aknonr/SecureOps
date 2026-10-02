using System.Globalization;
using System.Text;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// A validated, bounded first-name or full-name query (ADR-0025). Only letters, spaces, apostrophes, hyphens and periods
/// are accepted; wildcards and LDAP filter characters are rejected before any provider call (and escaped again when the
/// filter is built). Exact-account lookup (ADR-0008) is unchanged and separate.
/// </summary>
public sealed class DirectoryNameQuery
{
    /// <summary>Minimum number of letters a query must contain.</summary>
    public const int MinimumLetters = 3;

    /// <summary>Maximum query length after whitespace normalization.</summary>
    public const int MaximumLength = 64;

    /// <summary>Maximum number of name tokens (given names and surname).</summary>
    public const int MaximumTokens = 4;

    /// <summary>Maximum results returned to a caller.</summary>
    public const int MaximumResults = 10;

    private static readonly CultureInfo _turkish = CultureInfo.GetCultureInfo("tr-TR");

    private DirectoryNameQuery(string text, IReadOnlyList<string> tokens)
    {
        Text = text;
        Tokens = tokens;
        Key = DirectoryNameMatching.Fold(text);
        TokenKeys = [.. tokens.Select(DirectoryNameMatching.Fold)];
    }

    /// <summary>Normalized query text (single spaces, trimmed).</summary>
    public string Text { get; }

    /// <summary>Name tokens.</summary>
    public IReadOnlyList<string> Tokens { get; }

    /// <summary>Turkish- and accent-insensitive comparison key of the whole query.</summary>
    public string Key { get; }

    /// <summary>Comparison keys of the tokens.</summary>
    public IReadOnlyList<string> TokenKeys { get; }

    /// <summary>Validates and normalizes a query; returns null with a stable error code when it is not acceptable.</summary>
    public static DirectoryNameQuery? TryCreate(string? input, out string? error)
    {
        string text = string.Join(' ', (input ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (text.Length > MaximumLength)
        {
            error = "NameQueryTooLong";
            return null;
        }

        if (text.Any(c => !(char.IsLetter(c) || c is ' ' or '\'' or '-' or '.' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)))
        {
            error = "NameQueryCharacters";
            return null;
        }

        if (text.Count(char.IsLetter) < MinimumLetters)
        {
            error = "NameQueryTooShort";
            return null;
        }

        string[] tokens = text.Split(' ');
        if (tokens.Length > MaximumTokens)
        {
            error = "NameQueryTooManyWords";
            return null;
        }

        error = null;
        return new DirectoryNameQuery(text.Normalize(NormalizationForm.FormC), [.. tokens.Select(t => t.Normalize(NormalizationForm.FormC))]);
    }

    /// <summary>
    /// Spellings sent to the directory. Directory matching is case-insensitive but does not equate the Turkish dotted and
    /// dotless I with I/i, so the typed form, the Turkish title/upper/lower forms and the invariant forms are all asked for.
    /// </summary>
    public IReadOnlyList<string> Variants(string value) =>
    [
        .. new[]
        {
            value,
            Title(value, _turkish),
            value.ToUpper(_turkish),
            value.ToLower(_turkish),
            Title(value, CultureInfo.InvariantCulture),
            value.ToUpperInvariant()
        }.Distinct(StringComparer.Ordinal)
    ];

    private static string Title(string value, CultureInfo culture) => string.Join(' ', value.Split(' ').Select(word =>
        word.Length == 0 ? word : word[..1].ToUpper(culture) + word[1..].ToLower(culture)));
}

/// <summary>RFC 4515 escaping and the bounded name filter.</summary>
public static class DirectoryNameFilter
{
    /// <summary>Escapes a value for an LDAP filter assertion (RFC 4515): NUL, '(', ')', '*' and '\'.</summary>
    public static string Escape(string value)
    {
        StringBuilder escaped = new(value.Length);
        foreach (char c in value)
        {
            escaped.Append(c switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => c.ToString()
            });
        }

        return escaped.ToString();
    }

    /// <summary>
    /// Enabled and disabled user objects whose display name or given name starts with the query, or (several words) whose
    /// given name starts with the first word and surname with the last word. Only a trailing wildcard is ever added.
    /// </summary>
    public static string Build(DirectoryNameQuery query)
    {
        StringBuilder any = new();
        foreach (string variant in query.Variants(query.Text))
        {
            string value = Escape(variant);
            any.Append("(displayName=").Append(value).Append("*)");
            if (query.Tokens.Count == 1)
            {
                any.Append("(givenName=").Append(value).Append("*)");
            }
        }

        if (query.Tokens.Count > 1)
        {
            IReadOnlyList<string> first = query.Variants(query.Tokens[0]), last = query.Variants(query.Tokens[^1]);
            for (int i = 0; i < Math.Min(first.Count, last.Count); i++)
            {
                any.Append("(&(givenName=").Append(Escape(first[i])).Append("*)(sn=").Append(Escape(last[i])).Append("*))");
            }
        }

        return $"(&(objectCategory=person)(objectClass=user)(|{any}))";
    }
}

/// <summary>One directory candidate with only the fields needed to tell results apart.</summary>
/// <param name="DisplayName">Display name.</param>
/// <param name="GivenName">Given name (matching only; not returned to callers).</param>
/// <param name="Surname">Surname (matching only; not returned to callers).</param>
/// <param name="SamAccountName">Account name.</param>
/// <param name="Department">Department, to tell same-named people apart.</param>
public sealed record DirectoryNameCandidate(string? DisplayName, string? GivenName, string? Surname, string SamAccountName, string? Department);

/// <summary>Bounded provider answer.</summary>
/// <param name="Candidates">At most the requested limit.</param>
/// <param name="Truncated">More entries matched than the limit.</param>
public sealed record DirectoryNameSearchResult(IReadOnlyList<DirectoryNameCandidate> Candidates, bool Truncated);

/// <summary>Read-only bounded name search over the configured identity directory.</summary>
public interface IDirectoryNameSearchProvider
{
    /// <summary>Searches at most <paramref name="limit"/> users; never modifies the directory.</summary>
    public Task<DirectoryNameSearchResult> SearchAsync(DirectoryNameQuery query, int limit, CancellationToken cancellationToken);
}

/// <summary>Provider-independent matching and ordering so every provider answers the same way.</summary>
public static class DirectoryNameMatching
{
    private static readonly CultureInfo _turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Turkish- and accent-insensitive key: İ/I/ı/i equal, ş/ğ/ç/ö/ü fold to their base letters.</summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string lower = value.Normalize(NormalizationForm.FormC).ToLower(_turkish).Replace('ı', 'i');
        StringBuilder folded = new(lower.Length);
        foreach (char c in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(c);
            }
        }

        return string.Join(' ', folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Whether a candidate really matches (also guards against a provider returning more than asked).</summary>
    public static bool Matches(DirectoryNameQuery query, DirectoryNameCandidate candidate)
    {
        string display = Fold(candidate.DisplayName), given = Fold(candidate.GivenName), surname = Fold(candidate.Surname);
        return display.StartsWith(query.Key, StringComparison.Ordinal)
            || query.TokenKeys.Count == 1 && given.StartsWith(query.Key, StringComparison.Ordinal)
            || query.TokenKeys.Count > 1 && given.StartsWith(query.TokenKeys[0], StringComparison.Ordinal)
                && surname.StartsWith(query.TokenKeys[^1], StringComparison.Ordinal);
    }

    /// <summary>Exact full-name matches first, then by display name and account (deterministic).</summary>
    public static IEnumerable<DirectoryNameCandidate> Order(DirectoryNameQuery query, IEnumerable<DirectoryNameCandidate> candidates) => candidates
        .OrderBy(c => Fold(c.DisplayName) == query.Key ? 0 : 1)
        .ThenBy(c => Fold(c.DisplayName), StringComparer.Ordinal)
        .ThenBy(c => c.SamAccountName, StringComparer.OrdinalIgnoreCase);
}
