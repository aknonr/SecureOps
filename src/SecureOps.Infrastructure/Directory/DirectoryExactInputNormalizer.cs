using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Normalizes exact group identifiers without accepting LDAP/filter grammar.</summary>
public sealed class DirectoryExactInputNormalizer
{
    private static readonly char[] _forbidden = ['*', '%', '?', '[', ']', '(', ')', '|', '&', '=', '<', '>', '"', ',', ';', '\\', '/', ':'];
    private readonly DirectoryExplorerOptions _options;

    /// <summary>Initializes the normalizer.</summary>
    public DirectoryExactInputNormalizer(IOptions<DirectoryExplorerOptions> options) => _options = options.Value;

    /// <summary>Normalizes one exact group name or sAMAccountName.</summary>
    public DirectoryInputNormalizationResult NormalizeGroup(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            return DirectoryInputNormalizationResult.Invalid("GroupRequired");
        }

        string normalized = group.Trim();
        if (normalized.Length > _options.MaxGroupInputLength
            || normalized.Any(char.IsControl)
            || normalized.IndexOfAny(_forbidden) >= 0
            || !Identity.IdentityProviderInputGuard.HasSafeDollarSuffix(normalized)
            || !Regex.IsMatch(normalized, "^[a-zA-Z0-9._@ -]+\\$?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
        {
            return DirectoryInputNormalizationResult.Invalid("UnsafeExactGroup");
        }

        return DirectoryInputNormalizationResult.Valid(normalized.ToLowerInvariant());
    }
}

/// <summary>Exact directory input normalization result.</summary>
public sealed record DirectoryInputNormalizationResult(bool IsValid, string? Value, string? ErrorCode)
{
    /// <summary>Creates a valid result.</summary>
    public static DirectoryInputNormalizationResult Valid(string value) => new(true, value, null);
    /// <summary>Creates an invalid result.</summary>
    public static DirectoryInputNormalizationResult Invalid(string errorCode) => new(false, null, errorCode);
}
