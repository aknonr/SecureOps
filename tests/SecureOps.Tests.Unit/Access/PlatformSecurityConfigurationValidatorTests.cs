using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Access;

public sealed class PlatformSecurityConfigurationValidatorTests
{
    [Fact]
    public void BootstrapAdmin_DefaultsDisabled()
    {
        BootstrapAdminOptions options = new();

        options.Enabled.Should().BeFalse();
        options.LoginName.Should().BeEmpty();
        options.AllowedIssuer.Should().BeEmpty();
    }

    [Fact]
    public void Validate_LegacyBootstrapAdministratorsAreRejected()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Access:BootstrapAdministrators:0"] = "example.bootstrap"
        });

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*retired*");
    }

    [Theory]
    [InlineData(null, "https://identity.example.test", "*LoginName*")]
    [InlineData("bootstrap.operator", null, "*AllowedIssuer*")]
    [InlineData("bootstrap.operator", "http://identity.example.test", "*AllowedIssuer*")]
    [InlineData("bootstrap.operator", "https://*.example.test", "*AllowedIssuer*")]
    public void Validate_EnabledBootstrapRequiresBoundedLoginAndExactHttpsIssuer(
        string? loginName,
        string? allowedIssuer,
        string expectedMessage)
    {
        Dictionary<string, string?> values = ValidBootstrapConfiguration();
        values["BootstrapAdmin:LoginName"] = loginName;
        values["BootstrapAdmin:AllowedIssuer"] = allowedIssuer;

        Action act = () => PlatformSecurityConfigurationValidator.Validate(Configuration(values));

        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData("Access:RepositoryProvider", "InMemory", "*RepositoryProvider=SqlServer*")]
    [InlineData("Access:AutoCreateRequest", "false", "*AutoCreateRequest=true*")]
    [InlineData("Access:DemoCompatibilityEnabled", "true", "*DemoCompatibilityEnabled*")]
    [InlineData("Audit:Provider", "File", "*Audit:Provider=SqlServer*")]
    [InlineData("Audit:FailClosed", "false", "*Audit:FailClosed=true*")]
    [InlineData("Oidc:Enabled", "false", "*requires enabled OIDC*")]
    [InlineData("Oidc:Authority", "https://different.example.test", "*exactly matches*")]
    public void Validate_EnabledBootstrapRejectsUnsafeProviderCombination(
        string key,
        string value,
        string expectedMessage)
    {
        Dictionary<string, string?> values = ValidBootstrapConfiguration();
        values[key] = value;

        Action act = () => PlatformSecurityConfigurationValidator.Validate(Configuration(values));

        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
    }

    [Fact]
    public void Validate_EnabledBootstrapAcceptsSqlOidcAndFailClosedAudit()
    {
        IConfiguration configuration = Configuration(ValidBootstrapConfiguration());

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WhenSessionRepositoryDoesNotMatchAccess_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Access:RepositoryProvider"] = "InMemory",
            ["SessionSecurity:RepositoryProvider"] = "SqlServer",
            ["ConnectionStrings:SecureOpsDb"] = "Server=(local);Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
        });

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*providers must match*");
    }

    [Theory]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void Validate_ControlledEnvironmentRejectsInMemorySessionAuthority(string environment)
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>());

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*RepositoryProvider SqlServer*");
    }

    [Fact]
    public void Validate_RejectsUnsafeActivityPersistenceInterval()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["SessionSecurity:IdleTimeoutMinutes"] = "5",
            ["SessionSecurity:ActivityPersistenceIntervalMinutes"] = "5"
        });

        Action act = () => PlatformSecurityConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*activity-persistence*");
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) => new ConfigurationBuilder()
        .AddInMemoryCollection(values)
        .Build();

    private static Dictionary<string, string?> ValidBootstrapConfiguration() => new()
    {
        ["BootstrapAdmin:Enabled"] = "true",
        ["BootstrapAdmin:LoginName"] = "bootstrap.operator",
        ["BootstrapAdmin:AllowedIssuer"] = "https://identity.example.test",
        ["Oidc:Enabled"] = "true",
        ["Oidc:Authority"] = "https://identity.example.test",
        ["Access:RepositoryProvider"] = "SqlServer",
        ["Access:AutoCreateRequest"] = "true",
        ["Access:DemoCompatibilityEnabled"] = "false",
        ["SessionSecurity:RepositoryProvider"] = "SqlServer",
        ["Audit:Provider"] = "SqlServer",
        ["Audit:FailClosed"] = "true",
        ["ConnectionStrings:SecureOpsDb"] = "Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout=15"
    };
}
