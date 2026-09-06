using System.Globalization;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Infrastructure.Resources;

/// <summary>Bounded plain-text and positive HTTPS dashboard-query policy; performs no I/O.</summary>
public static class ResourceValidation
{
    private static readonly HashSet<string> _queryKeys = new(StringComparer.OrdinalIgnoreCase)
    { "orgId", "dashboard", "view", "tab", "from", "to", "refresh", "theme", "environment", "location", "page" };

    /// <summary>Plain text excludes controls, markup delimiters, and bidi format controls.</summary>
    public static bool Text(string? value, int max, bool required = false) => value is null
        ? !required
        : value.Length <= max && (!required || !string.IsNullOrWhiteSpace(value))
          && value.All(c => !char.IsControl(c) && c is not '<' and not '>'
              && char.GetUnicodeCategory(c) != UnicodeCategory.Format);

    /// <summary>Accepts HTTPS without credentials/fragments and only simple approved dashboard filters.</summary>
    public static bool Url(string? value)
    {
        if (value is null || value.Length > 2048 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || value.Contains('\\') || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || !uri.IsWellFormedOriginalString() || uri.HostNameType == UriHostNameType.Unknown
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
        {
            return false;
        }

        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path.Any(c => char.IsControl(c)) || path.Contains('%') || path.Contains(';') || path.Contains('=')
            || path.Contains('@') || path.Contains('\\'))
        {
            return false;
        }
        string[] secretSegments = ["token", "password", "secret", "apikey", "api-key", "access_token", "authorization", "sessionid"];
        if (path.Split('/').Any(segment => secretSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (uri.Query.Length == 0)
        {
            return true;
        }

        string[] pairs = value[(value.IndexOf('?') + 1)..].Split('&');
        if (pairs.Length > 10)
        {
            return false;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in pairs)
        {
            string[] parts = pair.Split('=');
            if (parts.Length != 2 || !_queryKeys.Contains(parts[0]) || !seen.Add(parts[0]))
            {
                return false;
            }

            string filter = Uri.UnescapeDataString(parts[1]);
            if (filter.Length is < 1 or > 80 || !filter.All(c => char.IsAsciiLetterOrDigit(c) || "-_.:, ".Contains(c)))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Validates shared category fields before repository access.</summary>
    public static bool Category(SaveResourceCategoryRequest value) => Text(value.Name, 80, true)
        && value.DisplayOrder is >= 0 and <= 100000 && value.ExpectedVersion is >= 0 and < long.MaxValue;

    /// <summary>Validates every link field, including nested tag entries.</summary>
    public static bool Link(SaveResourceLinkRequest value) => value.CategoryId != Guid.Empty
        && Text(value.Name, 120, true) && Url(value.Url) && Text(value.Purpose, 300, true)
        && Text(value.Notes, 1000) && Text(value.Environment, 40) && Text(value.Location, 40)
        && value.DisplayOrder is >= 0 and <= 100000 && value.ExpectedVersion is >= 0 and < long.MaxValue
        && (value.Tags is null || value.Tags.Count <= 10 && value.Tags.All(t => Text(t, 30, true))
            && value.Tags.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == value.Tags.Count);

    /// <summary>Validates query bounds before database access.</summary>
    public static bool Query(ResourceQuery value) => Text(value.Search, 100) && Text(value.Environment, 40)
        && Text(value.Location, 40) && Text(value.Tag, 30) && value.Page is >= 1 and <= 10000
        && value.PageSize is >= 1 and <= 100 && value.CategoryId != Guid.Empty;

    /// <summary>Validates one private set; empty sets are valid.</summary>
    public static bool Set(SaveShiftSetRequest value) => Text(value.Name, 80, true) && value.ExpectedVersion is >= 0 and < long.MaxValue
        && value.LinkIds is not null && value.LinkIds.Count <= 100 && value.LinkIds.All(id => id != Guid.Empty)
        && value.LinkIds.Distinct().Count() == value.LinkIds.Count;
}
