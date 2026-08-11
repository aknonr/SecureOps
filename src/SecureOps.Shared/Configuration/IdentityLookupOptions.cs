namespace SecureOps.Shared.Configuration;

/// <summary>
/// Configuration for Phase 1A identity lookup.
/// </summary>
public sealed class IdentityLookupOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "IdentityLookup";

    /// <summary>
    /// Provider name. Supported values are Mock and ActiveDirectory.
    /// </summary>
    public string Provider { get; set; } = "Mock";

    /// <summary>
    /// Whether DOMAIN\user input should normalize to user.
    /// </summary>
    public bool StripDomainPrefix { get; set; } = true;

    /// <summary>
    /// Whether normalized account values should be lower-cased with invariant culture.
    /// </summary>
    public bool NormalizeToLowerInvariant { get; set; } = true;

    /// <summary>
    /// Whether UPN-shaped input may be queried as userPrincipalName after sAMAccountName.
    /// </summary>
    public bool EnableUpnLookup { get; set; } = true;

    /// <summary>
    /// Maximum accepted account length after trimming and domain-prefix processing.
    /// </summary>
    public int MaxAccountLength { get; set; } = 128;

    /// <summary>
    /// Regex pattern allowed for normalized account values.
    /// </summary>
    public string AllowedAccountPattern { get; set; } = "^[a-zA-Z0-9._@-]+$";

    /// <summary>
    /// Regex timeout in milliseconds.
    /// </summary>
    public int RegexTimeoutMilliseconds { get; set; } = 250;

    /// <summary>
    /// Maximum time to wait for a real identity provider call.
    /// </summary>
    public int ProviderTimeoutSeconds { get; set; } = 3;

    /// <summary>Maximum accounts accepted by one bulk lookup request.</summary>
    public int BulkMaxAccounts { get; set; } = 20;

    /// <summary>
    /// Rate-limit settings for the privileged identity lookup endpoint.
    /// </summary>
    public IdentityLookupRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// Optional AD domain name for PrincipalContext.
    /// </summary>
    public string? DomainName { get; set; }

    /// <summary>
    /// Optional AD container distinguished name.
    /// </summary>
    public string? Container { get; set; }
}

/// <summary>
/// Rate-limit settings for Phase 1A identity lookup.
/// </summary>
public sealed class IdentityLookupRateLimitOptions
{
    /// <summary>
    /// Maximum accepted lookup requests within the configured window.
    /// </summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>
    /// Rate-limit window in minutes.
    /// </summary>
    public int WindowMinutes { get; set; } = 1;
}
