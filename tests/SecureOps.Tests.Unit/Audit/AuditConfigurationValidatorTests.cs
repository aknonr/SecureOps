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
            ["Audit:Provider"] = "File",
            ["Audit:FailClosed"] = "false",
            ["Audit:File:Directory"] = @"D:\SecureOps\Audit"
        });

        Action act = () => AuditConfigurationValidator.Validate(configuration, "Production");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FailClosed*Production*");
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

    [Fact]
    public void Validate_WhenSqlServerUsesSecureOpsDbConnectionString_DoesNotThrow()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "SqlServer",
            ["Audit:FailClosed"] = "true",
            ["ConnectionStrings:SecureOpsDb"] = "Server=.;Database=SecureOps;Integrated Security=true;TrustServerCertificate=true"
        });

        Action act = () => AuditConfigurationValidator.Validate(configuration, "Development");

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
