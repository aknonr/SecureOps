using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Identity;

public sealed class MockIdentityDirectoryProviderTests
{
    [Fact]
    public async Task NameSearchCandidates_CanBeSelectedForExactLookup_WithTheSameSyntheticProvider()
    {
        DirectoryNameSearchResult names = await new MockDirectoryNameSearchProvider()
            .SearchAsync(DirectoryNameQuery.TryCreate("ayse", out _)!, 10, CancellationToken.None);
        MockIdentityDirectoryProvider exact = Create(enableUpnLookup: true);
        foreach (DirectoryNameCandidate candidate in names.Candidates)
        {
            DirectoryUserRecord? found = await exact.FindUserAsync(candidate.SamAccountName, CancellationToken.None);
            found.Should().NotBeNull();
            found!.DisplayName.Should().Be(candidate.DisplayName);
        }
    }

    [Theory]
    [InlineData("pam12356")]
    [InlineData("PAM12356")]
    public async Task FindUserAsync_WithExactSamAccountName_ReturnsSeededIdentity(string account)
    {
        MockIdentityDirectoryProvider provider = Create(enableUpnLookup: true);

        DirectoryUserRecord? result = await provider.FindUserAsync(account, CancellationToken.None);

        result!.SamAccountName.Should().Be("pam12356");
    }

    [Fact]
    public async Task FindUserAsync_WithExactUpnAndAdvertisedSupport_ReturnsSeededIdentity()
    {
        MockIdentityDirectoryProvider provider = Create(enableUpnLookup: true);

        DirectoryUserRecord? result = await provider.FindUserAsync("pam12356@contoso.local", CancellationToken.None);

        provider.SupportsUpnLookup.Should().BeTrue();
        result!.SamAccountName.Should().Be("pam12356");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindUserAsync_WithUnknownExactUpn_ReturnsNotFound(bool enableUpnLookup)
    {
        MockIdentityDirectoryProvider provider = Create(enableUpnLookup);

        DirectoryUserRecord? result = await provider.FindUserAsync("missing@contoso.local", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindUserAsync_WithUpnSupportDisabled_DoesNotMatchSeededUpn()
    {
        MockIdentityDirectoryProvider provider = Create(enableUpnLookup: false);

        DirectoryUserRecord? result = await provider.FindUserAsync("pam12356@contoso.local", CancellationToken.None);

        provider.SupportsUpnLookup.Should().BeFalse();
        result.Should().BeNull();
    }

    private static MockIdentityDirectoryProvider Create(bool enableUpnLookup) =>
        new(Options.Create(new IdentityLookupOptions { EnableUpnLookup = enableUpnLookup }));
}
