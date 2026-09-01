using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Api.Security;

/// <summary>Registers optional corporate bearer authentication without bypassing SecureOps access.</summary>
public static class OidcApiAuthentication
{
    /// <summary>Adds the active API authentication schemes.</summary>
    public static void AddSecureOpsApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        OidcConfigurationValidator.ValidateApi(configuration, environmentName);
        OidcOptions oidc = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new();
        bool demoEnabled = DemoApiAuthentication.IsEnabled(environmentName, configuration);
        services.Configure<OidcOptions>(configuration.GetSection(OidcOptions.SectionName));

        string defaultScheme = SelectDefaultScheme(oidc.Enabled, demoEnabled);
        AuthenticationBuilder authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = defaultScheme;
            options.DefaultAuthenticateScheme = defaultScheme;
            options.DefaultChallengeScheme = defaultScheme;
        });

        if (oidc.Enabled && demoEnabled)
        {
            string demoHeader = configuration[$"{DemoApiAuthOptions.SectionName}:HeaderName"]
                ?? "X-SecureOps-Demo-Actor";
            authentication.AddPolicyScheme(
                ExternalIdentityClaimTypes.CompositeApiScheme,
                displayName: null,
                options => options.ForwardDefaultSelector = context =>
                {
                    string authorization = context.Request.Headers.Authorization.ToString();
                    if (!string.IsNullOrWhiteSpace(authorization))
                    {
                        return ExternalIdentityClaimTypes.OidcBearerScheme;
                    }

                    return context.Request.Headers.ContainsKey(demoHeader)
                        ? DemoApiAuthentication.SchemeName
                        : ExternalIdentityClaimTypes.OidcBearerScheme;
                });
        }

        if (demoEnabled)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DemoApiAuthenticationHandler>(
                DemoApiAuthentication.SchemeName,
                configureOptions: null);
        }

        if (oidc.Enabled)
        {
            authentication.AddJwtBearer(ExternalIdentityClaimTypes.OidcBearerScheme, options =>
                ConfigureBearer(options, oidc));
        }
        else if (!demoEnabled)
        {
            authentication.AddNegotiate();
        }
    }

    private static string SelectDefaultScheme(bool oidcEnabled, bool demoEnabled)
    {
        if (oidcEnabled && demoEnabled)
        {
            return ExternalIdentityClaimTypes.CompositeApiScheme;
        }

        if (oidcEnabled)
        {
            return ExternalIdentityClaimTypes.OidcBearerScheme;
        }

        return demoEnabled
            ? DemoApiAuthentication.SchemeName
            : Microsoft.AspNetCore.Authentication.Negotiate.NegotiateDefaults.AuthenticationScheme;
    }

    private static void ConfigureBearer(JwtBearerOptions options, OidcOptions configured)
    {
        options.Authority = configured.Authority.TrimEnd('/');
        options.MetadataAddress = configured.MetadataAddress;
        options.Audience = configured.ApiAudience;
        options.RequireHttpsMetadata = configured.RequireHttpsMetadata;
        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = configured.ApiAudience,
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
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                OidcExternalIdentityNormalizationResult result = context.HttpContext.RequestServices
                    .GetRequiredService<OidcExternalIdentityNormalizer>()
                    .Normalize(context.Principal!);
                if (!result.IsValid || result.Principal is null)
                {
                    context.Fail(result.ErrorCode ?? "OIDC identity could not be normalized.");
                    return Task.CompletedTask;
                }

                context.Principal = result.Principal;
                return Task.CompletedTask;
            }
        };
    }
}
