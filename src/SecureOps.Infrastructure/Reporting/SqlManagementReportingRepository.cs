using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>SQL Server implementation that aggregates reporting data before returning it to the API.</summary>
public sealed class SqlManagementReportingRepository : IManagementReportingRepository
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    /// <summary>Initializes the SQL reporting repository.</summary>
    public SqlManagementReportingRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for management reporting.");
    }

    /// <inheritdoc />
    public async Task<ManagementReportingData> GetSummaryAsync(
        ReportingWindow window,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                CONVERT(date, SWITCHOFFSET(OccurredAt, '+00:00')) AS BucketDate,
                Action,
                CASE WHEN Action = @SourceChangedAction THEN JSON_VALUE(DetailsJson, '$.errorCode') END AS DetailCode,
                COUNT_BIG(*) AS [Count]
            FROM reporting.ManagementAuditEvents
            WHERE OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive
              AND Action IN @SummaryActions
            GROUP BY CONVERT(date, SWITCHOFFSET(OccurredAt, '+00:00')), Action,
                CASE WHEN Action = @SourceChangedAction THEN JSON_VALUE(DetailsJson, '$.errorCode') END;

            SELECT WorkflowState, COUNT_BIG(DISTINCT OperationalRecordId) AS [Count]
            FROM reporting.ManagementWorkflowEvents
            WHERE OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive
            GROUP BY WorkflowState;

            SELECT COUNT_BIG(DISTINCT Actor)
            FROM reporting.ManagementAuditEvents
            WHERE OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive
              AND Action IN @IdentityTerminalActions
              AND Actor <> 'anonymous' AND Actor NOT LIKE 'system:%';

            SELECT
                (SELECT COUNT_BIG(DISTINCT Actor) FROM reporting.ManagementAuditEvents
                 WHERE OccurredAt >= DATEADD(day, -1, @ToExclusive) AND OccurredAt < @ToExclusive
                   AND Action IN @AdoptionActions AND Actor <> 'anonymous' AND Actor NOT LIKE 'system:%') AS Daily,
                (SELECT COUNT_BIG(DISTINCT Actor) FROM reporting.ManagementAuditEvents
                 WHERE OccurredAt >= DATEADD(day, -7, @ToExclusive) AND OccurredAt < @ToExclusive
                   AND Action IN @AdoptionActions AND Actor <> 'anonymous' AND Actor NOT LIKE 'system:%') AS Weekly,
                (SELECT COUNT_BIG(DISTINCT Actor) FROM reporting.ManagementAuditEvents
                 WHERE OccurredAt >= DATEADD(day, -30, @ToExclusive) AND OccurredAt < @ToExclusive
                   AND Action IN @AdoptionActions AND Actor <> 'anonymous' AND Actor NOT LIKE 'system:%') AS Monthly,
                (SELECT COUNT_BIG(DISTINCT Actor) FROM reporting.ManagementAuditEvents
                 WHERE OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive
                   AND Action IN @AdoptionActions AND Actor <> 'anonymous' AND Actor NOT LIKE 'system:%') AS SelectedWindow;

            SELECT COUNT_BIG(*)
            FROM reporting.ManagementOperationalStatus
            WHERE ReconciliationRequired = 1
              AND TransferUpdatedAt >= @FromInclusive AND TransferUpdatedAt < @ToExclusive;

            WITH RetryCorrelations AS
            (
                SELECT DISTINCT CorrelationId
                FROM reporting.ManagementAuditEvents
                WHERE OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive
                  AND Action = @RetryAction AND CorrelationId IS NOT NULL
            ),
            RetryOutcomes AS
            (
                SELECT retry.CorrelationId,
                    MAX(CASE WHEN audit.Action = @CompletedAction THEN 1 ELSE 0 END) AS HasSucceeded,
                    MAX(CASE WHEN audit.Action IN @FailureActions THEN 1 ELSE 0 END) AS HasFailed
                FROM RetryCorrelations retry
                LEFT JOIN reporting.ManagementAuditEvents audit
                  ON audit.CorrelationId = retry.CorrelationId AND audit.OccurredAt < @ToExclusive
                GROUP BY retry.CorrelationId
            )
            SELECT
                COUNT_BIG(*) AS Requested,
                COALESCE(SUM(CONVERT(bigint, HasSucceeded)), 0) AS Succeeded,
                COALESCE(SUM(CONVERT(bigint, CASE WHEN HasSucceeded = 0 AND HasFailed = 1 THEN 1 ELSE 0 END)), 0) AS Failed
            FROM RetryOutcomes;

            WITH WorkflowStages AS
            (
                SELECT OperationalRecordId,
                    MIN(CASE WHEN WorkflowState = 'Imported' THEN OccurredAt END) AS ImportedAt,
                    MIN(CASE WHEN WorkflowState = 'Previewed' THEN OccurredAt END) AS PreviewedAt
                FROM reporting.ManagementWorkflowEvents
                GROUP BY OperationalRecordId
            ),
            AuditStages AS
            (
                SELECT CorrelationId,
                    TRY_CONVERT(uniqueidentifier, JSON_VALUE(DetailsJson, '$.operationalRecordId')) AS OperationalRecordId,
                    MIN(CASE WHEN Action = @ClaimedAction THEN OccurredAt END) AS ClaimedAt,
                    MIN(CASE WHEN Action = @JiraCreatedAction THEN OccurredAt END) AS JiraCreatedAt,
                    MIN(CASE WHEN Action = @CompletedAction THEN OccurredAt END) AS CompletedAt
                FROM reporting.ManagementAuditEvents
                WHERE Action IN @TimingActions AND CorrelationId IS NOT NULL
                GROUP BY CorrelationId, TRY_CONVERT(uniqueidentifier, JSON_VALUE(DetailsJson, '$.operationalRecordId'))
            ),
            Durations AS
            (
                SELECT 'ImportToPreview' AS Name, DATEDIFF_BIG(millisecond, ImportedAt, PreviewedAt) AS DurationMilliseconds
                FROM WorkflowStages
                WHERE ImportedAt IS NOT NULL AND PreviewedAt >= @FromInclusive AND PreviewedAt < @ToExclusive
                  AND ImportedAt <= PreviewedAt
                UNION ALL
                SELECT 'ClaimToJiraCreated', DATEDIFF_BIG(millisecond, ClaimedAt, JiraCreatedAt)
                FROM AuditStages
                WHERE ClaimedAt IS NOT NULL AND JiraCreatedAt >= @FromInclusive AND JiraCreatedAt < @ToExclusive
                  AND ClaimedAt <= JiraCreatedAt
                UNION ALL
                SELECT 'ClaimToCompleted', DATEDIFF_BIG(millisecond, ClaimedAt, CompletedAt)
                FROM AuditStages
                WHERE ClaimedAt IS NOT NULL AND CompletedAt >= @FromInclusive AND CompletedAt < @ToExclusive
                  AND ClaimedAt <= CompletedAt
            )
            SELECT Name, COUNT_BIG(*) AS SampleCount,
                MIN(DurationMilliseconds) / 1000.0 AS MinimumSeconds,
                AVG(CONVERT(float, DurationMilliseconds)) / 1000.0 AS AverageSeconds,
                MAX(DurationMilliseconds) / 1000.0 AS MaximumSeconds
            FROM Durations
            GROUP BY Name;
            """;

        object parameters = new
        {
            FromInclusive = window.FromInclusiveUtc,
            ToExclusive = window.ToExclusiveUtc,
            SummaryActions = ReportingMetricCatalog.SummaryActions,
            IdentityTerminalActions = ReportingMetricCatalog.IdentityTerminalActions,
            AdoptionActions = ReportingMetricCatalog.AdoptionActions,
            SourceChangedAction = AuditActions.OperationalRecordSourceChanged,
            RetryAction = AuditActions.WorkflowRetried,
            CompletedAction = AuditActions.WorkflowCompleted,
            FailureActions = new[] { AuditActions.JiraCreateFailed, AuditActions.OperationalRecordCloseFailed },
            ClaimedAction = AuditActions.OperationalRecordClaimed,
            JiraCreatedAction = AuditActions.JiraCreated,
            TimingActions = new[]
            {
                AuditActions.OperationalRecordClaimed,
                AuditActions.JiraCreated,
                AuditActions.WorkflowCompleted
            }
        };

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using SqlMapper.GridReader results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            parameters,
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: cancellationToken));

        ReportingAuditCount[] auditCounts = (await results.ReadAsync<ReportingAuditCount>()).ToArray();
        ReportingWorkflowCount[] workflowCounts = (await results.ReadAsync<ReportingWorkflowCount>()).ToArray();
        long identityUniqueOperators = await results.ReadSingleAsync<long>();
        ReportingActiveUsers activeUsers = await results.ReadSingleAsync<ReportingActiveUsers>();
        long reconciliationRequired = await results.ReadSingleAsync<long>();
        ReportingRetryOutcomes retryOutcomes = await results.ReadSingleAsync<ReportingRetryOutcomes>();
        ReportingDurationStatistics[] durations = (await results.ReadAsync<ReportingDurationStatistics>()).ToArray();

        return new ManagementReportingData(
            auditCounts,
            workflowCounts,
            identityUniqueOperators,
            activeUsers,
            reconciliationRequired,
            retryOutcomes,
            durations);
    }

    /// <inheritdoc />
    public async Task<OperatorActivityDataPage> GetOperatorActivityAsync(
        ReportingWindow window,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT_BIG(DISTINCT Actor)
            FROM reporting.ManagementAuditEvents
            WHERE OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive
              AND Action IN @AdoptionActions
              AND Actor <> 'anonymous' AND Actor NOT LIKE 'system:%';

            SELECT Actor, COUNT_BIG(*) AS OperationCount,
                MIN(OccurredAt) AS FirstActivityAt, MAX(OccurredAt) AS LastActivityAt,
                SUM(CONVERT(bigint, CASE WHEN Action IN @IdentityActions THEN 1 ELSE 0 END)) AS IdentityOperations,
                SUM(CONVERT(bigint, CASE WHEN Action IN @DirectoryActions THEN 1 ELSE 0 END)) AS DirectoryOperations,
                SUM(CONVERT(bigint, CASE WHEN Action IN @AccessActions THEN 1 ELSE 0 END)) AS AccessOperations,
                SUM(CONVERT(bigint, CASE WHEN Action IN @OperationalActions THEN 1 ELSE 0 END)) AS OperationalWorkflowOperations
            FROM reporting.ManagementAuditEvents
            WHERE OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive
              AND Action IN @AdoptionActions
              AND Actor <> 'anonymous' AND Actor NOT LIKE 'system:%'
            GROUP BY Actor
            ORDER BY OperationCount DESC, Actor
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        object parameters = new
        {
            FromInclusive = window.FromInclusiveUtc,
            ToExclusive = window.ToExclusiveUtc,
            AdoptionActions = ReportingMetricCatalog.AdoptionActions,
            IdentityActions = ReportingMetricCatalog.IdentityTerminalActions,
            DirectoryActions = ReportingMetricCatalog.DirectoryTerminalActions,
            AccessActions = ReportingMetricCatalog.AccessActivityActions,
            OperationalActions = ReportingMetricCatalog.OperationalWorkflowActions,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using SqlMapper.GridReader results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            parameters,
            commandTimeout: CommandTimeoutSeconds,
            cancellationToken: cancellationToken));
        long totalItems = await results.ReadSingleAsync<long>();
        OperatorActivityData[] items = (await results.ReadAsync<OperatorActivityData>()).ToArray();
        return new OperatorActivityDataPage(totalItems, items);
    }
}
