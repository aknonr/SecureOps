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

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SourceProvider*Fake*");
    }

    [Fact]
    public void Validate_WithSqlRepositoryWithoutConnectionString_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["OperationalRecords:RepositoryProvider"] = "SqlServer"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:SecureOpsDb*");
    }

    [Fact]
    public void Validate_WithUnapprovedRequesterPolicy_FailsClearly()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["Jira:UnresolvedRequesterPolicy"] = "Guess"
        });

        Action act = () => OperationalRecordConfigurationValidator.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Block or ProceedUnassigned*");
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
