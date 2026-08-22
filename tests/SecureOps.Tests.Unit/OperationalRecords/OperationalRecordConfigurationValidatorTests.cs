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
    public void Validate_WithDisabledJiraInProduction_Succeeds()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Jira:Provider"] = "Disabled"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithFakeJiraInProduction_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Jira:Provider"] = "Fake"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jira:Provider Fake*Development, Demo, or Test*");
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

    [Fact]
    public void Validate_WithCompleteEnterpriseProviderConfiguration_Succeeds()
    {
        IConfiguration configuration = Configuration(EnterpriseValues());

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Test");

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithTuruncuHatMissingRuntimeSecret_FailsClearly()
    {
        Dictionary<string, string?> values = EnterpriseValues();
        values["TuruncuHat:Password"] = string.Empty;

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*TuruncuHat:Password*");
    }

    [Fact]
    public void Validate_WithNonHttpsCorporateEndpoint_FailsClearly()
    {
        Dictionary<string, string?> values = EnterpriseValues();
        values["Jira:BaseUrl"] = "http://jira.invalid/";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jira:BaseUrl*HTTPS*");
    }

    private static Dictionary<string, string?> EnterpriseValues() => new()
    {
        ["OperationalRecords:SourceProvider"] = "TuruncuHat",
        ["Jira:Provider"] = "Corporate",
        ["TuruncuHat:BaseUrl"] = "https://source.invalid/",
        ["TuruncuHat:Authorization"] = "Sanitized runtime value",
        ["TuruncuHat:Username"] = "sanitized-user",
        ["TuruncuHat:Password"] = "sanitized-secret",
        ["TuruncuHat:TenantId"] = "218",
        ["TuruncuHat:SourceBaseObject"] = "SMSS_oRFF",
        ["TuruncuHat:RelatedGroupId"] = "68",
        ["TuruncuHat:ExcludedDccIds:0"] = "4241",
        ["TuruncuHat:ActivityBaseObject"] = "BPM_Actvty",
        ["TuruncuHat:ActivityTaskModelId"] = "10",
        ["TuruncuHat:ActivityGroupId"] = "20",
        ["TuruncuHat:ActivityMainObjectTypeId"] = "30",
        ["TuruncuHat:CompletedStatusId"] = "40",
        ["TuruncuHat:CompletionCommentTemplate"] = "Transferred to {JiraKey}",
        ["TuruncuHat:SessionLifetimeSeconds"] = "60",
        ["Jira:BaseUrl"] = "https://jira.invalid/",
        ["Jira:Authorization"] = "Sanitized runtime value",
        ["Jira:ProjectKey"] = "SAFE",
        ["Jira:IssueTypeId"] = "10001",
        ["Jira:TeamCustomField"] = "customfield_team",
        ["Jira:TeamValue"] = "Safe Team",
        ["Jira:RequesterWatcherCustomField"] = "customfield_requester",
        ["Jira:Labels:0"] = "safe-label"
    };

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
