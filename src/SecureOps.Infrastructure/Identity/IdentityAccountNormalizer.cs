using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Config-based normalizer for exact identity lookup account values.
/// </summary>
public sealed class IdentityAccountNormalizer : IIdentityAccountNormalizer
{
    private static readonly char[] _forbiddenCharacters = ['*', '%', '?', '[', ']', '(', ')', '|', '&', '=', '<', '>', '"'];
    private readonly IdentityLookupOptions _options;

    /// <summary>
    /// Initializes a new normalizer.
    /// </summary>
    /// <param name="options">Identity lookup options.</param>
    public IdentityAccountNormalizer(IOptions<IdentityLookupOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public IdentityAccountNormalizationResult Normalize(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return Invalid("EmptyAccount", "Account is required.");
        }

        string normalized = account.Trim();

        if (ContainsBulkSeparator(normalized))
        {
            return Invalid("BulkLookupRejected", "Only one exact account can be looked up per request.");
        }

        if (normalized.Any(char.IsControl) || normalized.IndexOfAny(_forbiddenCharacters) >= 0
            || !IdentityProviderInputGuard.HasSafeDollarSuffix(normalized))
        {
            return Invalid("SearchPatternRejected", "Wildcard, LDAP filter, or invalid account suffix is not allowed.");
        }

        if (_options.StripDomainPrefix)
        {
            int slashIndex = normalized.LastIndexOf('\\');
            if (slashIndex >= 0)
            {
                normalized = normalized[(slashIndex + 1)..];
            }
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Invalid("EmptyAccount", "Account is empty after normalization.");
        }

        if (normalized.Length > _options.MaxAccountLength)
        {
            return Invalid("AccountTooLong", $"Account exceeds {_options.MaxAccountLength} characters.");
        }

        if (normalized.IndexOfAny(_forbiddenCharacters) >= 0)
        {
            return Invalid("SearchPatternRejected", "Wildcard, LDAP filter, or search-style characters are not allowed.");
        }

        if (!IdentityProviderInputGuard.HasSafeDollarSuffix(normalized) || !Regex.IsMatch(
                normalized,
                _options.AllowedAccountPattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(_options.RegexTimeoutMilliseconds)))
        {
            return Invalid("AccountPatternRejected", "Account contains characters outside the configured allow-list.");
        }

        if (_options.NormalizeToLowerInvariant)
        {
            normalized = normalized.ToLowerInvariant();
        }

        return new IdentityAccountNormalizationResult(true, normalized, null, null);
    }

    private static bool ContainsBulkSeparator(string value)
    {
        return value.Any(char.IsWhiteSpace)
            || value.Contains(',')
            || value.Contains(';')
            || value.Contains('\r')
            || value.Contains('\n');
    }

    private static IdentityAccountNormalizationResult Invalid(string code, string message)
    {
        return new IdentityAccountNormalizationResult(false, null, code, message);
    }
}
