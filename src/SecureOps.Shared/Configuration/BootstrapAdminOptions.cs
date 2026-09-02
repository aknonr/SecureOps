namespace SecureOps.Shared.Configuration;

/// <summary>Server-owned one-time OIDC first-Admin bootstrap configuration.</summary>
public sealed class BootstrapAdminOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "BootstrapAdmin";

    /// <summary>Whether the one-time OIDC bootstrap gate is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Exact corporate login name eligible for first-Admin bootstrap.</summary>
    public string LoginName { get; set; } = string.Empty;

    /// <summary>Exact validated OIDC issuer eligible for first-Admin bootstrap.</summary>
    public string AllowedIssuer { get; set; } = string.Empty;
}
