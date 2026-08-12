namespace SecureOps.Infrastructure.Identity;

/// <summary>Thread-safe aggregate identity cache metrics.</summary>
public sealed class IdentityLookupCacheMetrics : IIdentityLookupCacheMetrics
{
    private long _hits;
    private long _misses;
    private long _providerCalls;
    private long _coalesced;

    /// <inheritdoc />
    public void RecordHit() => Interlocked.Increment(ref _hits);
    /// <inheritdoc />
    public void RecordMiss() => Interlocked.Increment(ref _misses);
    /// <inheritdoc />
    public void RecordProviderCall() => Interlocked.Increment(ref _providerCalls);
    /// <inheritdoc />
    public void RecordCoalesced() => Interlocked.Increment(ref _coalesced);

    /// <inheritdoc />
    public IdentityLookupCacheMetricsSnapshot Snapshot(int entries, int inflight) => new(
        Interlocked.Read(ref _hits),
        Interlocked.Read(ref _misses),
        Interlocked.Read(ref _providerCalls),
        Interlocked.Read(ref _coalesced),
        entries,
        inflight);
}
