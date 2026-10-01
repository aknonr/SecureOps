using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.DirectoryExplorer;

public sealed class DirectorySecurityPrimitiveTests
{
    [Theory]
    [InlineData("*")]
    [InlineData("(objectClass=*)")]
    [InlineData("CN=Admins,DC=example")]
    [InlineData("group\\name")]
    [InlineData("member:1.2.840.113556.1.4.1941:=target")]
    [InlineData("group\0name")]
    public void NormalizeGroup_RejectsWildcardAndRawLdapGrammar(string input)
    {
        DirectoryExactInputNormalizer normalizer = new(Options.Create(new DirectoryExplorerOptions()));

        normalizer.NormalizeGroup(input).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NormalizeGroup_RejectsOversizedExactIdentifier()
    {
        DirectoryExactInputNormalizer normalizer = new(Options.Create(new DirectoryExplorerOptions()));

        normalizer.NormalizeGroup(new string('a', 257)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NormalizeGroup_AllowsExactNameAndNormalizesCase()
    {
        DirectoryExactInputNormalizer normalizer = new(Options.Create(new DirectoryExplorerOptions()));

        DirectoryInputNormalizationResult result = normalizer.NormalizeGroup(" Operations Readers ");

        result.Value.Should().Be("operations readers");
    }

    [Fact]
    public async Task QueryCache_DoesNotCacheExceptionsAndRefreshBypassesValue()
    {
        DirectoryExplorerOptions options = new() { Cache = new DirectoryExplorerCacheOptions { Enabled = true, TtlSeconds = 30, MaxEntries = 10 } };
        DirectoryQueryCache cache = new(Options.Create(options), TimeProvider.System);
        int calls = 0;

        Func<Task> failing = () => cache.GetOrCreateAsync<int>("key", false, _ =>
        {
            calls++;
            throw new DirectoryProviderUnavailableException();
        }, CancellationToken.None);
        await failing.Should().ThrowAsync<DirectoryProviderUnavailableException>();
        int first = await cache.GetOrCreateAsync("key", false, _ => Task.FromResult(++calls), CancellationToken.None);
        int cached = await cache.GetOrCreateAsync("key", false, _ => Task.FromResult(++calls), CancellationToken.None);
        int refreshed = await cache.GetOrCreateAsync("key", true, _ => Task.FromResult(++calls), CancellationToken.None);

        first.Should().Be(2);
        cached.Should().Be(2);
        refreshed.Should().Be(3);
    }

    [Fact]
    public async Task QueryCache_CoalescesConcurrentProviderCalls()
    {
        DirectoryExplorerOptions options = new() { Cache = new DirectoryExplorerCacheOptions { Enabled = true, TtlSeconds = 30, MaxEntries = 10 } };
        DirectoryQueryCache cache = new(Options.Create(options), TimeProvider.System);
        TaskCompletionSource<int> providerResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return providerResult.Task;
        }

        Task<int>[] requests = Enumerable.Range(0, 8)
            .Select(_ => cache.GetOrCreateAsync("same", false, Factory, CancellationToken.None))
            .ToArray();
        providerResult.SetResult(42);

        (await Task.WhenAll(requests)).Should().OnlyContain(value => value == 42);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task QueryCache_ExpiresAndPartitionsTargetsAndOperations()
    {
        ManualTimeProvider time = new();
        DirectoryExplorerOptions options = new()
        {
            Cache = new DirectoryExplorerCacheOptions { Enabled = true, TtlSeconds = 5, MaxEntries = 10 }
        };
        DirectoryQueryCache cache = new(Options.Create(options), time);
        int calls = 0;
        Task<int> Factory(CancellationToken _) => Task.FromResult(Interlocked.Increment(ref calls));

        int first = await cache.GetOrCreateAsync("Mock|group-metadata|alpha", false, Factory, CancellationToken.None);
        int cached = await cache.GetOrCreateAsync("Mock|group-metadata|alpha", false, Factory, CancellationToken.None);
        int otherTarget = await cache.GetOrCreateAsync("Mock|group-metadata|beta", false, Factory, CancellationToken.None);
        int enrichment = await cache.GetOrCreateAsync("Mock|phase2-principal|alpha|0", false, Factory, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(6));
        int expired = await cache.GetOrCreateAsync("Mock|group-metadata|alpha", false, Factory, CancellationToken.None);

        cached.Should().Be(first);
        otherTarget.Should().NotBe(first);
        enrichment.Should().NotBe(first);
        expired.Should().NotBe(first);
        calls.Should().Be(4);
    }

    [Fact]
    public void DirectoryRateLimitDefaults_RemainConservativeAndIndependent()
    {
        RateLimitingOptions options = new();

        (options.DirectoryGroupQuery.PermitLimit, options.DirectoryGroupQuery.WindowSeconds).Should().Be((20, 60));
        (options.DirectoryGroupMembers.PermitLimit, options.DirectoryGroupMembers.WindowSeconds).Should().Be((10, 60));
        (options.DirectoryEnrichment.PermitLimit, options.DirectoryEnrichment.WindowSeconds).Should().Be((6, 60));
        (options.DirectoryPrivilegedGroups.PermitLimit, options.DirectoryPrivilegedGroups.WindowSeconds).Should().Be((4, 60));
    }

    [Fact]
    public void ConfigurationValidator_RejectsUnsafeBounds()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DirectoryExplorer:DefaultPageSize"] = "101",
            ["DirectoryExplorer:MaxPageSize"] = "100"
        }).Build();

        Action act = () => DirectoryExplorerConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*page sizes*");
    }

    [Fact]
    public void ConfigurationValidator_RejectsUnsafeTraversalBounds()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DirectoryExplorer:MaxTraversalDepth"] = "9",
            ["DirectoryExplorer:MaxTraversalNodes"] = "8"
        }).Build();

        Action act = () => DirectoryExplorerConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*traversal and enrichment*");
    }

    [Theory]
    [InlineData("admin*")]
    [InlineData("CN=Admins,DC=example")]
    public void ConfigurationValidator_RejectsUnsafePrivilegedGroupIdentifier(string identifier)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DirectoryExplorer:PrivilegedGroupIdentifiers:0"] = identifier
        }).Build();

        Action act = () => DirectoryExplorerConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*privileged-group identifiers*");
    }

    [Fact]
    public void ConfigurationValidator_AcceptsExactSidAndRejectsNormalizedDuplicates()
    {
        IConfiguration valid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DirectoryExplorer:PrivilegedGroupIdentifiers:0"] = "S-1-5-21-1001"
        }).Build();
        IConfiguration duplicate = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DirectoryExplorer:PrivilegedGroupIdentifiers:0"] = "Ops-Admins",
            ["DirectoryExplorer:PrivilegedGroupIdentifiers:1"] = "ops-admins"
        }).Build();

        Action validAct = () => DirectoryExplorerConfigurationValidator.Validate(valid);
        Action duplicateAct = () => DirectoryExplorerConfigurationValidator.Validate(duplicate);

        validAct.Should().NotThrow();
        duplicateAct.Should().Throw<InvalidOperationException>().WithMessage("*privileged-group identifiers*");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 8, 24, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
