using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Short-lived bounded cache for read-only directory query pages.</summary>
public sealed class DirectoryQueryCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CacheGate> _locks = new(StringComparer.Ordinal);
    private readonly DirectoryExplorerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the cache.</summary>
    public DirectoryQueryCache(IOptions<DirectoryExplorerOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>Returns a cached result or executes one coalesced provider call.</summary>
    public async Task<T> GetOrCreateAsync<T>(string key, bool refresh, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        if (!_options.Cache.Enabled)
        {
            return await factory(cancellationToken);
        }

        if (refresh)
        {
            _entries.TryRemove(key, out _);
        }
        else if (TryGet(key, out T? cachedValue))
        {
            return cachedValue!;
        }

        CacheGate gate = AcquireGate(key);
        bool entered = false;
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            entered = true;
            if (!refresh && TryGet(key, out T? cached))
            {
                return cached!;
            }

            T value = await factory(cancellationToken);
            EnsureCapacity();
            _entries[key] = new CacheEntry(new CacheBox<T>(value), _timeProvider.GetUtcNow().AddSeconds(_options.Cache.TtlSeconds));
            return value;
        }
        finally
        {
            if (entered)
            {
                gate.Semaphore.Release();
            }
            if (Interlocked.Decrement(ref gate.Users) == 0)
            {
                _locks.TryRemove(new KeyValuePair<string, CacheGate>(key, gate));
            }
        }
    }

    private CacheGate AcquireGate(string key)
    {
        while (true)
        {
            CacheGate gate = _locks.GetOrAdd(key, _ => new CacheGate());
            Interlocked.Increment(ref gate.Users);
            if (_locks.TryGetValue(key, out CacheGate? registered) && ReferenceEquals(gate, registered))
            {
                return gate;
            }

            Interlocked.Decrement(ref gate.Users);
        }
    }

    private bool TryGet<T>(string key, out T? value)
    {
        if (_entries.TryGetValue(key, out CacheEntry? entry) && entry.ExpiresAt > _timeProvider.GetUtcNow() && entry.Value is CacheBox<T> box)
        {
            value = box.Value;
            return true;
        }

        _entries.TryRemove(key, out _);
        value = default;
        return false;
    }

    private void EnsureCapacity()
    {
        foreach ((string key, CacheEntry entry) in _entries)
        {
            if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                _entries.TryRemove(key, out _);
            }
        }

        if (_entries.Count >= _options.Cache.MaxEntries)
        {
            _entries.Clear();
        }
    }

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAt);
    private sealed record CacheBox<T>(T Value);
    private sealed class CacheGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users;
    }
}
