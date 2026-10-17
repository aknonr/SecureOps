using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class CurrentAccessProviderTests
{
    [Fact]
    public async Task ConcurrentInitialReads_PublishOneChange_NotOnePerWaiter()
    {
        IAccessApiClient api = Substitute.For<IAccessApiClient>();
        var response = new TaskCompletionSource<CurrentAccessResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(response.Task);
        using var provider = new CurrentAccessProvider(api, NullLogger<CurrentAccessProvider>.Instance);
        int changes = 0;
        provider.Changed += () => Interlocked.Increment(ref changes);

        Task<AccessSnapshot> first = provider.GetAsync(TestContext.Current.CancellationToken);
        Task<AccessSnapshot> second = provider.GetAsync(TestContext.Current.CancellationToken);
        response.SetResult(new(Guid.NewGuid(), "Approved", [], [], null, "synthetic", null!));
        AccessSnapshot[] snapshots = await Task.WhenAll(first, second);

        snapshots[0].Should().BeSameAs(snapshots[1]);
        changes.Should().Be(1, "a cached waiter must not invalidate an in-flight preview");
        await api.Received(1).GetCurrentAsync(Arg.Any<CancellationToken>());
        await provider.GetAsync(TestContext.Current.CancellationToken);
        changes.Should().Be(1);
        await provider.RefreshAsync(TestContext.Current.CancellationToken);
        changes.Should().Be(2, "an explicit refresh must still notify access consumers");
    }
}
