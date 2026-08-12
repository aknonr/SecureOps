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
    DateTimeOffset CreatedAt,
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
    DateTimeOffset? ClaimExpiresAt,
    DateTimeOffset? LastSourceValidationAt,
    long Version);
