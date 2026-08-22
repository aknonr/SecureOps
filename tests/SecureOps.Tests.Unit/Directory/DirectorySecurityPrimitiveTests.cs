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
    public void NormalizeGroup_RejectsWildcardAndRawLdapGrammar(string input)
    {
        DirectoryExactInputNormalizer normalizer = new(Options.Create(new DirectoryExplorerOptions()));

        normalizer.NormalizeGroup(input).IsValid.Should().BeFalse();
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
            calls++; throw new DirectoryProviderUnavailableException();
        }, CancellationToken.None);
        await failing.Should().ThrowAsync<DirectoryProviderUnavailableException>();
        int first = await cache.GetOrCreateAsync("key", false, _ => Task.FromResult(++calls), CancellationToken.None);
        int cached = await cache.GetOrCreateAsync("key", false, _ => Task.FromResult(++calls), CancellationToken.None);
        int refreshed = await cache.GetOrCreateAsync("key", true, _ => Task.FromResult(++calls), CancellationToken.None);

        first.Should().Be(2); cached.Should().Be(2); refreshed.Should().Be(3);
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
            Interlocked.Increment(ref calls); return providerResult.Task;
        }

        Task<int> first = cache.GetOrCreateAsync("same", false, Factory, CancellationToken.None);
        Task<int> second = cache.GetOrCreateAsync("same", false, Factory, CancellationToken.None);
        providerResult.SetResult(42);

        (await Task.WhenAll(first, second)).Should().Equal(42, 42);
        calls.Should().Be(1);
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

}
