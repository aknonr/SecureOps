namespace SecureOps.Infrastructure.Access;

/// <summary>Stable authentication-provider-neutral corporate principal.</summary>
public sealed record CorporatePrincipal(
    string Identifier,
    string AuthenticationSource,
    string? LoginName = null,
    string? DisplayName = null,
    string? Mail = null,
    string? Uid = null,
    IReadOnlyList<string>? RoleEvidence = null,
    string? Issuer = null,
    string? Subject = null);
