namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Result of resolving a PAM-visible account to a directory lookup account.
/// </summary>
/// <param name="DirectoryAccount">Directory account to query.</param>
/// <param name="Source">Resolver source.</param>
public sealed record PamAccountResolution(string DirectoryAccount, string Source);
