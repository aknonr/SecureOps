using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Access;

/// <summary>Validated normalized corporate OIDC identity.</summary>
public sealed record OidcExternalIdentity(
    string AuthenticationProvider,
    string Issuer,
    string Subject,
    string StableIdentifier,
    string? LoginName,
    string? DisplayName,
    string? Mail,
    string? Uid,
    IReadOnlyList<string> RoleEvidence);

/// <summary>Bounded OIDC identity normalization outcome.</summary>
public sealed record OidcExternalIdentityNormalizationResult(
    bool IsValid,
    OidcExternalIdentity? Identity,
    ClaimsPrincipal? Principal,
    string? ErrorCode);

/// <summary>Normalizes only reviewed OIDC claims and discards the raw claim set.</summary>
public sealed class OidcExternalIdentityNormalizer
{
    private readonly OidcOptions _options;

    /// <summary>Initializes the normalizer.</summary>
    public OidcExternalIdentityNormalizer(IOptions<OidcOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Returns a bounded provider-neutral principal without granting application roles.</summary>
    public OidcExternalIdentityNormalizationResult Normalize(ClaimsPrincipal source)
    {
        Claim[] claims = source.Claims.ToArray();
        if (claims.Length == 0 || claims.Length > _options.MaxClaimCount
            || claims.Any(claim => claim.Value.Length > _options.MaxClaimValueLength))
        {
            return Invalid("OidcClaimsOutOfBounds");
        }

        if (!TrySingle(claims, _options.IssuerClaimType, required: true, out string? issuer)
            || !TrySingle(claims, _options.SubjectClaimType, required: true, out string? subject)
            || !TrySingle(claims, _options.LoginNameClaimType, required: false, out string? loginName)
            || !TrySingle(claims, _options.DisplayNameClaimType, required: false, out string? displayName)
            || !TrySingle(claims, _options.MailClaimType, required: false, out string? mail)
            || !TrySingle(claims, _options.UidClaimType, required: false, out string? uid))
        {
            return Invalid("OidcClaimsInvalid");
        }

        string[] roleEvidence = claims
            .Where(claim => string.Equals(claim.Type, _options.RoleEvidenceClaimType, StringComparison.Ordinal))
            .Select(claim => claim.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roleEvidence.Length > _options.MaxRoleEvidenceCount)
        {
            return Invalid("OidcClaimsOutOfBounds");
        }

        string stableIdentifier = StableIdentifier(issuer!, subject!);
        string principalName = loginName ?? stableIdentifier;
        OidcExternalIdentity external = new(
            "OIDC",
            issuer!,
            subject!,
            stableIdentifier,
            loginName,
            displayName,
            mail,
            uid,
            roleEvidence);

        List<Claim> normalized =
        [
            new(ExternalIdentityClaimTypes.AuthenticationProvider, external.AuthenticationProvider),
            new(ExternalIdentityClaimTypes.Issuer, external.Issuer),
            new(ExternalIdentityClaimTypes.Subject, external.Subject),
            new(ExternalIdentityClaimTypes.StableIdentifier, external.StableIdentifier),
            new(ExternalIdentityClaimTypes.PrincipalName, principalName),
            new("secureops:auth_source", "oidc")
        ];
        AddOptional(normalized, ExternalIdentityClaimTypes.LoginName, external.LoginName);
        AddOptional(normalized, ExternalIdentityClaimTypes.DisplayName, external.DisplayName);
        AddOptional(normalized, ExternalIdentityClaimTypes.Mail, external.Mail);
        AddOptional(normalized, ExternalIdentityClaimTypes.Uid, external.Uid);
        normalized.AddRange(roleEvidence.Select(value => new Claim(ExternalIdentityClaimTypes.RoleEvidence, value)));

        ClaimsIdentity identity = new(
            normalized,
            authenticationType: "OIDC",
            nameType: ExternalIdentityClaimTypes.PrincipalName,
            roleType: ClaimTypes.Role);
        return new(true, external, new ClaimsPrincipal(identity), null);
    }

    /// <summary>Builds the stable persisted identity from exact issuer and subject values.</summary>
    public static string StableIdentifier(string issuer, string subject)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer.Trim()}\n{subject.Trim()}"));
        return $"oidc:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static bool TrySingle(Claim[] claims, string type, bool required, out string? value)
    {
        string[] values = claims
            .Where(claim => string.Equals(claim.Type, type, StringComparison.Ordinal))
            .Select(claim => claim.Value.Trim())
            .Where(candidate => candidate.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        value = values.Length == 1 ? values[0] : null;
        return values.Length == 1 || (!required && values.Length == 0);
    }

    private static void AddOptional(ICollection<Claim> claims, string type, string? value)
    {
        if (value is not null)
        {
            claims.Add(new Claim(type, value));
        }
    }

    private static OidcExternalIdentityNormalizationResult Invalid(string code) =>
        new(false, null, null, code);
}
