using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Access;

public sealed class AccessIdentityProfileResolverTests
{
    [Fact]
    public async Task ExactDomainPrefixedAccount_ReturnsProviderBackedSafeProfile()
    {
        IOptions<IdentityLookupOptions> options = Options.Create(new IdentityLookupOptions());
        AccessIdentityProfileResolver resolver = new(
            new IdentityAccountNormalizer(options),
            new MockIdentityDirectoryProvider(options),
            NullLogger<AccessIdentityProfileResolver>.Instance);

        AccessIdentityProfile? result = await resolver.ResolveAsync("CONTOSO\\pam12356", CancellationToken.None);

        result.Should().Be(new AccessIdentityProfile(
            "Example Admin",
            "pam12356",
            "example.admin@contoso.local",
            "Windows Operations",
            "Systems Engineer"));
    }

    [Theory]
    [InlineData("CONTOSO\\unknown.user")]
    [InlineData("unsafe*")]
    [InlineData("demo:platform-admin")]
    public async Task MissingOrUnsafeIdentity_ReturnsNullWithoutFabricatingProfile(string identity)
    {
        IOptions<IdentityLookupOptions> options = Options.Create(new IdentityLookupOptions());
        AccessIdentityProfileResolver resolver = new(
            new IdentityAccountNormalizer(options),
            new MockIdentityDirectoryProvider(options),
            NullLogger<AccessIdentityProfileResolver>.Instance);

        (await resolver.ResolveAsync(identity, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ProviderFailure_ReturnsNullWithoutLeakingOrFabricatingProfile()
    {
        IOptions<IdentityLookupOptions> options = Options.Create(new IdentityLookupOptions());
        AccessIdentityProfileResolver resolver = new(
            new IdentityAccountNormalizer(options),
            new FailingDirectoryProvider(),
            NullLogger<AccessIdentityProfileResolver>.Instance);

        (await resolver.ResolveAsync("CONTOSO\\exact.user", CancellationToken.None)).Should().BeNull();
    }

    private sealed class FailingDirectoryProvider : IIdentityDirectoryProvider
    {
        public bool SupportsUpnLookup => false;

        public Task<DirectoryUserRecord?> FindUserAsync(string normalizedAccount, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Deterministic provider failure.");
    }
}
