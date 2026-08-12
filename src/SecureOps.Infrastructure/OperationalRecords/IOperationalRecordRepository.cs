using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Durable, concurrency-safe operational workflow persistence boundary.</summary>
public interface IOperationalRecordRepository
{
    /// <summary>Lists persisted records.</summary>
    public Task<IReadOnlyList<OperationalRecord>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Gets one persisted record.</summary>
    public Task<OperationalRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Imports or refreshes source fields without overwriting workflow progress.</summary>
    public Task<OperationalRecord> UpsertImportedAsync(OperationalRecordSourceItem sourceItem, string correlationId, CancellationToken cancellationToken);
    /// <summary>Persists deterministic classification.</summary>
    public Task<OperationalRecord> SetClassificationAsync(Guid id, OperationalRecordClassificationResult classification, string correlationId, CancellationToken cancellationToken);
    /// <summary>Transitions an eligible record to Previewed.</summary>
    public Task<WorkflowAcquireResult> MarkPreviewedAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken);
    /// <summary>Atomically acquires Jira-create ownership.</summary>
    public Task<WorkflowAcquireResult> TryAcquireCreateAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken);
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
    ReconciliationRequired
}

/// <summary>Result of an atomic repository transition.</summary>
public sealed record WorkflowAcquireResult(WorkflowAcquireDisposition Disposition, OperationalRecord? Record);

/// <summary>Workflow failure stage persisted by the repository.</summary>
public enum WorkflowFailureStage
{
    /// <summary>Jira creation stage.</summary>
    JiraCreate,
    /// <summary>Operational-record close/update stage.</summary>
    OperationalRecordClose
}
