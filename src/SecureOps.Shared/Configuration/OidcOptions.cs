namespace SecureOps.Shared.Configuration;

/// <summary>Server-owned corporate OpenID Connect configuration shared by the UI and API.</summary>
public sealed class OidcOptions
{
    private string _loginNameClaim = "loginname";
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Oidc";

    /// <summary>Whether corporate OIDC authentication is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Corporate identity-provider authority used for metadata and issuer validation.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Explicit provider discovery document address, validated separately from the issuer.</summary>
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>Deployment-owned OIDC client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Optional deployment-owned client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Token endpoint authentication method: None or ClientSecretPost.</summary>
    public string ClientAuthenticationMethod { get; set; } = "Unknown";

    /// <summary>Token endpoint request body format: FormUrlEncoded or Json.</summary>
    public string TokenEndpointRequestFormat { get; set; } = "FormUrlEncoded";

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

    /// <summary>
    /// Pushed Authorization Requests: <c>Disable</c> (default, the pre-.NET 9 behaviour), <c>UseIfAvailable</c> or
    /// <c>Require</c>. .NET 9+ would otherwise use PAR automatically whenever the IdP metadata advertises it.
    /// </summary>
    public string PushedAuthorization { get; set; } = "Disable";

    /// <summary>Whether missing configured profile claims may be retrieved from UserInfo.</summary>
    public bool GetClaimsFromUserInfoEndpoint { get; set; }

    /// <summary>Whether local logout should also invoke the provider end-session endpoint.</summary>
    public bool EnableRemoteSignOut { get; set; }

    /// <summary>Issuer claim name.</summary>
    public string IssuerClaimType { get; set; } = "iss";

    /// <summary>Stable subject claim name.</summary>
    public string SubjectClaimType { get; set; } = "sub";

    /// <summary>Corporate login claim name.</summary>
    public string LoginNameClaimType
    {
        get => _loginNameClaim;
        set => _loginNameClaim = value;
    }

    /// <summary>Corporate login claim mapping; runtime alias for LoginNameClaimType.</summary>
    public string LoginNameClaim
    {
        get => _loginNameClaim;
        set => _loginNameClaim = value;
    }

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

    /// <summary>Maximum accepted refresh-token or ID-token length retained in server memory.</summary>
    public int MaxServerTokenLength { get; set; } = 65_536;

    /// <summary>Token/UserInfo backchannel timeout in seconds.</summary>
    public int BackchannelTimeoutSeconds { get; set; } = 15;

    /// <summary>Maximum token or UserInfo response body size.</summary>
    public int MaxBackchannelResponseBytes { get; set; } = 65_536;

    /// <summary>Refreshes an access token this many seconds before expiry.</summary>
    public int AccessTokenRefreshSkewSeconds { get; set; } = 60;

    /// <summary>Maximum server-side refresh-token lifetime when the provider omits one.</summary>
    public int RefreshTokenLifetimeMinutes { get; set; } = 480;
}
