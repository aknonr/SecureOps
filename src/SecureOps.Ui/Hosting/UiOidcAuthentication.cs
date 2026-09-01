using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Hosting;

/// <summary>Registers the disabled-by-default browser OIDC code flow.</summary>
public static class UiOidcAuthentication
{
    /// <summary>Adds interim cookie authentication and optional corporate OIDC.</summary>
    public static AuthenticationBuilder AddSecureOpsUiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        OidcConfigurationValidator.Validate(configuration, environmentName);
        OidcOptions configured = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new();
        services.Configure<OidcOptions>(configuration.GetSection(OidcOptions.SectionName));
        services.AddSingleton<OidcExternalIdentityNormalizer>();
        services.AddSingleton<IOidcBackchannelClient, OidcBackchannelClient>();

        AuthenticationBuilder authentication = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = configured.Enabled
                    ? ExternalIdentityClaimTypes.OidcInteractiveScheme
                    : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options => ConfigureCookie(options, configured.Enabled));

        if (configured.Enabled)
        {
            authentication.AddOpenIdConnect(ExternalIdentityClaimTypes.OidcInteractiveScheme, options =>
                ConfigureOidc(options, configured));
        }

        return authentication;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, bool oidcEnabled)
    {
        options.Cookie.Name = "__Host-SecureOpsUi.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            if (context.Principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            {
                return;
            }

            Claim? browserSession = identity.FindFirst(SignedInUserService.BrowserSessionClaim);
            if (browserSession is null)
            {
                browserSession = new Claim(SignedInUserService.BrowserSessionClaim, Guid.NewGuid().ToString("N"));
                identity.AddClaim(browserSession);
                context.ShouldRenew = true;
            }

            bool oidcPrincipal = string.Equals(
                identity.FindFirst(SignedInUserService.AuthenticationSourceClaim)?.Value,
                "oidc",
                StringComparison.Ordinal);
            if (oidcPrincipal)
            {
                IApiSessionStore sessions = context.HttpContext.RequestServices.GetRequiredService<IApiSessionStore>();
                TimeProvider timeProvider = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                OidcAccessTokenResult token = oidcEnabled
                    ? await sessions.GetOrCreate(browserSession.Value).GetOidcAccessTokenAsync(
                        timeProvider.GetUtcNow(),
                        context.HttpContext.RequestServices.GetRequiredService<IOptions<OidcOptions>>().Value,
                        context.HttpContext.RequestServices.GetRequiredService<IOidcBackchannelClient>(),
                        context.HttpContext.RequestAborted)
                    : new OidcAccessTokenResult(null, true);
                if (token.RequiresReauthentication)
                {
                    sessions.Remove(browserSession.Value);
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    }

    private static void ConfigureOidc(OpenIdConnectOptions options, OidcOptions configured)
    {
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Authority = configured.Authority.TrimEnd('/');
        options.MetadataAddress = configured.MetadataAddress;
        options.ClientId = configured.ClientId;
        options.ClientSecret = string.Equals(
            configured.ClientAuthenticationMethod,
            "ClientSecretPost",
            StringComparison.OrdinalIgnoreCase)
            ? configured.ClientSecret
            : null;
        options.CallbackPath = configured.CallbackPath;
        options.SignedOutCallbackPath = configured.SignedOutCallbackPath;
        options.SignedOutRedirectUri = "/signed-out?provider=oidc";
        options.RequireHttpsMetadata = configured.RequireHttpsMetadata;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = configured.UsePkce;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SaveTokens = false;
        options.Scope.Clear();
        foreach (string scope in configured.Scopes)
        {
            options.Scope.Add(scope);
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = configured.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerValidator = (issuer, _, _) => string.Equals(
                issuer,
                configured.Authority.TrimEnd('/'),
                StringComparison.Ordinal)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException("The OIDC token issuer did not match the configured authority."),
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = configured.LoginNameClaimType,
            RoleClaimType = ClaimTypes.Role
        };
        options.CorrelationCookie.Name = "__Host-SecureOpsUi.Oidc.Correlation.";
        options.CorrelationCookie.Path = "/";
        options.CorrelationCookie.HttpOnly = true;
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.Name = "__Host-SecureOpsUi.Oidc.Nonce.";
        options.NonceCookie.Path = "/";
        options.NonceCookie.HttpOnly = true;
        options.NonceCookie.SameSite = SameSiteMode.None;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;

        options.Events = new OpenIdConnectEvents
        {
            OnAuthorizationCodeReceived = context => RedeemCodeAsync(context, configured),
            OnTokenValidated = context => ValidateNormalizeAndStoreAsync(context, configured),
            OnRedirectToIdentityProviderForSignOut = context =>
            {
                string? idTokenHint = context.Properties.GetParameter<string>("secureops:id_token_hint");
                if (!string.IsNullOrWhiteSpace(idTokenHint))
                {
                    context.ProtocolMessage.IdTokenHint = idTokenHint;
                }

                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/login?error=sign-in-failed");
                return Task.CompletedTask;
            }
        };
    }

    private static async Task RedeemCodeAsync(AuthorizationCodeReceivedContext context, OidcOptions configured)
    {
        OpenIdConnectConfiguration discovery = await context.Options.ConfigurationManager!
            .GetConfigurationAsync(context.HttpContext.RequestAborted);
        ValidateDiscoveryIssuer(discovery, configured);
        Uri endpoint = RequireHttpsEndpoint(discovery.TokenEndpoint, "token");
        OpenIdConnectMessage tokenRequest = context.TokenEndpointRequest!;
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            ["redirect_uri"] = tokenRequest.RedirectUri,
            ["client_id"] = configured.ClientId,
            ["grant_type"] = OpenIdConnectGrantTypes.AuthorizationCode,
            ["code"] = tokenRequest.Code
        };
        string? codeVerifier = tokenRequest.GetParameter("code_verifier");
        if (!string.IsNullOrWhiteSpace(codeVerifier))
        {
            parameters["code_verifier"] = codeVerifier;
        }

        if (string.Equals(configured.ClientAuthenticationMethod, "ClientSecretPost", StringComparison.OrdinalIgnoreCase))
        {
            parameters["client_secret"] = configured.ClientSecret;
        }

        try
        {
            OpenIdConnectMessage response = await context.HttpContext.RequestServices
                .GetRequiredService<IOidcBackchannelClient>()
                .PostTokenAsync(endpoint, parameters, context.HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(response.Error))
            {
                context.Fail("The OIDC token endpoint returned an error.");
                return;
            }

            context.Properties!.Items["secureops:custom_code_redemption"] = "true";
            context.HandleCodeRedemption(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Fail("OIDC authorization-code redemption failed.");
        }
    }

    private static async Task ValidateNormalizeAndStoreAsync(TokenValidatedContext context, OidcOptions configured)
    {
        if (context.Principal is null)
        {
            context.Fail("OIDC identity was not supplied.");
            return;
        }

        if (!string.Equals(context.SecurityToken.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            context.Fail("The OIDC ID token was not signed with RS256.");
            return;
        }

        if (context.Properties!.Items.Remove("secureops:custom_code_redemption"))
        {
            context.Options.ProtocolValidator.ValidateTokenResponse(new OpenIdConnectProtocolValidationContext
            {
                ClientId = configured.ClientId,
                ProtocolMessage = context.TokenEndpointResponse,
                ValidatedIdToken = context.SecurityToken,
                Nonce = context.Nonce
            });
        }

        OpenIdConnectConfiguration discovery = await context.Options.ConfigurationManager!
            .GetConfigurationAsync(context.HttpContext.RequestAborted);
        ValidateDiscoveryIssuer(discovery, configured);

        string? accessToken = context.TokenEndpointResponse?.AccessToken;
        if (configured.GetClaimsFromUserInfoEndpoint
            && !string.IsNullOrWhiteSpace(accessToken)
            && NeedsUserInfo(context.Principal, configured))
        {
            await AddUserInfoClaimsAsync(context, configured, discovery, accessToken);
            if (context.Result?.Failure is not null)
            {
                return;
            }
        }

        OidcExternalIdentityNormalizer normalizer = context.HttpContext.RequestServices
            .GetRequiredService<OidcExternalIdentityNormalizer>();
        OidcExternalIdentityNormalizationResult normalized = normalizer.Normalize(context.Principal);
        bool expiresParsed = int.TryParse(
            context.TokenEndpointResponse?.ExpiresIn,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int expiresInSeconds);
        if (!normalized.IsValid
            || normalized.Principal?.Identity is not ClaimsIdentity identity
            || string.IsNullOrWhiteSpace(accessToken)
            || accessToken.Length > configured.MaxAccessTokenLength
            || !expiresParsed
            || expiresInSeconds is < 30 or > 86_400)
        {
            context.Fail(normalized.ErrorCode ?? "OIDC token response was incomplete or unsafe.");
            return;
        }

        string? refreshToken = context.TokenEndpointResponse?.RefreshToken;
        string? idToken = context.TokenEndpointResponse?.IdToken;
        if (refreshToken?.Length > configured.MaxServerTokenLength
            || idToken?.Length > configured.MaxServerTokenLength)
        {
            context.Fail("OIDC server token material exceeded the configured limit.");
            return;
        }

        string browserSessionKey = Guid.NewGuid().ToString("N");
        identity.AddClaim(new Claim(SignedInUserService.BrowserSessionClaim, browserSessionKey));
        context.Principal = normalized.Principal;
        DateTimeOffset now = context.HttpContext.RequestServices
            .GetRequiredService<TimeProvider>()
            .GetUtcNow();
        context.HttpContext.RequestServices
            .GetRequiredService<IApiSessionStore>()
            .GetOrCreate(browserSessionKey)
            .SetOidcTokens(new OidcServerTokenSet(
                accessToken,
                now.AddSeconds(expiresInSeconds),
                refreshToken,
                string.IsNullOrWhiteSpace(refreshToken)
                    ? null
                    : now.AddMinutes(configured.RefreshTokenLifetimeMinutes),
                idToken,
                RequireHttpsEndpoint(discovery.TokenEndpoint, "token")));
    }

    private static async Task AddUserInfoClaimsAsync(
        TokenValidatedContext context,
        OidcOptions configured,
        OpenIdConnectConfiguration discovery,
        string accessToken)
    {
        try
        {
            Uri endpoint = RequireHttpsEndpoint(discovery.UserInfoEndpoint, "UserInfo");
            using JsonDocument document = await context.HttpContext.RequestServices
                .GetRequiredService<IOidcBackchannelClient>()
                .GetUserInfoAsync(endpoint, accessToken, context.HttpContext.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !TryScalar(document.RootElement, configured.SubjectClaimType, out string? subject)
                || !string.Equals(
                    subject,
                    context.Principal!.FindFirst(configured.SubjectClaimType)?.Value,
                    StringComparison.Ordinal))
            {
                context.Fail("OIDC UserInfo subject validation failed.");
                return;
            }

            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            foreach (string claimType in ReviewedUserInfoClaims(configured))
            {
                if (identity.HasClaim(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
                    || !document.RootElement.TryGetProperty(claimType, out JsonElement value))
                {
                    continue;
                }

                IEnumerable<string> values = value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)
                    : value.ValueKind == JsonValueKind.String ? [value.GetString()!] : [];
                foreach (string claimValue in values)
                {
                    identity.AddClaim(new Claim(claimType, claimValue));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Fail("OIDC UserInfo retrieval failed.");
        }
    }

    private static bool NeedsUserInfo(ClaimsPrincipal principal, OidcOptions configured) =>
        ReviewedUserInfoClaims(configured).Any(type => principal.FindFirst(type) is null);

    private static string[] ReviewedUserInfoClaims(OidcOptions configured) =>
    [
        configured.LoginNameClaimType,
        configured.DisplayNameClaimType,
        configured.MailClaimType,
        configured.UidClaimType,
        configured.RoleEvidenceClaimType
    ];

    private static bool TryScalar(JsonElement root, string name, out string? value)
    {
        value = root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void ValidateDiscoveryIssuer(OpenIdConnectConfiguration discovery, OidcOptions configured)
    {
        if (!string.Equals(discovery.Issuer, configured.Authority.TrimEnd('/'), StringComparison.Ordinal))
        {
            throw new SecurityTokenInvalidIssuerException("OIDC discovery issuer did not match the configured authority.");
        }
    }

    private static Uri RequireHttpsEndpoint(string? value, string endpointName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint)
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException($"OIDC {endpointName} endpoint was not an absolute HTTPS URL.");
        }

        return endpoint;
    }
}
