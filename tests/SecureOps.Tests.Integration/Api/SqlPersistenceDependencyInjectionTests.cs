using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Infrastructure.Persistence;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Infrastructure.Sessions;

namespace SecureOps.Tests.Integration.Api;

public sealed class SqlPersistenceDependencyInjectionTests
{
    [Fact]
    public void FullSqlProviderSelection_RegistersEveryAuthoritativeRepositoryWithoutConnecting()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Audit:Provider"] = "SqlServer",
            ["Audit:Queue:Enabled"] = "true",
            ["Access:RepositoryProvider"] = "SqlServer",
            ["SessionSecurity:RepositoryProvider"] = "SqlServer",
            ["OperationalRecords:RepositoryProvider"] = "SqlServer",
            ["OperationalRecords:SourceProvider"] = "Disabled",
            ["Jira:Provider"] = "Disabled",
            ["ConnectionStrings:SecureOpsDb"] = "Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout=15"
        }).Build();
        ServiceCollection registrations = new();
        registrations.AddSingleton(configuration);
        registrations.AddLogging();
        registrations.AddSecureOpsInfrastructure(configuration);
        using ServiceProvider services = registrations.BuildServiceProvider();
        using IServiceScope scope = services.CreateScope();

        services.GetRequiredService<IAuditEventSink>().Should().BeOfType<SqlAuditWriter>();
        services.GetRequiredService<ISqlPersistenceProbe>().Should().BeOfType<SqlPersistenceProbe>();
        scope.ServiceProvider.GetRequiredService<IAccessRepository>().Should().BeOfType<SqlAccessRepository>();
        scope.ServiceProvider.GetRequiredService<IFirstAdminBootstrapStore>().Should().BeOfType<SqlFirstAdminBootstrapStore>();
        scope.ServiceProvider.GetRequiredService<IApplicationSessionRepository>().Should().BeOfType<SqlApplicationSessionRepository>();
        scope.ServiceProvider.GetRequiredService<IOperationalRecordRepository>().Should().BeOfType<SqlOperationalRecordRepository>();
        scope.ServiceProvider.GetRequiredService<ICommandIdempotencyStore>().Should().BeOfType<SqlCommandIdempotencyStore>();
        scope.ServiceProvider.GetRequiredService<IManagementReportingRepository>().Should().BeOfType<SqlManagementReportingRepository>();
    }

    [Theory]
    [InlineData("Audit:Provider")]
    [InlineData("Access:RepositoryProvider")]
    [InlineData("OperationalRecords:RepositoryProvider")]
    public void PartialSqlProviderSelection_DoesNotFallBackToInMemoryReporting(string nonSqlSetting)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Audit:Provider"] = "SqlServer",
            ["Audit:Queue:Enabled"] = "true",
            ["Access:RepositoryProvider"] = "SqlServer",
            ["SessionSecurity:RepositoryProvider"] = "SqlServer",
            ["OperationalRecords:RepositoryProvider"] = "SqlServer",
            ["OperationalRecords:SourceProvider"] = "Disabled",
            ["Jira:Provider"] = "Disabled",
            ["ConnectionStrings:SecureOpsDb"] = "Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout=15"
        };
        settings[nonSqlSetting] = "InMemory";
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection registrations = new();
        registrations.AddSingleton(configuration);
        registrations.AddLogging();
        registrations.AddSecureOpsInfrastructure(configuration);
        using ServiceProvider services = registrations.BuildServiceProvider();

        services.GetRequiredService<IManagementReportingRepository>()
            .Should().BeOfType<UnavailableManagementReportingRepository>();
    }
}
