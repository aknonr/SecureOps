using Microsoft.Extensions.Configuration;

namespace SecureOps.Ui.Configuration;

/// <summary>
/// Canonical configuration for the Phase 1A Identity Lookup API endpoint the UI calls.
/// </summary>
/// <remarks>
/// This is the single source of truth for the API base address. The legacy
/// <c>DemoMode:ApiBaseAddress</c> key is still read as a migration fallback when the canonical
/// key is absent (see <see cref="IdentityLookupApiConfiguration"/>).
/// </remarks>
public sealed class IdentityLookupApiOptions
{
    /// <summary>
    /// Configuration section name: <c>IdentityLookupApi</c>.
    /// </summary>
    public const string SectionName = "IdentityLookupApi";

    /// <summary>
    /// Legacy configuration key kept only for backward-compatible migration.
    /// </summary>
    public const string LegacyBaseAddressKey = "DemoMode:ApiBaseAddress";

    /// <summary>
    /// Absolute base address of the Identity Lookup API. Must end with a trailing slash so that
    /// relative request paths (for example <c>api/v1/identity/lookup</c>) combine correctly.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// HTTP client timeout for API calls.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether the base address was resolved from the legacy <c>DemoMode:ApiBaseAddress</c> key.
    /// </summary>
    public bool UsedLegacyKey { get; set; }
}

/// <summary>
/// Resolves and validates the Identity Lookup API base address from configuration with a safe
/// migration path from the legacy demo key.
/// </summary>
public static class IdentityLookupApiConfiguration
{
    /// <summary>
    /// Resolves the effective base address string and whether the legacy key was used.
    /// The canonical <c>IdentityLookupApi:BaseAddress</c> wins; otherwise the legacy
    /// <c>DemoMode:ApiBaseAddress</c> is used if present.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The resolved value (may be null) and whether the legacy key supplied it.</returns>
    public static (string? Value, bool UsedLegacyKey) ResolveBaseAddress(IConfiguration configuration)
    {
        string? canonical = configuration[$"{IdentityLookupApiOptions.SectionName}:BaseAddress"];
        if (!string.IsNullOrWhiteSpace(canonical))
        {
            return (canonical, false);
        }

        string? legacy = configuration[IdentityLookupApiOptions.LegacyBaseAddressKey];
        return (legacy, !string.IsNullOrWhiteSpace(legacy));
    }

    /// <summary>
    /// Normalizes a resolved value into an absolute URI with a trailing slash, or null if invalid.
    /// </summary>
    /// <param name="value">Resolved configuration value.</param>
    /// <returns>An absolute, slash-terminated URI, or null when the value is not an absolute URI.</returns>
    public static Uri? NormalizeAbsolute(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.AbsoluteUri.EndsWith('/'))
        {
            uri = new Uri(uri.AbsoluteUri + "/");
        }

        return uri;
    }

    /// <summary>Whether the server-to-server API transport protects session credentials.</summary>
    public static bool IsSecureTransport(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && uri.IsLoopback));
}
