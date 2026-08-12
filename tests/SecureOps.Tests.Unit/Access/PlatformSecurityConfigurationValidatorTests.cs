using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Access;

namespace SecureOps.Tests.Unit.Access;

public sealed class PlatformSecurityConfigurationValidatorTests
{
    [Fact]
    public void Validate_WhenBootstrapAdministratorIsBlank_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Access:BootstrapAdministrators:0"] = " "
        });

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*BootstrapAdministrators*");
    }

    [Fact]
    public void Validate_WhenBootstrapAdministratorsAreDuplicated_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Access:BootstrapAdministrators:0"] = "CONTOSO\\bootstrap.admin",
            ["Access:BootstrapAdministrators:1"] = "contoso\\BOOTSTRAP.ADMIN"
        });

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*unique*");
    }

    [Fact]
    public void Validate_WhenBootstrapIsConfiguredWithoutAutoRequest_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Access:AutoCreateRequest"] = "false",
            ["Access:BootstrapAdministrators:0"] = "CONTOSO\\bootstrap.admin"
        });

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AutoCreateRequest*");
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) => new ConfigurationBuilder()
        .AddInMemoryCollection(values)
        .Build();
}
