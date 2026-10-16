using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Reporting;

namespace SecureOps.Tests.Integration.Sql;

public sealed class OperatorReportOrderingSqlTests
{
    [LocalResourceSqlFact]
    public async Task Operators_AreOrderedByIdentity_NotByOperationVolume()
    {
        var server = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SECUREOPS_SQL_TEST_CONNECTION"));
        if (!server.DataSource.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) || !server.IntegratedSecurity)
        {
            throw new InvalidOperationException("A LocalDB integrated-security connection is required.");
        }

        string database = "SecureOps_ReportOrder_" + Guid.NewGuid().ToString("N");
        string master = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString;
        string scoped = new SqlConnectionStringBuilder(master) { InitialCatalog = database }.ConnectionString;
        await using (var connection = new SqlConnection(master))
        {
            await connection.ExecuteAsync($"CREATE DATABASE [{database}]");
        }

        try
        {
            await using (var connection = new SqlConnection(scoped))
            {
                await connection.ExecuteAsync("CREATE SCHEMA audit");
                await connection.ExecuteAsync("CREATE SCHEMA reporting");
                await connection.ExecuteAsync("""
                    CREATE TABLE audit.AuditLog
                    (
                        AuditLogId bigint IDENTITY(1,1) PRIMARY KEY,
                        OccurredAt datetimeoffset(7) NOT NULL,
                        Actor nvarchar(256) NOT NULL,
                        Action nvarchar(128) NOT NULL,
                        CorrelationId nvarchar(256) NULL,
                        DetailsJson nvarchar(max) NULL
                    )
                    """);
                await connection.ExecuteAsync("""
                    CREATE VIEW reporting.ManagementAuditEvents AS
                    SELECT OccurredAt, Actor, Action, CorrelationId, DetailsJson FROM audit.AuditLog
                    """);
                string action = ReportingMetricCatalog.AdoptionActions[0];
                DateTimeOffset at = DateTimeOffset.UtcNow.AddHours(-1);
                // Synthetic principals: the busiest one sorts last, the quietest first.
                await connection.ExecuteAsync(
                    "INSERT INTO audit.AuditLog(OccurredAt, Actor, Action) VALUES (@at, @actor, @action)",
                    Enumerable.Range(0, 1).Select(_ => new { at, actor = "synthetic-a", action })
                        .Concat(Enumerable.Range(0, 2).Select(_ => new { at, actor = "synthetic-b", action }))
                        .Concat(Enumerable.Range(0, 9).Select(_ => new { at, actor = "synthetic-c", action })));
            }

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = scoped })
                .Build();
            var repository = new SqlManagementReportingRepository(configuration);
            var window = new ReportingWindow("24h", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(5));

            OperatorActivityDataPage first = await repository.GetOperatorActivityAsync(window, 1, 2, default);
            OperatorActivityDataPage second = await repository.GetOperatorActivityAsync(window, 2, 2, default);

            first.TotalItems.Should().Be(3);
            first.Items.Select(item => item.Actor).Should().Equal("synthetic-a", "synthetic-b");
            second.Items.Select(item => item.Actor).Should().Equal("synthetic-c");
            first.Items.Select(item => item.OperationCount).Should().Equal(1, 2);
        }
        finally
        {
            await using var connection = new SqlConnection(master);
            await connection.ExecuteAsync($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        }
    }
}
