using System.Globalization;
using System.Text;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>
/// Deterministic identity normalization. Display names are never identity keys: exact person keys
/// use Unicode NFKC, whitespace standardization and Turkish casing; accent folding only produces
/// candidates. Account names are never fuzzy-corrected.
/// </summary>
public static class ServiceAccountText
{
    private static readonly CultureInfo _turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Values that mean "unknown" in legacy sources and must never become a team or person.</summary>
    private static readonly string[] _placeholders = ["BELİRLENECEK", "BİLİNMİYOR", "TEYİT BEKLİYOR", "-", "—", "YOK", "N/A"];

    /// <summary>Trims, converts NBSP and control whitespace, collapses runs and applies NFKC; empty becomes null.</summary>
    public static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = value.Normalize(NormalizationForm.FormKC);
        StringBuilder builder = new(normalized.Length);
        bool space = false;
        foreach (char c in normalized)
        {
            if (char.IsWhiteSpace(c) || c == ' ' || char.IsControl(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Account key: cleaned, invariant upper case, no fuzzy correction or letter substitution.</summary>
    public static string? AccountKey(string? name) => Clean(name)?.ToUpperInvariant();

    /// <summary>Domain key: cleaned and upper-cased; null when unknown.</summary>
    public static string? DomainKey(string? domain) => Clean(domain)?.ToUpperInvariant();

    /// <summary>Exact person/team label key with Turkish casing (i→İ, ı→I); preserves accents.</summary>
    public static string? LabelKey(string? label) => Clean(label)?.ToUpper(_turkish);

    /// <summary>Accent-folded candidate key. Equality means "possibly the same", never identity.</summary>
    public static string? CandidateKey(string? label)
    {
        string? exact = LabelKey(label);
        if (exact is null)
        {
            return null;
        }

        StringBuilder builder = new(exact.Length);
        foreach (char c in exact.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c switch { 'İ' or 'ı' => 'I', _ => c });
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>True when the label is a legacy placeholder for an unknown team/person.</summary>
    public static bool IsPlaceholder(string? label)
    {
        string? key = LabelKey(label);
        return key is null || _placeholders.Contains(key, StringComparer.Ordinal);
    }

    /// <summary>Exact label comparison under the person/team key rules.</summary>
    public static bool Same(string? left, string? right) =>
        LabelKey(left) is { } l && string.Equals(l, LabelKey(right), StringComparison.Ordinal);

    /// <summary>Identity key: confirmed domain wins; otherwise one explicit unknown-directory provisional scope.</summary>
    public static string IdentityKey(string accountKey, string? domainKey) =>
        domainKey is null ? "P:UNKNOWN|" + accountKey : "D:" + domainKey + "|" + accountKey;

    /// <summary>Normalizes an external record number; types are never mixed.</summary>
    public static string? RecordNumber(string? number) => Clean(number)?.ToUpperInvariant();
}
