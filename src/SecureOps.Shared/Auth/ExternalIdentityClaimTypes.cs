namespace SecureOps.Shared.Auth;

/// <summary>Internal normalized external-identity claim names.</summary>
public static class ExternalIdentityClaimTypes
{
    /// <summary>Interactive OIDC authentication scheme used by the browser-facing UI.</summary>
    public const string OidcInteractiveScheme = "SecureOpsOidc";
    /// <summary>Bearer authentication scheme used by the API.</summary>
    public const string OidcBearerScheme = "SecureOpsOidcBearer";
    /// <summary>Composite selector used while DemoAuth remains available.</summary>
    public const string CompositeApiScheme = "SecureOpsApiAuthentication";
    /// <summary>Authentication provider name.</summary>
    public const string AuthenticationProvider = "secureops:external:provider";
    /// <summary>Validated OIDC issuer.</summary>
    public const string Issuer = "secureops:external:issuer";
    /// <summary>Validated OIDC subject.</summary>
    public const string Subject = "secureops:external:subject";
    /// <summary>Opaque stable issuer/subject identifier.</summary>
    public const string StableIdentifier = "secureops:external:stable_id";
    /// <summary>Principal name used for audit and downstream exact identity resolution.</summary>
    public const string PrincipalName = "secureops:external:principal_name";
    /// <summary>Corporate login name when supplied.</summary>
    public const string LoginName = "secureops:external:loginname";
    /// <summary>Display name when supplied.</summary>
    public const string DisplayName = "secureops:external:displayname";
    /// <summary>Mail address when supplied.</summary>
    public const string Mail = "secureops:external:mail";
    /// <summary>Additional corporate identifier when supplied.</summary>
    public const string Uid = "secureops:external:uid";
    /// <summary>Non-authoritative role evidence. Never configured as the identity role claim type.</summary>
    public const string RoleEvidence = "secureops:external:role_evidence";
}
