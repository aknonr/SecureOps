using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Durable, concurrency-safe operational workflow persistence boundary.</summary>
public interface IOperationalRecordRepository
{
    /// <summary>Reads a bounded persisted page without external calls or writes.</summary>
    public Task<OperationalRecordPage> BrowseAsync(SecureOps.Shared.Contracts.OperationalRecords.OperationalRecordQuery query, bool excludeSynthetic, CancellationToken cancellationToken);
    /// <summary>Serializes evaluation with workflow state and records safe evidence only when input changes.</summary>
    public Task<OperationalRecord> EvaluateAsync(Guid id, SdmEvaluationInput input, OperationalRecordCommandContext context,
        SecureOps.Infrastructure.Audit.IAuditWriter auditWriter, CancellationToken cancellationToken);
    /// <summary>Lists persisted records.</summary>
    public Task<IReadOnlyList<OperationalRecord>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Gets one persisted record.</summary>
    public Task<OperationalRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Imports or refreshes source fields without overwriting workflow progress.</summary>
    public Task<OperationalRecord> UpsertImportedAsync(OperationalRecordSourceItem sourceItem, string correlationId, CancellationToken cancellationToken);
    /// <summary>Persists deterministic classification.</summary>
    public Task<OperationalRecord> SetClassificationAsync(Guid id, OperationalRecordClassificationResult classification, string correlationId, CancellationToken cancellationToken);
    /// <summary>Atomically acquires or renews a bounded actor lease.</summary>
    public Task<WorkflowClaimResult> TryClaimAsync(Guid id, string actor, TimeSpan leaseDuration, string correlationId, CancellationToken cancellationToken);
    /// <summary>Releases a lease only when held by the actor.</summary>
    public Task<OperationalRecord?> ReleaseClaimAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken);
    /// <summary>Persists a successful external-source freshness check.</summary>
    public Task<OperationalRecord> RecordSourceValidationAsync(Guid id, DateTimeOffset validatedAt, string correlationId, CancellationToken cancellationToken);
    /// <summary>Transitions an eligible record to Previewed.</summary>
    public Task<WorkflowAcquireResult> MarkPreviewedAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken);
    /// <summary>Atomically acquires Jira-create ownership.</summary>
    public Task<WorkflowAcquireResult> TryAcquireCreateAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken, bool sourceCloseRequested = false);
    /// <summary>Persists a confirmed Jira issue key before any source update.</summary>
    public Task<OperationalRecord> RecordJiraCreatedAsync(Guid id, string issueKey, string actor, string correlationId, CancellationToken cancellationToken);
    /// <summary>Atomically acquires source-close ownership.</summary>
    public Task<WorkflowAcquireResult> TryAcquireCloseAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken);
    /// <summary>Persists successful completion.</summary>
    public Task<OperationalRecord> RecordCompletedAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken);
    /// <summary>Persists a safe failure state.</summary>
    public Task<OperationalRecord> RecordFailureAsync(Guid id, WorkflowFailureStage stage, string errorCode, bool reconciliationRequired, string actor, string correlationId, CancellationToken cancellationToken);
    /// <summary>Increments retry metadata and returns the current workflow.</summary>
    public Task<OperationalRecord?> RecordRetryRequestedAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken);
}

/// <summary>Atomic workflow acquisition disposition.</summary>
public enum WorkflowAcquireDisposition
{
    /// <summary>The caller acquired the requested stage.</summary>
    Acquired,
    /// <summary>The record was not found.</summary>
    NotFound,
    /// <summary>The current state does not permit the stage.</summary>
    InvalidState,
    /// <summary>Another request owns a workflow stage.</summary>
    Conflict,
    /// <summary>A Jira issue key is already persisted.</summary>
    JiraAlreadyCreated,
    /// <summary>The workflow already completed.</summary>
    AlreadyCompleted,
    /// <summary>An uncertain Jira outcome requires manual reconciliation.</summary>
    ReconciliationRequired,
    /// <summary>Another actor owns an unexpired workflow lease.</summary>
    AlreadyClaimed,
    /// <summary>A non-resumable external workflow stage is already active.</summary>
    InProgress
}

/// <summary>Result of an atomic repository transition.</summary>
public sealed record WorkflowAcquireResult(WorkflowAcquireDisposition Disposition, OperationalRecord? Record);

/// <summary>Atomic claim result.</summary>
public sealed record WorkflowClaimResult(WorkflowAcquireDisposition Disposition, OperationalRecord? Record);

/// <summary>Workflow failure stage persisted by the repository.</summary>
public enum WorkflowFailureStage
{
    /// <summary>Jira creation stage.</summary>
    JiraCreate,
    /// <summary>Operational-record close/update stage.</summary>
    OperationalRecordClose
}
