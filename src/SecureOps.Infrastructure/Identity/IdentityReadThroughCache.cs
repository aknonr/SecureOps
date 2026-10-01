using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>In-process bounded TTL cache with one provider task per normalized exact account.</summary>
public sealed class IdentityReadThroughCache : IIdentityReadThroughCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<DirectoryUserRecord?>>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _trimGate = new();
    private readonly IdentityLookupCacheOptions _options;
    private readonly IIdentityLookupCacheMetrics _metrics;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the cache.</summary>
    public IdentityReadThroughCache(IOptions<IdentityLookupOptions> options, IIdentityLookupCacheMetrics metrics, TimeProvider timeProvider)
    {
        _options = options.Value.Cache;
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IdentityReadCacheResult> GetOrCreateAsync(string normalizedAccount, Func<CancellationToken, Task<DirectoryUserRecord?>> provider, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _metrics.RecordMiss();
            _metrics.RecordProviderCall();
            return new IdentityReadCacheResult(await provider(cancellationToken), IdentityReadCacheDisposition.ProviderCall);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(normalizedAccount, out CacheEntry? cached))
        {
            if (cached.ExpiresAt > now)
            {
                _metrics.RecordHit();
                return new IdentityReadCacheResult(cached.User, IdentityReadCacheDisposition.CacheHit);
            }

            _entries.TryRemove(new KeyValuePair<string, CacheEntry>(normalizedAccount, cached));
        }

        Lazy<Task<DirectoryUserRecord?>> candidate = new(
            () => LoadAsync(normalizedAccount, provider),
            LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<DirectoryUserRecord?>> selected = _inflight.GetOrAdd(normalizedAccount, candidate);
        bool owner = ReferenceEquals(candidate, selected);
        if (!owner)
        {
            _metrics.RecordCoalesced();
        }

        try
        {
            DirectoryUserRecord? user = await selected.Value.WaitAsync(cancellationToken);
            return new IdentityReadCacheResult(user, owner ? IdentityReadCacheDisposition.ProviderCall : IdentityReadCacheDisposition.Coalesced);
        }
        finally
        {
            if (selected.IsValueCreated && selected.Value.IsCompleted)
            {
                _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<DirectoryUserRecord?>>>(normalizedAccount, selected));
            }
        }
    }

    /// <summary>Returns aggregate cache diagnostics.</summary>
    public IdentityLookupCacheMetricsSnapshot GetSnapshot() => _metrics.Snapshot(_entries.Count, _inflight.Count);

    private async Task<DirectoryUserRecord?> LoadAsync(string normalizedAccount, Func<CancellationToken, Task<DirectoryUserRecord?>> provider)
    {
        _metrics.RecordMiss();
        _metrics.RecordProviderCall();
        DirectoryUserRecord? user = await provider(CancellationToken.None);
        StoreBounded(normalizedAccount, user);
        return user;
    }

    private void StoreBounded(string normalizedAccount, DirectoryUserRecord? user)
    {
        lock (_trimGate)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (KeyValuePair<string, CacheEntry> expired in _entries.Where(pair => pair.Value.ExpiresAt <= now))
            {
                _entries.TryRemove(expired);
            }

            while (_entries.Count >= _options.MaxEntries)
            {
                KeyValuePair<string, CacheEntry> oldest = _entries.OrderBy(pair => pair.Value.ExpiresAt).First();
                _entries.TryRemove(oldest);
            }

            _entries[normalizedAccount] = new CacheEntry(user, now.AddSeconds(_options.TtlSeconds));
        }
    }

    private sealed record CacheEntry(DirectoryUserRecord? User, DateTimeOffset ExpiresAt);
}
