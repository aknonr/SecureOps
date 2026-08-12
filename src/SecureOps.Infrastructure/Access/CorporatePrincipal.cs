namespace SecureOps.Infrastructure.Access;

/// <summary>Stable authentication-provider-neutral corporate principal.</summary>
public sealed record CorporatePrincipal(string Identifier, string AuthenticationSource);
