using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Access;

/// <summary>Resolves normalized OIDC and existing Windows/Demo principals for persisted access.</summary>
public sealed class CorporatePrincipalResolver : ICorporatePrincipalResolver
{
    private readonly AccessOptions _options;

    /// <summary>Initializes the resolver.</summary>
    public CorporatePrincipalResolver(IOptions<AccessOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public CorporatePrincipal? Resolve(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? authenticationSource = principal.FindFirst("secureops:auth_source")?.Value
            ?? principal.Identity.AuthenticationType;
        string? stableIdentifier = principal.FindFirst(ExternalIdentityClaimTypes.StableIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(stableIdentifier))
        {
            string? normalizedIssuer = principal.FindFirst(ExternalIdentityClaimTypes.Issuer)?.Value;
            string? normalizedSubject = principal.FindFirst(ExternalIdentityClaimTypes.Subject)?.Value;
            string? provider = principal.FindFirst(ExternalIdentityClaimTypes.AuthenticationProvider)?.Value;
            if (!string.Equals(authenticationSource, "oidc", StringComparison.Ordinal)
                || !string.Equals(provider, "OIDC", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(normalizedIssuer)
                || string.IsNullOrWhiteSpace(normalizedSubject)
                || !string.Equals(stableIdentifier, OidcExternalIdentityNormalizer.StableIdentifier(normalizedIssuer, normalizedSubject), StringComparison.Ordinal))
            {
                return null;
            }

            return new CorporatePrincipal(
                stableIdentifier,
                "oidc",
                principal.FindFirst(ExternalIdentityClaimTypes.LoginName)?.Value,
                principal.FindFirst(ExternalIdentityClaimTypes.DisplayName)?.Value,
                principal.FindFirst(ExternalIdentityClaimTypes.Mail)?.Value,
                principal.FindFirst(ExternalIdentityClaimTypes.Uid)?.Value,
                principal.FindAll(ExternalIdentityClaimTypes.RoleEvidence).Select(claim => claim.Value).ToArray(),
                normalizedIssuer,
                normalizedSubject);
        }

        string? issuer = principal.FindFirst(_options.OidcIssuerClaimType)?.Value;
        string? subject = principal.FindFirst(_options.OidcSubjectClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(issuer) && !string.IsNullOrWhiteSpace(subject))
        {
            return new CorporatePrincipal($"oidc:{HashSubject(issuer, subject)}", "oidc");
        }

        string? name = principal.Identity.Name?.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new CorporatePrincipal(name, authenticationSource?.Trim() ?? "unknown");
    }

    private static string HashSubject(string issuer, string subject)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer.Trim()}\n{subject.Trim()}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
