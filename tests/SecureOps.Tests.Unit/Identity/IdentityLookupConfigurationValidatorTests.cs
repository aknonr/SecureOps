using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Identity;

namespace SecureOps.Tests.Unit.Identity;

public sealed class IdentityLookupConfigurationValidatorTests
{
    [Fact]
    public void Validate_ActiveDirectoryWithoutDomain_Throws()
    {
        IConfiguration configuration = Build("ActiveDirectory");

        Action act = () => IdentityLookupConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*IdentityLookup:DomainName*");
    }

    [Fact]
    public void Validate_ActiveDirectoryWithDomainAndMockPam_DoesNotThrow()
    {
        IConfiguration configuration = Build("ActiveDirectory", "contoso.local");

        Action act = () => IdentityLookupConfigurationValidator.Validate(configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_UnknownPamProvider_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityLookup:Provider"] = "Mock",
            ["PamProvider:Provider"] = "Unapproved"
        }).Build();

        Action act = () => IdentityLookupConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*PamProvider currently supports only Mock*");
    }

    private static IConfiguration Build(string provider, string? domain = null)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityLookup:Provider"] = provider,
            ["IdentityLookup:DomainName"] = domain,
            ["IdentityLookup:BulkMaxAccounts"] = "20",
            ["PamProvider:Provider"] = "Mock",
            ["PamProvider:TimeoutSeconds"] = "3"
        }).Build();
    }
}
