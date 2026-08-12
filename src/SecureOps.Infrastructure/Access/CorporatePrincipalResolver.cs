using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Access;

/// <summary>Default Windows/Demo principal resolver with an unused future OIDC subject seam.</summary>
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
