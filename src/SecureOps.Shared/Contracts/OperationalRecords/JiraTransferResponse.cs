using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Current durable result of a Jira transfer command.</summary>
public sealed record JiraTransferResponse(
    Guid OperationalRecordId,
    string OrCode,
    OperationalRecordWorkflowState WorkflowState,
    string? JiraIssueKey,
    string MappingVersion,
    string IdempotencyKey,
    int RetryCount,
    string CorrelationId);
