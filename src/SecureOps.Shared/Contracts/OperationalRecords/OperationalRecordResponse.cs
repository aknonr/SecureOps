using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Safe operational-record API representation.</summary>
public sealed record OperationalRecordResponse(
    Guid Id,
    string SourceRecordId,
    string OrCode,
    string Title,
    string Description,
    string? Requester,
    DateTimeOffset? CreatedAt,
    string? Environment,
    string? ServerReference,
    string? ApplicationReference,
    OperationalRecordClassification Classification,
    bool JiraEligible,
    string EligibilityReason,
    OperationalRecordWorkflowState WorkflowState,
    string? JiraIssueKey,
    string? LastErrorCode,
    string? CorrelationId,
    int RetryCount,
    DateTimeOffset UpdatedAt,
    bool Claimed,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? ClaimExpiresAt,
    DateTimeOffset? LastSourceValidationAt,
    long Version,
    bool ReconciliationRequired,
    bool RetryEligible,
    bool JiraExists,
    string PresentationState = OperationalRecordPresentationStates.NeedsAttention,
    bool SimulationMode = false,
    string? SimulationNotice = null);

/// <summary>Stable UI presentation categories derived from durable workflow states.</summary>
public static class OperationalRecordPresentationStates
{
    /// <summary>The operator must inspect or reconcile the workflow.</summary>
    public const string NeedsAttention = "NeedsAttention";
    /// <summary>The workflow can proceed to Jira preview or create.</summary>
    public const string Actionable = "Actionable";
    /// <summary>A claimed create or source-completion stage is underway.</summary>
    public const string InProgress = "InProgress";
    /// <summary>Jira creation and source completion are confirmed.</summary>
    public const string Completed = "Completed";
}
