using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Concurrency-safe local repository used only when durable SQL persistence is not selected.</summary>
public sealed class InMemoryOperationalRecordRepository : IOperationalRecordRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, OperationalRecord> _records = [];
    private readonly Dictionary<string, Guid> _sourceIds = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationalRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _records.Values.OrderByDescending(record => record.CreatedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationalRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _records.GetValueOrDefault(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> UpsertImportedAsync(OperationalRecordSourceItem sourceItem, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (_sourceIds.TryGetValue(sourceItem.SourceRecordId, out Guid existingId))
            {
                OperationalRecord existing = _records[existingId];
                OperationalRecord refreshed = existing with
                {
                    OrCode = sourceItem.OrCode,
                    Title = sourceItem.Title,
                    Description = sourceItem.Description,
                    Requester = sourceItem.Requester,
                    CreatedAt = sourceItem.CreatedAt,
                    Environment = sourceItem.Environment,
                    ServerReference = sourceItem.ServerReference,
                    ApplicationReference = sourceItem.ApplicationReference,
                    CorrelationId = correlationId,
                    UpdatedAt = now
                };
                _records[existingId] = refreshed;
                return refreshed;
            }

            OperationalRecord imported = new()
            {
                Id = Guid.NewGuid(),
                SourceRecordId = sourceItem.SourceRecordId,
                OrCode = sourceItem.OrCode,
                Title = sourceItem.Title,
                Description = sourceItem.Description,
                Requester = sourceItem.Requester,
                CreatedAt = sourceItem.CreatedAt,
                Environment = sourceItem.Environment,
                ServerReference = sourceItem.ServerReference,
                ApplicationReference = sourceItem.ApplicationReference,
                Classification = OperationalRecordClassification.NeedsManualReview,
                JiraEligible = false,
                EligibilityReason = "Classification pending.",
                WorkflowState = OperationalRecordWorkflowState.Imported,
                CorrelationId = correlationId,
                UpdatedAt = now
            };
            _records[imported.Id] = imported;
            _sourceIds[imported.SourceRecordId] = imported.Id;
            return imported;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> SetClassificationAsync(Guid id, OperationalRecordClassificationResult classification, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            OperationalRecord current = Required(id);
            if (current.JiraIssueKey is not null)
            {
                return current;
            }

            OperationalRecord updated = current with
            {
                Classification = classification.Classification,
                JiraEligible = classification.JiraEligible,
                EligibilityReason = classification.EligibilityReason,
                WorkflowState = classification.JiraEligible
                    ? OperationalRecordWorkflowState.Eligible
                    : OperationalRecordWorkflowState.NeedsManualReview,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<WorkflowAcquireResult> MarkPreviewedAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken) =>
        TransitionAsync(id, cancellationToken, current =>
        {
            if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
            }

            if (!current.JiraEligible || current.WorkflowState is not (OperationalRecordWorkflowState.Eligible or OperationalRecordWorkflowState.Previewed or OperationalRecordWorkflowState.JiraCreateFailed))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            if (!MappingMatches(current, mappingVersion, idempotencyKey))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            OperationalRecord updated = current with
            {
                WorkflowState = OperationalRecordWorkflowState.Previewed,
                MappingVersion = mappingVersion,
                IdempotencyKey = idempotencyKey,
                LastErrorCode = null,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, updated);
        });

    /// <inheritdoc />
    public Task<WorkflowAcquireResult> TryAcquireCreateAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken) =>
        TransitionAsync(id, cancellationToken, current =>
        {
            if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
            }

            if (!string.IsNullOrWhiteSpace(current.JiraIssueKey))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.JiraAlreadyCreated, current);
            }

            if (current.ReconciliationRequired)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.ReconciliationRequired, current);
            }

            if (current.WorkflowState is OperationalRecordWorkflowState.CreateRequested or OperationalRecordWorkflowState.CreatingJira or OperationalRecordWorkflowState.ClosingOperationalRecord)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            if (current.WorkflowState is not (OperationalRecordWorkflowState.Previewed or OperationalRecordWorkflowState.JiraCreateFailed))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            if (!MappingMatches(current, mappingVersion, idempotencyKey))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            OperationalRecord updated = current with
            {
                WorkflowState = OperationalRecordWorkflowState.CreatingJira,
                MappingVersion = mappingVersion,
                IdempotencyKey = idempotencyKey,
                LastErrorCode = null,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, updated);
        });

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordJiraCreatedAsync(Guid id, string issueKey, string actor, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            OperationalRecord current = Required(id);
            if (!string.IsNullOrWhiteSpace(current.JiraIssueKey))
            {
                return current;
            }

            if (current.WorkflowState != OperationalRecordWorkflowState.CreatingJira)
            {
                throw new InvalidOperationException("Jira creation cannot be persisted from the current workflow state.");
            }

            OperationalRecord updated = current with
            {
                WorkflowState = OperationalRecordWorkflowState.JiraCreated,
                JiraIssueKey = issueKey,
                ReconciliationRequired = false,
                LastErrorCode = null,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<WorkflowAcquireResult> TryAcquireCloseAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken) =>
        TransitionAsync(id, cancellationToken, current =>
        {
            if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
            }

            if (string.IsNullOrWhiteSpace(current.JiraIssueKey))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            if (current.WorkflowState == OperationalRecordWorkflowState.ClosingOperationalRecord)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            if (current.WorkflowState is not (OperationalRecordWorkflowState.JiraCreated or OperationalRecordWorkflowState.OperationalRecordCloseFailed))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            OperationalRecord updated = current with
            {
                WorkflowState = OperationalRecordWorkflowState.ClosingOperationalRecord,
                LastErrorCode = null,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, updated);
        });

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordCompletedAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            OperationalRecord current = Required(id);
            if (current.WorkflowState != OperationalRecordWorkflowState.ClosingOperationalRecord)
            {
                throw new InvalidOperationException("Workflow completion cannot be persisted from the current state.");
            }

            OperationalRecord updated = current with
            {
                WorkflowState = OperationalRecordWorkflowState.Completed,
                LastErrorCode = null,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordFailureAsync(Guid id, WorkflowFailureStage stage, string errorCode, bool reconciliationRequired, string actor, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            OperationalRecord current = Required(id);
            OperationalRecord updated = current with
            {
                WorkflowState = stage == WorkflowFailureStage.JiraCreate
                    ? OperationalRecordWorkflowState.JiraCreateFailed
                    : OperationalRecordWorkflowState.OperationalRecordCloseFailed,
                LastErrorCode = errorCode,
                ReconciliationRequired = reconciliationRequired,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationalRecord?> RecordRetryRequestedAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_records.TryGetValue(id, out OperationalRecord? current))
            {
                return null;
            }

            OperationalRecord updated = current with
            {
                RetryCount = current.RetryCount + 1,
                CorrelationId = correlationId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _records[id] = updated;
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkflowAcquireResult> TransitionAsync(
        Guid id,
        CancellationToken cancellationToken,
        Func<OperationalRecord, WorkflowAcquireResult> transition)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _records.TryGetValue(id, out OperationalRecord? current)
                ? transition(current)
                : new WorkflowAcquireResult(WorkflowAcquireDisposition.NotFound, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private OperationalRecord Required(Guid id) =>
        _records.TryGetValue(id, out OperationalRecord? record)
            ? record
            : throw new KeyNotFoundException("Operational record was not found.");

    private static bool MappingMatches(OperationalRecord record, string mappingVersion, string idempotencyKey) =>
        record.MappingVersion is null
        || (string.Equals(record.MappingVersion, mappingVersion, StringComparison.Ordinal)
            && string.Equals(record.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
}
