using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Tests.Unit.Audit;

public sealed class AuditConfigurationValidatorTests
{
    [Fact]
    public void Validate_WhenProductionUsesInMemory_Throws()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "InMemory",
            ["Audit:FailClosed"] = "true"
        });

        Action act = () => AuditConfigurationValidator.Validate(configuration, "Production");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*InMemory*Production*");
    }

    [Fact]
    public void Validate_WhenProductionFailOpen_Throws()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "SqlServer",
            ["Audit:FailClosed"] = "false",
            ["ConnectionStrings:SecureOpsDb"] = "Server=.;Database=Synthetic;Integrated Security=true"
        });

        Action act = () => AuditConfigurationValidator.Validate(configuration, "Production");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FailClosed*Production*");
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public void Validate_FileRetentionFloor_RequiresApprovedProductionStore(string environment, bool rejected)
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "File",
            ["Audit:FailClosed"] = "true",
            ["Audit:RequirePersistentStoreInProduction"] = "false",
            ["Audit:File:Directory"] = Path.Combine(Path.GetTempPath(), "synthetic-audit"),
            ["Audit:File:RetainedFileCountLimit"] = "0"
        });
        Action act = () => AuditConfigurationValidator.Validate(configuration, environment);

        if (rejected)
        {
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*File audit*not an approved Production store*at least 36 months*SqlServer is required*");
        }
        else
        {
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void Validate_WhenSqlServerMissingConnectionString_Throws()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "SqlServer",
            ["Audit:FailClosed"] = "true"
        });

        Action act = () => AuditConfigurationValidator.Validate(configuration, "Development");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:SecureOpsDb*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Validate_WhenSqlServerUsesSecureOpsDbConnectionString_DoesNotThrow(string environment)
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "SqlServer",
            ["Audit:FailClosed"] = "true",
            ["ConnectionStrings:SecureOpsDb"] = "Server=.;Database=SecureOps;Integrated Security=true;TrustServerCertificate=true"
        });

        Action act = () => AuditConfigurationValidator.Validate(configuration, environment);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WhenSqlServerUsesLegacySecureOpsOnly_Throws()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "SqlServer",
            ["Audit:FailClosed"] = "true",
            ["ConnectionStrings:SecureOps"] = "Server=.;Database=SecureOps;Integrated Security=true;TrustServerCertificate=true"
        });

        Action act = () => AuditConfigurationValidator.Validate(configuration, "Development");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:SecureOpsDb*");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
