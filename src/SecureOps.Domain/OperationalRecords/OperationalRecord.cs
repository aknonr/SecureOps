namespace SecureOps.Domain.OperationalRecords;

/// <summary>Minimal persisted operational record and its Jira workflow projection.</summary>
public sealed record OperationalRecord
{
    /// <summary>Internal stable identifier.</summary>
    public required Guid Id { get; init; }
    /// <summary>Identifier assigned by the source system.</summary>
    public required string SourceRecordId { get; init; }
    /// <summary>Operator-facing operational-record code.</summary>
    public required string OrCode { get; init; }
    /// <summary>Short source title.</summary>
    public required string Title { get; init; }
    /// <summary>Bounded source description.</summary>
    public required string Description { get; init; }
    /// <summary>Source requester identifier or display value.</summary>
    public string? Requester { get; init; }
    /// <summary>Source creation time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Optional environment reference.</summary>
    public string? Environment { get; init; }
    /// <summary>Optional server reference.</summary>
    public string? ServerReference { get; init; }
    /// <summary>Optional application reference.</summary>
    public string? ApplicationReference { get; init; }
    /// <summary>Deterministic classification.</summary>
    public required OperationalRecordClassification Classification { get; init; }
    /// <summary>Whether approved rules permit Jira creation.</summary>
    public required bool JiraEligible { get; init; }
    /// <summary>Safe reason for the eligibility decision.</summary>
    public required string EligibilityReason { get; init; }
    /// <summary>Current durable workflow state.</summary>
    public required OperationalRecordWorkflowState WorkflowState { get; init; }
    /// <summary>Jira issue key after confirmed creation.</summary>
    public string? JiraIssueKey { get; init; }
    /// <summary>Stable last failure code.</summary>
    public string? LastErrorCode { get; init; }
    /// <summary>Latest workflow correlation identifier.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Mapping version used for the Jira transfer.</summary>
    public string? MappingVersion { get; init; }
    /// <summary>Deterministic transfer idempotency key.</summary>
    public string? IdempotencyKey { get; init; }
    /// <summary>Whether an uncertain remote create outcome blocks automatic retry.</summary>
    public bool ReconciliationRequired { get; init; }
    /// <summary>Number of requested retries.</summary>
    public int RetryCount { get; init; }
    /// <summary>Last persisted update time.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
