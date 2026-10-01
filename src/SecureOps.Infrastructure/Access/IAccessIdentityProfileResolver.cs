namespace SecureOps.Infrastructure.Access;

/// <summary>Resolves optional safe identity enrichment through the configured exact-match provider.</summary>
public interface IAccessIdentityProfileResolver
{
    /// <summary>Returns a provider-backed profile or null when the principal cannot be safely resolved.</summary>
    public Task<AccessIdentityProfile?> ResolveAsync(string corporateIdentity, CancellationToken cancellationToken);
}
