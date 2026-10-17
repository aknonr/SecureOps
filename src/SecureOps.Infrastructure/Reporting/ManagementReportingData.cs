namespace SecureOps.Infrastructure.Reporting;

/// <summary>Server-aggregated audit action count.</summary>
public sealed record ReportingAuditCount(DateTime BucketDate, string Action, string? DetailCode, long Count);

/// <summary>Server-aggregated distinct workflow record count.</summary>
public sealed record ReportingWorkflowCount(string WorkflowState, long Count);

/// <summary>Distinct adoption actors at fixed trailing windows.</summary>
public sealed record ReportingActiveUsers(long Daily, long Weekly, long Monthly, long SelectedWindow);

/// <summary>Retry requests and correlated outcomes.</summary>
public sealed record ReportingRetryOutcomes(long Requested, long Succeeded, long Failed);

/// <summary>Server-computed elapsed-time statistics.</summary>
public sealed record ReportingDurationStatistics(
    string Key,
    long SampleCount,
    double? MinimumSeconds,
    double? AverageSeconds,
    double? MaximumSeconds);

/// <summary>All aggregate rows needed to project a management summary.</summary>
public sealed record ManagementReportingData(
    IReadOnlyList<ReportingAuditCount> AuditCounts,
    IReadOnlyList<ReportingWorkflowCount> WorkflowCounts,
    long IdentityUniqueOperators,
    ReportingActiveUsers ActiveUsers,
    long ReconciliationRequired,
    ReportingRetryOutcomes RetryOutcomes,
    IReadOnlyList<ReportingDurationStatistics> Durations,
    DateTimeOffset? CoverageFromUtc)
{
    /// <summary>Where the evidence was read from; SQL-aggregated data is durable by construction.</summary>
    public ReportingSources Sources { get; init; } = ReportingSources.Durable;
}

/// <summary>One server-aggregated operator row.</summary>
public sealed record OperatorActivityData(
    string Actor,
    long OperationCount,
    DateTimeOffset FirstActivityAt,
    DateTimeOffset LastActivityAt,
    long IdentityOperations,
    long DirectoryOperations,
    long AccessOperations,
    long OperationalWorkflowOperations);

/// <summary>Server-side paginated operator aggregate page.</summary>
public sealed record OperatorActivityDataPage(
    long TotalItems,
    IReadOnlyList<OperatorActivityData> Items,
    DateTimeOffset? CoverageFromUtc);
