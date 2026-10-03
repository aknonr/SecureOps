namespace SecureOps.Infrastructure.Reporting;

/// <summary>Where one reporting evidence stream is read from.</summary>
public enum ReportingSourceKind
{
    /// <summary>Durable SQL Server persistence.</summary>
    SqlServer,

    /// <summary>Process memory; lost on restart and never authoritative history.</summary>
    InMemory
}

/// <summary>The evidence sources behind one management summary.</summary>
public sealed record ReportingSources(ReportingSourceKind Audit, ReportingSourceKind Workflow)
{
    /// <summary>Both streams come from SQL Server.</summary>
    public static ReportingSources Durable { get; } = new(ReportingSourceKind.SqlServer, ReportingSourceKind.SqlServer);

    /// <summary>True when every stream survives a restart.</summary>
    public bool IsDurable => this is { Audit: ReportingSourceKind.SqlServer, Workflow: ReportingSourceKind.SqlServer };
}

/// <summary>One audit row in the shape of <c>reporting.ManagementAuditEvents</c>.</summary>
public sealed record ReportingAuditFact(
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string? CorrelationId,
    string? DetailsJson);

/// <summary>One workflow transition in the shape of <c>reporting.ManagementWorkflowEvents</c>.</summary>
public sealed record ReportingWorkflowFact(Guid OperationalRecordId, string WorkflowState, DateTimeOffset OccurredAt);

/// <summary>One Jira transfer status row in the shape of <c>reporting.ManagementOperationalStatus</c>.</summary>
public sealed record ReportingTransferFact(Guid OperationalRecordId, bool ReconciliationRequired, DateTimeOffset UpdatedAt);

/// <summary>Reads audit evidence for management reporting.</summary>
public interface IReportingAuditFacts
{
    /// <summary>Source kind, surfaced to readers of the report.</summary>
    public ReportingSourceKind Kind { get; }

    /// <summary>Returns reporting-relevant audit rows that occurred before <paramref name="toExclusive"/>.</summary>
    public Task<IReadOnlyList<ReportingAuditFact>> ReadAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken);
}

/// <summary>Reads Operational Record workflow evidence for management reporting.</summary>
public interface IReportingWorkflowFacts
{
    /// <summary>Source kind, surfaced to readers of the report.</summary>
    public ReportingSourceKind Kind { get; }

    /// <summary>Returns workflow transitions that occurred before <paramref name="toExclusive"/>.</summary>
    public Task<IReadOnlyList<ReportingWorkflowFact>> ReadHistoryAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken);

    /// <summary>Returns the current Jira transfer status rows.</summary>
    public Task<IReadOnlyList<ReportingTransferFact>> ReadTransfersAsync(CancellationToken cancellationToken);
}
