using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Identity;

public sealed class IdentityReadThroughCacheTests
{
    [Fact]
    public async Task RepeatedAccount_UsesShortLivedCachedResult()
    {
        ManualTimeProvider time = new();
        IdentityReadThroughCache cache = CreateCache(time);
        int calls = 0;

        DirectoryUserRecord? first = (await cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None)).User;
        IdentityReadCacheResult second = await cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None);

        first.Should().NotBeNull();
        second.Disposition.Should().Be(IdentityReadCacheDisposition.CacheHit);
        calls.Should().Be(1);

        Task<DirectoryUserRecord?> Provider(CancellationToken _)
        {
            calls++;
            return Task.FromResult<DirectoryUserRecord?>(User("sample.user"));
        }
    }

    [Fact]
    public async Task ExpiredEntry_CallsProviderAgain()
    {
        ManualTimeProvider time = new();
        IdentityReadThroughCache cache = CreateCache(time, ttlSeconds: 5);
        int calls = 0;
        Task<DirectoryUserRecord?> Provider(CancellationToken _) => Task.FromResult<DirectoryUserRecord?>(User($"sample-{++calls}"));

        _ = await cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(6));
        _ = await cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentSameAccount_CoalescesOneProviderCall()
    {
        IdentityReadThroughCache cache = CreateCache(new ManualTimeProvider());
        TaskCompletionSource<DirectoryUserRecord?> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<DirectoryUserRecord?> Provider(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        }

        Task<IdentityReadCacheResult> first = cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None);
        Task<IdentityReadCacheResult> second = cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None);
        release.SetResult(User("sample.user"));
        IdentityReadCacheResult[] results = await Task.WhenAll(first, second);

        calls.Should().Be(1);
        results.Should().Contain(result => result.Disposition == IdentityReadCacheDisposition.ProviderCall);
        results.Should().Contain(result => result.Disposition == IdentityReadCacheDisposition.Coalesced);
    }

    [Fact]
    public async Task DifferentAccounts_RemainIndependent()
    {
        IdentityReadThroughCache cache = CreateCache(new ManualTimeProvider());
        int calls = 0;
        Task<DirectoryUserRecord?> Provider(CancellationToken _) => Task.FromResult<DirectoryUserRecord?>(User($"sample-{Interlocked.Increment(ref calls)}"));

        await Task.WhenAll(
            cache.GetOrCreateAsync("sample.one", Provider, CancellationToken.None),
            cache.GetOrCreateAsync("sample.two", Provider, CancellationToken.None));

        calls.Should().Be(2);
    }

    [Fact]
    public async Task ProviderError_DoesNotPoisonCache()
    {
        IdentityReadThroughCache cache = CreateCache(new ManualTimeProvider());
        int calls = 0;
        Task<DirectoryUserRecord?> Provider(CancellationToken _) => ++calls == 1
            ? Task.FromException<DirectoryUserRecord?>(new InvalidOperationException("synthetic provider failure"))
            : Task.FromResult<DirectoryUserRecord?>(User("sample.user"));

        Func<Task> first = async () => _ = await cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None);
        await first.Should().ThrowAsync<InvalidOperationException>();
        IdentityReadCacheResult second = await cache.GetOrCreateAsync("sample.user", Provider, CancellationToken.None);

        second.User.Should().NotBeNull();
        calls.Should().Be(2);
    }

    private static IdentityReadThroughCache CreateCache(TimeProvider timeProvider, int ttlSeconds = 30) => new(
        Options.Create(new IdentityLookupOptions { Cache = new IdentityLookupCacheOptions { Enabled = true, TtlSeconds = ttlSeconds, MaxEntries = 20 } }),
        new IdentityLookupCacheMetrics(),
        timeProvider);

    private static DirectoryUserRecord User(string account) => new(
        "Synthetic User", account, null, null, null, null, null, true, false, "Mock");

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
