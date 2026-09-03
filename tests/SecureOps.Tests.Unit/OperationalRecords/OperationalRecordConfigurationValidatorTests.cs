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

    [Theory]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void Validate_WithSimulationProvidersOutsideSyntheticEnvironment_FailsStartup(string environmentName)
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = "Simulation",
            ["Jira:Provider"] = "Simulation"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, environmentName);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Simulation*Development, Demo, or Test*");
    }

    [Fact]
    public void Validate_WithOnlyOneSimulationProvider_FailsStartup()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = "Simulation",
            ["Jira:Provider"] = "Disabled"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*both*Simulation*");
    }

    [Fact]
    public void Validate_WithCorporateProvidersInTestWithoutReadOnlyMode_FailsClosed()
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values["OperationalRecords:ReadOnlyIntegrationMode"] = "false";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ReadOnlyIntegrationMode=true*ControlledTestWritesEnabled=true*");
    }

    [Fact]
    public void Validate_WithExplicitControlledTestWriteGateAndCompleteConfiguration_Succeeds()
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values["OperationalRecords:ControlledTestWritesEnabled"] = "true";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("OperationalRecords:ReadOnlyIntegrationMode", "true")]
    [InlineData("OperationalRecords:SourceProvider", "Disabled")]
    [InlineData("Jira:Provider", "Disabled")]
    public void Validate_WithControlledTestWriteGateAndUnsafeProviderState_FailsClosed(string key, string value)
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values["OperationalRecords:ControlledTestWritesEnabled"] = "true";
        values[key] = value;

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ControlledTestWritesEnabled requires*");
    }

    [Fact]
    public void Validate_WithControlledTestWriteGateOutsideTest_FailsClosed()
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values["OperationalRecords:ControlledTestWritesEnabled"] = "true";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Pilot");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ControlledTestWritesEnabled requires Test*");
    }

    [Fact]
    public void Validate_WithReadOnlyModeAndNonCorporateProviders_FailsClosed()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:ReadOnlyIntegrationMode"] = "true",
            ["OperationalRecords:SourceProvider"] = "Fake",
            ["Jira:Provider"] = "Fake"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*SourceProvider=TuruncuHat*Jira:Provider=Corporate*");
    }

    [Theory]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void Validate_WithReadOnlyModeOutsideTest_FailsClosed(string environmentName)
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), environmentName);

        act.Should().Throw<InvalidOperationException>().WithMessage("*permitted only in Test*");
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
    public void Validate_WithReadOnlyEnterpriseProviderConfiguration_SucceedsWithPreviewMappings()
    {
        IConfiguration configuration = Configuration(ReadOnlyEnterpriseValues());

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration, "Test");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("TuruncuHat:BaseUrl", "http://source.invalid/")]
    [InlineData("TuruncuHat:Authorization", "")]
    [InlineData("TuruncuHat:Username", "")]
    [InlineData("TuruncuHat:Password", "")]
    [InlineData("TuruncuHat:TenantId", "0")]
    [InlineData("TuruncuHat:SourceBaseObject", "")]
    [InlineData("TuruncuHat:RelatedGroupId", "0")]
    [InlineData("TuruncuHat:ExcludedDccIds:0", "0")]
    [InlineData("TuruncuHat:SessionLifetimeSeconds", "0")]
    public void Validate_WithMissingTuruncuHatReadConfiguration_FailsClearly(string key, string value)
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();
        values[key] = value;

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Jira:BaseUrl", "http://jira.invalid/")]
    [InlineData("Jira:Authorization", "")]
    public void Validate_WithMissingOrInvalidJiraReadConfiguration_FailsClearly(string key, string value)
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();
        values[key] = value;

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_WithUnsupportedReporterModeInReadOnlyMode_FailsClearly()
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();
        values["Jira:ReporterMode"] = "Explicit";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProjectDefault or AuthenticatedOperator*");
    }

    [Fact]
    public void Validate_WithAuthenticatedOperatorReporterInReadOnlyMode_SucceedsWithPreviewMappings()
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();
        values["Jira:ReporterMode"] = "AuthenticatedOperator";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithAuthenticatedOperatorReporterInWriteMode_RemainsCompatibleWithStrictWriteValidation()
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values["Jira:ReporterMode"] = "AuthenticatedOperator";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Pilot");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Jira:IssueTypeId")]
    [InlineData("Jira:TeamCustomField")]
    [InlineData("Jira:TeamValue")]
    [InlineData("Jira:RequesterWatcherCustomField")]
    [InlineData("Jira:Labels:0")]
    public void Validate_WithReadOnlyCorporatePreviewAndMissingMapping_FailsClearly(string key)
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();
        values.Remove(key);

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Corporate Jira mapping configuration*");
    }

    [Fact]
    public void Validate_WithMalformedBasicAuthorization_FailsClearly()
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();
        values["Jira:Authorization"] = "Basic not-base64";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*AuthenticationMode Basic*runtime Basic Authorization*");
    }

    [Fact]
    public void Validate_WithProjectDefaultAndAssigneeMappings_FailsClearly()
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values["Jira:OperatorAssigneeMappings:0:SecureOpsActor"] = "EXAMPLE\\operator";
        values["Jira:OperatorAssigneeMappings:0:JiraUsername"] = "verified.operator";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Pilot");

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty for ProjectDefault*");
    }

    [Fact]
    public void Validate_WithVerifiedExactOperatorMapping_Succeeds()
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values["Jira:AssignmentMode"] = "VerifiedOperatorMapping";
        values["Jira:OperatorAssigneeMappings:0:SecureOpsActor"] = "EXAMPLE\\operator";
        values["Jira:OperatorAssigneeMappings:0:JiraUsername"] = "verified.operator";

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Pilot");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("TuruncuHat:ActivityBaseObject")]
    [InlineData("TuruncuHat:ActivityTaskModelId")]
    [InlineData("TuruncuHat:ActivityGroupId")]
    [InlineData("TuruncuHat:ActivityMainObjectTypeId")]
    [InlineData("TuruncuHat:CompletedStatusId")]
    [InlineData("TuruncuHat:CompletionCommentTemplate")]
    [InlineData("Jira:IssueTypeId")]
    [InlineData("Jira:TeamCustomField")]
    [InlineData("Jira:TeamValue")]
    [InlineData("Jira:RequesterWatcherCustomField")]
    [InlineData("Jira:Labels:0")]
    public void Validate_WithWriteModeAndMissingWriteConfiguration_FailsClearly(string key)
    {
        Dictionary<string, string?> values = WriteEnterpriseValues();
        values.Remove(key);

        Action act = () => OperationalRecordConfigurationValidator.Validate(Configuration(values), "Pilot");

        act.Should().Throw<InvalidOperationException>();
    }

    private static Dictionary<string, string?> ReadOnlyEnterpriseValues() => new()
    {
        ["OperationalRecords:SourceProvider"] = "TuruncuHat",
        ["OperationalRecords:ReadOnlyIntegrationMode"] = "true",
        ["Jira:Provider"] = "Corporate",
        ["TuruncuHat:BaseUrl"] = "https://source.invalid/",
        ["TuruncuHat:Authorization"] = "Sanitized runtime value",
        ["TuruncuHat:Username"] = "sanitized-user",
        ["TuruncuHat:Password"] = "sanitized-secret",
        ["TuruncuHat:TenantId"] = "218",
        ["TuruncuHat:SourceBaseObject"] = "SMSS_oRFF",
        ["TuruncuHat:RelatedGroupId"] = "68",
        ["TuruncuHat:ExcludedDccIds:0"] = "4241",
        ["TuruncuHat:SessionLifetimeSeconds"] = "60",
        ["Jira:BaseUrl"] = "https://jira.invalid/",
        ["Jira:Authorization"] = "Basic c2FuaXRpemVkOnNlY3JldA==",
        ["Jira:IssueTypeId"] = "3",
        ["Jira:TeamCustomField"] = "customfield_12700",
        ["Jira:TeamValue"] = "WASAS",
        ["Jira:RequesterWatcherCustomField"] = "customfield_11500",
        ["Jira:Labels:0"] = "SunucuTalep"
    };

    private static Dictionary<string, string?> WriteEnterpriseValues()
    {
        Dictionary<string, string?> values = ReadOnlyEnterpriseValues();
        values["OperationalRecords:ReadOnlyIntegrationMode"] = "false";
        values["TuruncuHat:ActivityBaseObject"] = "BPM_Actvty";
        values["TuruncuHat:ActivityTaskModelId"] = "10";
        values["TuruncuHat:ActivityGroupId"] = "20";
        values["TuruncuHat:ActivityMainObjectTypeId"] = "30";
        values["TuruncuHat:CompletedStatusId"] = "40";
        values["TuruncuHat:CompletionCommentTemplate"] = "Transferred to {JiraKey}";
        return values;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
