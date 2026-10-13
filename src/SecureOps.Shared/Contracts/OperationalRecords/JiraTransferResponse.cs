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
    string CorrelationId,
    bool SimulationMode = false,
    string? SimulationNotice = null,
    bool SourceCloseRequested = false)
{
    /// <summary>
    /// True when the same command had already completed: the stored result is returned and this
    /// request made no Jira or source call.
    /// </summary>
    public bool Replayed { get; init; }
    /// <summary>Absolute HTTPS issue address from server configuration (Corporate only); null when not configured.</summary>
    public string? JiraIssueUrl { get; init; }
}

/// <summary>
/// Read-only view of what a Jira-create retry would send. <see cref="Draft"/> is present only when the
/// rebuilt draft has the persisted reviewed fingerprint; otherwise the server will refuse the retry.
/// </summary>
public sealed record JiraRetryPreviewResponse(
    Guid OperationalRecordId,
    string OrCode,
    string PersistedMappingVersion,
    string PersistedIdempotencyKey,
    bool MatchesPersistedDraft,
    JiraPreviewResponse? Draft);
