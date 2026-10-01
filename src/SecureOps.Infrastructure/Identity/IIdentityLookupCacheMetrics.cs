namespace SecureOps.Infrastructure.Identity;

/// <summary>Aggregate-only cache metrics without account labels.</summary>
public interface IIdentityLookupCacheMetrics
{
    /// <summary>Records a cache hit.</summary>
    public void RecordHit();
    /// <summary>Records a cache miss.</summary>
    public void RecordMiss();
    /// <summary>Records a provider call.</summary>
    public void RecordProviderCall();
    /// <summary>Records a coalesced request.</summary>
    public void RecordCoalesced();
    /// <summary>Returns aggregate counters and current sizes.</summary>
    public IdentityLookupCacheMetricsSnapshot Snapshot(int entries, int inflight);
}

/// <summary>Aggregate-only identity cache metrics snapshot.</summary>
public sealed record IdentityLookupCacheMetricsSnapshot(long CacheHits, long CacheMisses, long ProviderCalls, long CoalescedRequests, int CachedEntries, int InflightRequests);
