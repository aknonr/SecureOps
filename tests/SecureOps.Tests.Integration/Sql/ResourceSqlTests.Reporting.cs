using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Audit;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    // ADR-0011 Amendment 1: the fact-based summary must equal the set-based SQL summary over the same rows.
    [LocalResourceSqlFact]
    public async Task Reporting_FactReadersOverSql_MatchSqlAggregation()
    {
        IConfiguration configuration = Configuration();
        var record = Guid.NewGuid();
        string correlation = "synthetic-report-" + Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (var connection = new SqlConnection(configuration.GetConnectionString("SecureOpsDb")))
        {
            await connection.ExecuteAsync("""
                INSERT INTO ops.OperationalRecords(OperationalRecordId,SourceRecordId,OrCode,Title,Description,Classification,JiraEligible,EligibilityReason,WorkflowState,UpdatedAt)
                VALUES(@record,@source,'OR-SYN-REPORT','Synthetic report fixture','Synthetic','NeedsManualReview',0,'Synthetic','Previewed',@now);
                INSERT INTO ops.OperationalRecordWorkflowHistory(OperationalRecordId,WorkflowState,Actor,CorrelationId,OccurredAt)
                VALUES(@record,'Imported','system:source-import',@correlation,DATEADD(minute,-30,@now)),
                      (@record,'Previewed','CONTOSO\synthetic-report',@correlation,DATEADD(minute,-20,@now));
                INSERT INTO ops.JiraTransfers(JiraTransferId,OperationalRecordId,MappingVersion,IdempotencyKey,CreatedByActor,CreatedAt,UpdatedAt,ReconciliationRequired)
                VALUES(NEWID(),@record,'synthetic',REPLICATE('b',64),'CONTOSO\synthetic-report',@now,@now,1);
                INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson) VALUES
                    (DATEADD(minute,-15,@now),'CONTOSO\synthetic-report',@lookup,NULL,NULL),
                    (DATEADD(minute,-14,@now),'contoso\SYNTHETIC-report',@sourceChanged,NULL,'{"errorCode":"SourceChanged"}'),
                    (DATEADD(minute,-13,@now),'CONTOSO\synthetic-report',@claimed,@correlation,@details),
                    (DATEADD(minute,-12,@now),'CONTOSO\synthetic-report',@jiraCreated,@correlation,@details),
                    (DATEADD(minute,-11,@now),'CONTOSO\synthetic-report',@retried,@correlation,@details),
                    (DATEADD(minute,-10,@now),'CONTOSO\synthetic-report',@completed,@correlation,@details);
                """, new
            {
                record,
                source = "synthetic-report-" + record.ToString("N"),
                correlation,
                now,
                details = $$"""{"operationalRecordId":"{{record}}"}""",
                lookup = AuditActions.IdentityLookupSucceeded,
                sourceChanged = AuditActions.OperationalRecordSourceChanged,
                claimed = AuditActions.OperationalRecordClaimed,
                jiraCreated = AuditActions.JiraCreated,
                retried = AuditActions.WorkflowRetried,
                completed = AuditActions.WorkflowCompleted
            });
        }

        var window = new ReportingWindow("custom", now.AddDays(-2), now.AddMinutes(5));
        var facts = new FactManagementReportingRepository(new SqlReportingAuditFacts(configuration), new SqlReportingWorkflowFacts(configuration));
        var sql = new SqlManagementReportingRepository(configuration);

        ManagementReportingData fromFacts = await facts.GetSummaryAsync(window, _token);
        ManagementReportingData fromSql = await sql.GetSummaryAsync(window, _token);

        fromFacts.Sources.IsDurable.Should().BeTrue();
        fromFacts.AuditCounts.Should().BeEquivalentTo(fromSql.AuditCounts);
        fromFacts.WorkflowCounts.Should().BeEquivalentTo(fromSql.WorkflowCounts);
        fromFacts.IdentityUniqueOperators.Should().Be(fromSql.IdentityUniqueOperators);
        fromFacts.ActiveUsers.Should().Be(fromSql.ActiveUsers);
        fromFacts.ReconciliationRequired.Should().Be(fromSql.ReconciliationRequired);
        fromFacts.RetryOutcomes.Should().Be(fromSql.RetryOutcomes);
        fromFacts.Durations.Should().BeEquivalentTo(fromSql.Durations, options => options
            .Using<double?>(ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 0.001)).WhenTypeIs<double?>());
        fromFacts.CoverageFromUtc.Should().Be(fromSql.CoverageFromUtc);
        fromFacts.AuditCounts.Should().Contain(count => count.Action == AuditActions.OperationalRecordSourceChanged && count.DetailCode == "SourceChanged");

        OperatorActivityDataPage factOperators = await facts.GetOperatorActivityAsync(window, 1, 50, _token);
        OperatorActivityDataPage sqlOperators = await sql.GetOperatorActivityAsync(window, 1, 50, _token);
        factOperators.TotalItems.Should().Be(sqlOperators.TotalItems);
        factOperators.Items.Select(item => (item.Actor.ToUpperInvariant(), item.OperationCount))
            .Should().Equal(sqlOperators.Items.Select(item => (item.Actor.ToUpperInvariant(), item.OperationCount)));
    }
}
