using System.Text.RegularExpressions;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Provider-side guard for exact identity lookup input.
/// </summary>
public static class IdentityProviderInputGuard
{
    private static readonly char[] _forbiddenCharacters = ['*', '%', '?', '[', ']', '(', ')', '|', '&', '=', '<', '>', '"', '\\', '/'];

    /// <summary>
    /// Ensures provider input remains a single exact account value.
    /// </summary>
    /// <param name="normalizedAccount">Normalized account value.</param>
    /// <param name="options">Identity lookup options.</param>
    /// <exception cref="IdentityProviderInputRejectedException">Thrown when the provider input is unsafe.</exception>
    public static void EnsureSafeExactAccount(string normalizedAccount, IdentityLookupOptions options)
    {
        if (string.IsNullOrWhiteSpace(normalizedAccount))
        {
            throw new IdentityProviderInputRejectedException("EmptyAccount", "Provider input is empty.");
        }

        if (normalizedAccount.Length > options.MaxAccountLength)
        {
            throw new IdentityProviderInputRejectedException("AccountTooLong", "Provider input exceeds the configured maximum length.");
        }

        if (ContainsBulkSeparator(normalizedAccount))
        {
            throw new IdentityProviderInputRejectedException("BulkLookupRejected", "Provider input is not a single exact account.");
        }

        if (normalizedAccount.IndexOfAny(_forbiddenCharacters) >= 0)
        {
            throw new IdentityProviderInputRejectedException("SearchPatternRejected", "Provider input contains search-style characters.");
        }

        if (!Regex.IsMatch(
                normalizedAccount,
                options.AllowedAccountPattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(options.RegexTimeoutMilliseconds)))
        {
            throw new IdentityProviderInputRejectedException("AccountPatternRejected", "Provider input is outside the configured allow-list.");
        }
    }

    private static bool ContainsBulkSeparator(string value)
    {
        return value.Any(char.IsWhiteSpace)
            || value.Contains(',')
            || value.Contains(';')
            || value.Contains('\r')
            || value.Contains('\n');
    }
}

/// <summary>
/// Exception thrown when provider-side exact lookup input validation rejects an account.
/// </summary>
public sealed class IdentityProviderInputRejectedException : Exception
{
    /// <summary>
    /// Initializes a new provider input rejection exception.
    /// </summary>
    /// <param name="code">Machine-readable rejection code.</param>
    /// <param name="message">Rejection message.</param>
    public IdentityProviderInputRejectedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Machine-readable rejection code.
    /// </summary>
    public string Code { get; }
}
