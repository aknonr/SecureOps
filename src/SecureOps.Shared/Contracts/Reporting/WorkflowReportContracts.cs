namespace SecureOps.Shared.Contracts.Reporting;

/// <summary>Explicit UTC half-open period; current-state facts are captured separately at AsOf.</summary>
public sealed record WorkflowReportRequest(DateTimeOffset From, DateTimeOffset To,
    string TimeZone = "UTC+03:00", bool IncludeSynthetic = false);

/// <summary>Immutable fact filters applied before SQL counting and paging.</summary>
public sealed record WorkflowReportFilter(string? Module = null, string? Status = null,
    string? RecordType = null, string? Metric = null, int Page = 1, int PageSize = 25);

/// <summary>One explicitly defined counting unit, never a composite completion total.</summary>
public sealed record WorkflowMetric(string Key, string Module, string Label, string Unit,
    string TimeBasis, long Count);

/// <summary>One contributing persisted fact, with actor and reviewer kept separate.</summary>
public sealed record WorkflowFact(string Metric, string LogicalId, string Module, Guid RecordId,
    string Reference, string RecordType, string Status, string? Actor, string? Assignee,
    DateTimeOffset? OccurredAt, string? Detail);

/// <summary>Captured readiness/freshness observation; not an external business success.</summary>
public sealed record WorkflowReadiness(string Module, string State, DateTimeOffset? LastSuccess, string Reason);

/// <summary>One owner-bound retained report cut; coverage limitations are mandatory.</summary>
public sealed record WorkflowReport(Guid Id, DateTimeOffset AsOf, DateTimeOffset ExpiresAt,
    WorkflowReportRequest Request, IReadOnlyList<WorkflowMetric> Metrics,
    IReadOnlyList<WorkflowFact> Items, long Total, int Page, int PageSize,
    IReadOnlyList<string> Limitations)
{
    /// <summary>Exact filters applied to metrics, rows and workbook.</summary>
    public WorkflowReportFilter Filter { get; init; } = new();
    /// <summary>Source freshness captured with this SQL cut.</summary>
    public IReadOnlyList<WorkflowReadiness> Readiness { get; init; } = [];
}
