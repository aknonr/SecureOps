using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Identity;

public sealed class ActiveDirectoryIdentityDirectoryProviderTests
{
    [Theory]
    [InlineData("test.user")]
    [InlineData("pam000001")]
    public async Task FindUserAsync_UsesExactSamAccountName(string account)
    {
        FakeClient client = new() { SamResult = Record(account) };
        ActiveDirectoryIdentityDirectoryProvider provider = Create(client);
        DirectoryUserRecord? result = await provider.FindUserAsync(account, CancellationToken.None);
        result!.SamAccountName.Should().Be(account);
        client.SamInputs.Should().ContainSingle().Which.Should().Be(account);
    }

    [Fact]
    public async Task FindUserAsync_UsesUpnOnlyAfterSamNotFound()
    {
        FakeClient client = new() { UpnResult = Record("test.user") };
        DirectoryUserRecord? result = await Create(client, upn: true).FindUserAsync("test.user@example.invalid", CancellationToken.None);
        result.Should().NotBeNull();
        client.UpnInputs.Should().ContainSingle().Which.Should().Be("test.user@example.invalid");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SupportsUpnLookup_ReflectsEffectiveConfiguredBehavior(bool enabled)
    {
        ActiveDirectoryIdentityDirectoryProvider provider = Create(new FakeClient(), upn: enabled);

        provider.SupportsUpnLookup.Should().Be(enabled);
    }

    [Fact]
    public async Task FindUserAsync_WithUpnDisabled_DoesNotCallUpnTransport()
    {
        FakeClient client = new() { UpnResult = Record("test.user") };

        DirectoryUserRecord? result = await Create(client, upn: false).FindUserAsync("test.user@example.invalid", CancellationToken.None);

        result.Should().BeNull();
        client.UpnInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task FindUserAsync_RejectsUnsafeInputWithoutClientCall()
    {
        FakeClient client = new();
        Func<Task> action = () => Create(client).FindUserAsync("pam*", CancellationToken.None);
        await action.Should().ThrowAsync<IdentityProviderInputRejectedException>();
        client.SamInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task FindUserAsync_PropagatesTimeoutAndCancellation()
    {
        FakeClient client = new() { SamTask = Task.Delay(3000, TestContext.Current.CancellationToken).ContinueWith<DirectoryUserRecord?>(_ => null) };
        await Assert.ThrowsAsync<TimeoutException>(() => Create(client, timeout: 1).FindUserAsync("test.user", CancellationToken.None));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new FakeClient()).FindUserAsync("test.user", cancelled.Token));
    }

    private static ActiveDirectoryIdentityDirectoryProvider Create(FakeClient client, bool upn = false, int timeout = 1) => new(Options.Create(new IdentityLookupOptions { DomainName = "example.invalid", EnableUpnLookup = upn, ProviderTimeoutSeconds = timeout }), client);
    private static DirectoryUserRecord Record(string account) => new("Test User", account, null, null, null, null, null, true, false, "ActiveDirectory");
    private sealed class FakeClient : IActiveDirectoryLookupClient
    {
        public List<string> SamInputs { get; } = []; public List<string> UpnInputs { get; } = [];
        public DirectoryUserRecord? SamResult { get; init; }
        public DirectoryUserRecord? UpnResult { get; init; }
        public Task<DirectoryUserRecord?>? SamTask { get; init; }
        public Task<DirectoryUserRecord?> FindBySamAccountNameAsync(string account, CancellationToken cancellationToken) { SamInputs.Add(account); return SamTask ?? Task.FromResult(SamResult); }
        public Task<DirectoryUserRecord?> FindByUserPrincipalNameAsync(string account, CancellationToken cancellationToken) { UpnInputs.Add(account); return Task.FromResult(UpnResult); }
    }
}
