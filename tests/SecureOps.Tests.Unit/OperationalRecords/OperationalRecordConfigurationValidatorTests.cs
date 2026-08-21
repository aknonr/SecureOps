using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class OperationalRecordConfigurationValidatorTests
{
    [Fact]
    public void Validate_WithUnsupportedLiveProvider_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = "Http"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*SourceProvider*not implemented*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Demo")]
    [InlineData("Test")]
    public void Validate_WithFakeSourceInSyntheticEnvironment_Succeeds(string environmentName)
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = "Fake"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, environmentName);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithFakeSourceInProduction_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = "Fake"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Fake*Development, Demo, or Test*");
    }

    [Fact]
    public void Validate_WithDisabledSourceInProduction_Succeeds()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = "Disabled"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithSqlRepositoryWithoutConnectionString_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:RepositoryProvider"] = "SqlServer"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:SecureOpsDb*");
    }

    [Fact]
    public void Validate_WithUnapprovedRequesterPolicy_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Jira:UnresolvedRequesterPolicy"] = "Guess"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Block or ProceedUnassigned*");
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
