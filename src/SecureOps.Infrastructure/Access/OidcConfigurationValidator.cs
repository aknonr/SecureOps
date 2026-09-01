using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Access;

/// <summary>Fail-closed OIDC startup validation shared by the browser UI and API.</summary>
public static class OidcConfigurationValidator
{
    private static readonly Regex _safeScope = new("^[A-Za-z0-9._:/-]{1,128}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    /// <summary>Validates enabled OIDC configuration without contacting provider metadata.</summary>
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        OidcOptions options = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new();
        if (!options.Enabled)
        {
            return;
        }

        ValidateCommon(options, environmentName);
        RequireBounded(options.ClientId, "Oidc:ClientId", 256);
        RequireBounded(options.ApiAudience, "Oidc:ApiAudience", 256);
        ValidatePath(options.CallbackPath, "Oidc:CallbackPath");
        ValidatePath(options.SignedOutCallbackPath, "Oidc:SignedOutCallbackPath");

        if (!options.UsePkce)
        {
            throw new InvalidOperationException("Oidc:UsePkce must be true for Authorization Code flow.");
        }

        if (string.Equals(options.ClientAuthenticationMethod, "None", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(options.ClientSecret))
            {
                throw new InvalidOperationException("Oidc:ClientSecret must be empty when Oidc:ClientAuthenticationMethod is None.");
            }
        }
        else if (string.Equals(options.ClientAuthenticationMethod, "ClientSecretPost", StringComparison.OrdinalIgnoreCase))
        {
            RequireBounded(options.ClientSecret, "Oidc:ClientSecret", 4096);
        }
        else
        {
            throw new InvalidOperationException("Oidc:ClientAuthenticationMethod must be None or ClientSecretPost after the IdP client contract is confirmed.");
        }

        string[] scopes = options.Scopes ?? [];
        if (scopes.Length == 0 || scopes.Length > 16
            || scopes.Any(scope => string.IsNullOrWhiteSpace(scope) || !_safeScope.IsMatch(scope))
            || !scopes.Contains("openid", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Oidc:Scopes must contain openid and no more than 16 bounded scope values.");
        }

    }

    /// <summary>Validates only the API resource-server values; client credentials stay on the UI host.</summary>
    public static void ValidateApi(IConfiguration configuration, string environmentName)
    {
        OidcOptions options = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new();
        if (!options.Enabled)
        {
            return;
        }

        ValidateCommon(options, environmentName);
        RequireBounded(options.ApiAudience, "Oidc:ApiAudience", 256);
    }

    private static void ValidateCommon(OidcOptions options, string environmentName)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out Uri? authority)
            || !string.IsNullOrEmpty(authority.UserInfo)
            || !string.IsNullOrEmpty(authority.Query)
            || !string.IsNullOrEmpty(authority.Fragment)
            || (options.RequireHttpsMetadata && !string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Oidc:Authority must be an absolute trusted authority without embedded credentials, query, or fragment.");
        }

        if (!options.RequireHttpsMetadata
            && !IsSyntheticEnvironment(environmentName))
        {
            throw new InvalidOperationException("Oidc:RequireHttpsMetadata=false is permitted only in Development, Demo, or Test.");
        }

        string[] claimTypes =
        [
            options.IssuerClaimType,
            options.SubjectClaimType,
            options.LoginNameClaimType,
            options.DisplayNameClaimType,
            options.MailClaimType,
            options.UidClaimType,
            options.RoleEvidenceClaimType
        ];
        if (claimTypes.Any(type => string.IsNullOrWhiteSpace(type) || type.Length > 128)
            || claimTypes.Distinct(StringComparer.Ordinal).Count() != claimTypes.Length
            || options.MaxClaimCount is < 8 or > 256
            || options.MaxClaimValueLength is < 64 or > 8192
            || options.MaxRoleEvidenceCount is < 0 or > 32
            || options.MaxAccessTokenLength is < 1024 or > 131_072)
        {
            throw new InvalidOperationException("OIDC claim names or bounds are incomplete or unsafe.");
        }
    }

    private static void ValidatePath(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains("?", StringComparison.Ordinal)
            || value.Contains("#", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{key} must be one local absolute path.");
        }
    }

    private static void RequireBounded(string value, string key, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new InvalidOperationException($"{key} is required and must not exceed {maximumLength} characters.");
        }
    }

    private static bool IsSyntheticEnvironment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
