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
    string? SimulationNotice = null,
    bool ReadOnlyIntegrationMode = false,
    string? ReadOnlyNotice = null)
{
    /// <summary>Durable intent recorded before Jira dispatch; retry cannot enable it.</summary>
    public bool SourceCloseRequested { get; init; }
    /// <summary>Whether the current deployment permits source-close dispatch below global fences.</summary>
    public bool SourceCloseEnabled { get; init; }
    /// <summary>Separately persisted final-state evidence; old Completed stages do not imply verification.</summary>
    public bool SourceClosureVerified { get; init; }
    /// <summary>Current stored source digest for exact-version policy review; not an authorization token.</summary>
    public string? SourceFingerprint { get; init; }
    /// <summary>Nullable recommendation; null means no durable SDM evaluation.</summary>
    public OperationalRecordClassification? RecommendedClassification { get; init; }
    /// <summary>Stored policy recommendation, independent of actor capability and deployment activation.</summary>
    public bool SdmCandidateRecommended { get; init; }
    /// <summary>Nullable immutable evaluation policy identifier.</summary>
    public string? RuleSetVersion { get; init; }
    /// <summary>Unique stable reason identifiers in ordinal order; never localized.</summary>
    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
    /// <summary>Nullable UTC evidence recording time, outside the pure decision.</summary>
    public DateTimeOffset? EvaluatedAt { get; init; }
    /// <summary>True for absent or invalidated evaluation evidence.</summary>
    public bool EvaluationStale { get; init; } = true;
    /// <summary>Whether an observed source change invalidated evaluation.</summary>
    public bool SourceChanged { get; init; }
    /// <summary>Unique publication blockers in ordinal order.</summary>
    public IReadOnlyList<string> BlockingConditions { get; init; } = [];
    /// <summary>Evaluation never grants permission to perform an external write.</summary>
    public bool ExternalWriteEligible { get; init; }
}

/// <summary>Stable UI presentation categories derived from durable workflow states.</summary>
public static class OperationalRecordPresentationStates
{
    /// <summary>The operator must inspect or reconcile the workflow.</summary>
    public const string NeedsAttention = "NeedsAttention";
    /// <summary>The workflow can proceed to Jira preview or create.</summary>
    public const string Actionable = "Actionable";
    /// <summary>A claimed create or source-completion stage is underway.</summary>
    public const string InProgress = "InProgress";
    /// <summary>Jira creation is confirmed; source closing was deliberately not requested.</summary>
    public const string SourceOpen = "SourceOpen";
    /// <summary>Jira creation and source completion are confirmed.</summary>
    public const string Completed = "Completed";
}
