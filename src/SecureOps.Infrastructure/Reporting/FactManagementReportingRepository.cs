using System.Collections.Frozen;
using System.Text.Json.Nodes;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>
/// Computes the management summary from raw evidence rows when the sources are not all SQL Server, so a
/// set-based SQL aggregation cannot run. Each stream is read from wherever it is configured (SQL or
/// memory) and aggregated here with the same rules as <see cref="SqlManagementReportingRepository"/>.
/// </summary>
/// <remarks>
/// SQL Server compares actors and correlation ids under a case-insensitive collation, so this class
/// does the same. Reading facts strictly before the window end gives the same first-occurrence
/// timings as the unbounded SQL <c>MIN</c>, because a later row can never be an earlier minimum.
/// </remarks>
public sealed class FactManagementReportingRepository(IReportingAuditFacts auditFacts, IReportingWorkflowFacts workflowFacts)
    : IManagementReportingRepository
{
    private static readonly StringComparer _actorComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly FrozenSet<string> _summary = ReportingMetricCatalog.SummaryActions.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> _identity = ReportingMetricCatalog.IdentityTerminalActions.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> _directory = ReportingMetricCatalog.DirectoryTerminalActions.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> _access = ReportingMetricCatalog.AccessActivityActions.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> _operational = ReportingMetricCatalog.OperationalWorkflowActions.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> _adoption = ReportingMetricCatalog.AdoptionActions.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> _activeUser = ReportingMetricCatalog.ActiveUserActions.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> _retryFailure = ReportingMetricCatalog.RetryFailureActions.ToFrozenSet(StringComparer.Ordinal);

    private ReportingSources Sources => new(auditFacts.Kind, workflowFacts.Kind);

    /// <inheritdoc />
    public async Task<ManagementReportingData> GetSummaryAsync(ReportingWindow window, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportingAuditFact> audit = await auditFacts.ReadAsync(window.ToExclusiveUtc, cancellationToken);
        IReadOnlyList<ReportingWorkflowFact> history = await workflowFacts.ReadHistoryAsync(window.ToExclusiveUtc, cancellationToken);
        IReadOnlyList<ReportingTransferFact> transfers = await workflowFacts.ReadTransfersAsync(cancellationToken);
        ReportingAuditFact[] inWindow = [.. audit.Where(row => window.Contains(row.OccurredAt))];

        ReportingAuditCount[] auditCounts =
        [
            .. inWindow.Where(row => _summary.Contains(row.Action))
                .GroupBy(row => (row.OccurredAt.UtcDateTime.Date, row.Action, DetailCode: DetailCode(row)))
                .Select(group => new ReportingAuditCount(group.Key.Date, group.Key.Action, group.Key.DetailCode, group.LongCount()))
        ];

        ReportingWorkflowCount[] workflowCounts =
        [
            .. history.Where(row => window.Contains(row.OccurredAt))
                .GroupBy(row => row.WorkflowState, StringComparer.Ordinal)
                .Select(group => new ReportingWorkflowCount(group.Key, group.Select(row => row.OperationalRecordId).Distinct().LongCount()))
        ];

        long identityOperators = DistinctActors(inWindow, _identity);
        ReportingActiveUsers activeUsers = new(
            DistinctActors(audit.Where(row => row.OccurredAt >= window.ToExclusiveUtc.AddDays(-1)), _activeUser),
            DistinctActors(audit.Where(row => row.OccurredAt >= window.ToExclusiveUtc.AddDays(-7)), _activeUser),
            DistinctActors(audit.Where(row => row.OccurredAt >= window.ToExclusiveUtc.AddDays(-30)), _activeUser),
            DistinctActors(inWindow, _activeUser));

        long reconciliation = transfers.LongCount(row => row.ReconciliationRequired && window.Contains(row.UpdatedAt));

        return new ManagementReportingData(
            auditCounts,
            workflowCounts,
            identityOperators,
            activeUsers,
            reconciliation,
            RetryOutcomes(audit, inWindow),
            Durations(window, audit, history),
            Coverage(audit))
        {
            Sources = Sources
        };
    }

    /// <inheritdoc />
    public async Task<OperatorActivityDataPage> GetOperatorActivityAsync(
        ReportingWindow window,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportingAuditFact> audit = await auditFacts.ReadAsync(window.ToExclusiveUtc, cancellationToken);
        IGrouping<string, ReportingAuditFact>[] operators =
        [
            .. audit.Where(row => window.Contains(row.OccurredAt) && _adoption.Contains(row.Action) && IsPerson(row.Actor))
                .GroupBy(row => row.Actor, _actorComparer)
        ];

        OperatorActivityData[] items =
        [
            .. operators
                .Select(group => new OperatorActivityData(
                    group.Key,
                    group.LongCount(),
                    group.Min(row => row.OccurredAt),
                    group.Max(row => row.OccurredAt),
                    group.LongCount(row => _identity.Contains(row.Action)),
                    group.LongCount(row => _directory.Contains(row.Action)),
                    group.LongCount(row => _access.Contains(row.Action)),
                    group.LongCount(row => _operational.Contains(row.Action))))
                .OrderByDescending(item => item.OperationCount)
                .ThenBy(item => item.Actor, _actorComparer)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
        ];

        return new OperatorActivityDataPage(operators.Length, items, Coverage(audit));
    }

    private static ReportingRetryOutcomes RetryOutcomes(IReadOnlyList<ReportingAuditFact> audit, IReadOnlyList<ReportingAuditFact> inWindow)
    {
        HashSet<string> retried = new(
            inWindow.Where(row => row.Action == AuditActions.WorkflowRetried && row.CorrelationId is not null).Select(row => row.CorrelationId!),
            _actorComparer);
        ILookup<string, string> outcomes = audit
            .Where(row => row.CorrelationId is not null && retried.Contains(row.CorrelationId))
            .ToLookup(row => row.CorrelationId!, row => row.Action, _actorComparer);

        long succeeded = 0, failed = 0;
        foreach (string correlation in retried)
        {
            IEnumerable<string> actions = outcomes[correlation];
            if (actions.Contains(AuditActions.WorkflowCompleted))
            {
                succeeded++;
            }
            else if (actions.Any(_retryFailure.Contains))
            {
                failed++;
            }
        }

        return new ReportingRetryOutcomes(retried.Count, succeeded, failed);
    }

    private static ReportingDurationStatistics[] Durations(
        ReportingWindow window,
        IReadOnlyList<ReportingAuditFact> audit,
        IReadOnlyList<ReportingWorkflowFact> history)
    {
        List<(string Key, double Milliseconds)> samples = [];

        foreach (IGrouping<Guid, ReportingWorkflowFact> record in history.GroupBy(row => row.OperationalRecordId))
        {
            DateTimeOffset? imported = First(record, nameof(OperationalRecordWorkflowState.Imported));
            DateTimeOffset? previewed = First(record, nameof(OperationalRecordWorkflowState.Previewed));
            Add(ManagementReportingDurationKeys.ImportToPreview, imported, previewed);
        }

        IEnumerable<IGrouping<(string Correlation, Guid? Record), ReportingAuditFact>> stages = audit
            .Where(row => row.CorrelationId is not null && ReportingMetricCatalog.TimingActions.Contains(row.Action))
            .GroupBy(row => (Correlation: row.CorrelationId!.ToUpperInvariant(), Record: OperationalRecordId(row)));
        foreach (IGrouping<(string Correlation, Guid? Record), ReportingAuditFact> stage in stages)
        {
            DateTimeOffset? claimed = FirstAction(stage, AuditActions.OperationalRecordClaimed);
            Add(ManagementReportingDurationKeys.ClaimToJiraCreation, claimed, FirstAction(stage, AuditActions.JiraCreated));
            Add(ManagementReportingDurationKeys.ClaimToCompletion, claimed, FirstAction(stage, AuditActions.WorkflowCompleted));
        }

        return
        [
            .. samples.GroupBy(sample => sample.Key, StringComparer.Ordinal)
                .Select(group => new ReportingDurationStatistics(
                    group.Key,
                    group.LongCount(),
                    group.Min(sample => sample.Milliseconds) / 1000.0,
                    group.Average(sample => sample.Milliseconds) / 1000.0,
                    group.Max(sample => sample.Milliseconds) / 1000.0))
        ];

        void Add(string key, DateTimeOffset? start, DateTimeOffset? end)
        {
            if (start is { } from && end is { } to && window.Contains(to) && from <= to)
            {
                // DATEDIFF_BIG(millisecond, ...) counts whole elapsed milliseconds.
                samples.Add((key, Math.Floor((to - from).TotalMilliseconds)));
            }
        }

        static DateTimeOffset? First(IEnumerable<ReportingWorkflowFact> rows, string state) =>
            rows.Where(row => row.WorkflowState == state).Select(row => (DateTimeOffset?)row.OccurredAt).Min();

        static DateTimeOffset? FirstAction(IEnumerable<ReportingAuditFact> rows, string action) =>
            rows.Where(row => row.Action == action).Select(row => (DateTimeOffset?)row.OccurredAt).Min();
    }

    private static DateTimeOffset? Coverage(IReadOnlyList<ReportingAuditFact> audit) =>
        audit.Where(row => _summary.Contains(row.Action)).Select(row => (DateTimeOffset?)row.OccurredAt).Min();

    private static long DistinctActors(IEnumerable<ReportingAuditFact> rows, FrozenSet<string> actions) =>
        rows.Where(row => actions.Contains(row.Action) && IsPerson(row.Actor))
            .Select(row => row.Actor)
            .Distinct(_actorComparer)
            .LongCount();

    // Mirrors "Actor <> 'anonymous' AND Actor NOT LIKE 'system:%'" under a case-insensitive collation.
    private static bool IsPerson(string actor) =>
        !actor.Equals("anonymous", StringComparison.OrdinalIgnoreCase)
        && !actor.StartsWith("system:", StringComparison.OrdinalIgnoreCase);

    private static string? DetailCode(ReportingAuditFact row) =>
        row.Action == AuditActions.OperationalRecordSourceChanged ? JsonValue(row, "errorCode") : null;

    private static Guid? OperationalRecordId(ReportingAuditFact row) =>
        Guid.TryParse(JsonValue(row, "operationalRecordId"), out Guid id) ? id : null;

    // JSON_VALUE semantics: a scalar at the top-level path, otherwise null; malformed JSON is null.
    private static string? JsonValue(ReportingAuditFact row, string property)
    {
        if (string.IsNullOrWhiteSpace(row.DetailsJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(row.DetailsJson) is JsonObject details && details[property] is JsonValue value
                ? value.ToString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
