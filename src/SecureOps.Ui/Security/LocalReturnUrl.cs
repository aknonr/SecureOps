namespace SecureOps.Ui.Security;

internal static class LocalReturnUrl
{
    internal const string DefaultPath = "dashboard";

    internal static string Sanitize(string? returnUrl, string fallback = DefaultPath)
    {
        string safeFallback = NormalizeFallback(fallback);
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return safeFallback;
        }

        string normalized = returnUrl.Trim();
        if (normalized.Length > 256
            || normalized.Any(char.IsControl)
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || normalized.StartsWith("\\\\", StringComparison.Ordinal)
            || normalized.Contains("://", StringComparison.Ordinal)
            || normalized.Contains('\\', StringComparison.Ordinal)
            || !Uri.TryCreate(normalized, UriKind.Relative, out _))
        {
            return safeFallback;
        }

        string relativePath = normalized.TrimStart('/');
        if (string.IsNullOrWhiteSpace(relativePath) || IsAuthenticationPath(relativePath))
        {
            return safeFallback;
        }

        return relativePath;
    }

    /// <summary>
    /// Rejects return paths that would re-enter the authentication flow.
    /// </summary>
    /// <remarks>
    /// Without this, a returnUrl of <c>auth/sign-out</c> would sign the operator out immediately after
    /// signing in, and <c>login</c> or <c>session-expired</c> would bounce them in a loop. The legacy
    /// <c>demo-auth</c> prefix stays blocked so a stale bookmark cannot slip through.
    /// </remarks>
    private static bool IsAuthenticationPath(string relativePath)
    {
        string[] blockedPrefixes = ["auth/", "auth", "demo-auth", "login", "signed-out", "session-expired"];

        return blockedPrefixes.Any(prefix =>
            relativePath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(prefix + "?", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeFallback(string fallback)
    {
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return DefaultPath;
        }

        return fallback.Trim().TrimStart('/');
    }
}
