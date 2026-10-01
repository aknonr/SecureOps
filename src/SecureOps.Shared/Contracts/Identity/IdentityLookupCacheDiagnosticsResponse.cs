namespace SecureOps.Shared.Contracts.Identity;

/// <summary>Aggregate-only identity lookup cache diagnostics.</summary>
public sealed record IdentityLookupCacheDiagnosticsResponse(
    long CacheHits,
    long CacheMisses,
    long ProviderCalls,
    long CoalescedRequests,
    int CachedEntries,
    int InflightRequests,
    bool Enabled,
    int TtlSeconds);
