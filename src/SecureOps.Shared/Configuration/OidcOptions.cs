namespace SecureOps.Shared.Configuration;

/// <summary>Server-owned corporate OpenID Connect configuration shared by the UI and API.</summary>
public sealed class OidcOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Oidc";

    /// <summary>Whether corporate OIDC authentication is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Corporate identity-provider authority used for metadata and issuer validation.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Deployment-owned OIDC client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Optional deployment-owned client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Token endpoint authentication method: None or ClientSecretPost.</summary>
    public string ClientAuthenticationMethod { get; set; } = "Unknown";

    /// <summary>Expected API access-token audience.</summary>
    public string ApiAudience { get; set; } = string.Empty;

    /// <summary>Local OIDC authorization callback path.</summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>Local OIDC signed-out callback path.</summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    /// <summary>Requested OIDC scopes. Must include openid.</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>Whether metadata retrieval requires HTTPS.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Whether authorization-code redemption uses PKCE.</summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>Whether local logout should also invoke the provider end-session endpoint.</summary>
    public bool EnableRemoteSignOut { get; set; }

    /// <summary>Issuer claim name.</summary>
    public string IssuerClaimType { get; set; } = "iss";

    /// <summary>Stable subject claim name.</summary>
    public string SubjectClaimType { get; set; } = "sub";

    /// <summary>Corporate login claim name.</summary>
    public string LoginNameClaimType { get; set; } = "loginname";

    /// <summary>Display-name claim name.</summary>
    public string DisplayNameClaimType { get; set; } = "displayname";

    /// <summary>Mail claim name.</summary>
    public string MailClaimType { get; set; } = "mail";

    /// <summary>Additional corporate identifier claim name.</summary>
    public string UidClaimType { get; set; } = "uid";

    /// <summary>Non-authoritative application-role evidence claim name.</summary>
    public string RoleEvidenceClaimType { get; set; } = "uygulama-role";

    /// <summary>Maximum number of claims accepted from one OIDC identity.</summary>
    public int MaxClaimCount { get; set; } = 64;

    /// <summary>Maximum accepted length of one claim value.</summary>
    public int MaxClaimValueLength { get; set; } = 512;

    /// <summary>Maximum number of retained role-evidence values.</summary>
    public int MaxRoleEvidenceCount { get; set; } = 8;

    /// <summary>Maximum accepted access-token length retained by the server-side UI session.</summary>
    public int MaxAccessTokenLength { get; set; } = 32_768;
}
