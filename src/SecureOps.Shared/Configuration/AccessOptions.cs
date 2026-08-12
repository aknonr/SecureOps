namespace SecureOps.Shared.Configuration;

/// <summary>Configuration for authentication-independent application access.</summary>
public sealed class AccessOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Access";

    /// <summary>Persistence provider: InMemory or SqlServer.</summary>
    public string RepositoryProvider { get; set; } = "InMemory";

    /// <summary>Whether first authentication creates one pending request.</summary>
    public bool AutoCreateRequest { get; set; } = true;

    /// <summary>Allows fixed Demo/Test actors to bootstrap only in the explicit demo authentication path.</summary>
    public bool DemoCompatibilityEnabled { get; set; }

    /// <summary>Exact principals allowed to initialize the Admin role through controlled runtime configuration.</summary>
    public string[] BootstrapAdministrators { get; set; } = [];

    /// <summary>Claim type containing an OIDC subject when OIDC is approved later.</summary>
    public string OidcSubjectClaimType { get; set; } = "sub";

    /// <summary>Claim type containing an OIDC issuer when OIDC is approved later.</summary>
    public string OidcIssuerClaimType { get; set; } = "iss";
}
