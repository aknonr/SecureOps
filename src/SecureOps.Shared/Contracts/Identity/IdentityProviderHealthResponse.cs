namespace SecureOps.Shared.Contracts.Identity;

/// <summary>
/// Safe health metadata for the configured identity provider.
/// </summary>
/// <param name="Status">Provider configuration status.</param>
/// <param name="Provider">Configured identity provider name.</param>
/// <param name="RealDirectoryProviderEnabled">Whether a real directory provider is configured.</param>
public sealed record IdentityProviderHealthResponse(
    string Status,
    string Provider,
    bool RealDirectoryProviderEnabled);
