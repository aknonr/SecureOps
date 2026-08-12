namespace SecureOps.Infrastructure.Identity;

/// <summary>Bounded exact-account read-through cache with single-flight provider calls.</summary>
public interface IIdentityReadThroughCache
{
    /// <summary>Gets a short-lived result or coalesces one provider call.</summary>
    public Task<IdentityReadCacheResult> GetOrCreateAsync(string normalizedAccount, Func<CancellationToken, Task<DirectoryUserRecord?>> provider, CancellationToken cancellationToken);
}

/// <summary>How an identity result was obtained.</summary>
public enum IdentityReadCacheDisposition
{
    /// <summary>A valid short-lived cache entry was used.</summary>
    CacheHit,
    /// <summary>This request performed the provider call.</summary>
    ProviderCall,
    /// <summary>This request joined an already-running provider call.</summary>
    Coalesced
}

/// <summary>Result from the identity read cache.</summary>
public sealed record IdentityReadCacheResult(DirectoryUserRecord? User, IdentityReadCacheDisposition Disposition);
