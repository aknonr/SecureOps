namespace SecureOps.Shared.Contracts.Access;

/// <summary>Provider-neutral logout result.</summary>
public sealed record LogoutResponse(string Status, string AuthenticationSource);
