using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
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
                string? token = oidcEnabled
                    ? sessions.GetOrCreate(browserSession.Value).GetOidcAccessToken(timeProvider.GetUtcNow())
                    : null;
                if (token is null)
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
            OnTokenValidated = context => NormalizeAndStoreAsync(context, configured),
            OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/login?error=sign-in-failed");
                return Task.CompletedTask;
            }
        };
    }

    private static Task NormalizeAndStoreAsync(TokenValidatedContext context, OidcOptions configured)
    {
        if (context.Principal is null)
        {
            context.Fail("OIDC identity was not supplied.");
            return Task.CompletedTask;
        }

        OidcExternalIdentityNormalizer normalizer = context.HttpContext.RequestServices
            .GetRequiredService<OidcExternalIdentityNormalizer>();
        OidcExternalIdentityNormalizationResult normalized = normalizer.Normalize(context.Principal);
        string? accessToken = context.TokenEndpointResponse?.AccessToken;
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
            return Task.CompletedTask;
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
            .SetOidcAccessToken(accessToken, now.AddSeconds(expiresInSeconds));
        return Task.CompletedTask;
    }
}
