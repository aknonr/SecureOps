using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Concurrency-safe local repository used only when durable SQL persistence is not selected.</summary>
public sealed class InMemoryOperationalRecordRepository : IOperationalRecordRepository
{
    /// <inheritdoc />
    public async Task<OperationalRecord> EvaluateAsync(Guid id, SdmEvaluationInput input, OperationalRecordCommandContext context,
        SecureOps.Infrastructure.Audit.IAuditWriter auditWriter, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            OperationalRecord current = Required(id);
            OperationalRecord evaluated = SdmEvaluationEvidence.Apply(current, input, _timeProvider.GetUtcNow());
            if (ReferenceEquals(current, evaluated))
            {
                return current;
            }

            await auditWriter.WriteAsync(SdmEvaluationEvidence.Audit(evaluated, context), cancellationToken);
            _records[id] = evaluated;
            return evaluated;
        }
        finally { _gate.Release(); }
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, OperationalRecord> _records = [];
    private readonly Dictionary<string, Guid> _sourceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the local repository with an injectable clock.</summary>
    public InMemoryOperationalRecordRepository(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (_sourceIds.TryGetValue(sourceItem.SourceRecordId, out Guid existingId))
            {
                OperationalRecord existing = _records[existingId];
                if (!string.IsNullOrWhiteSpace(existing.JiraIssueKey)
                    || existing.WorkflowState is OperationalRecordWorkflowState.CreatingJira or OperationalRecordWorkflowState.ClosingOperationalRecord)
                {
                    return existing;
                }

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
                    SourceConcurrencyToken = OperationalRecordSourceConcurrency.Create(sourceItem),
                    CorrelationId = correlationId,
                    UpdatedAt = now,
                    Version = existing.Version + 1
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
                ,
                SourceConcurrencyToken = OperationalRecordSourceConcurrency.Create(sourceItem),
                LastSourceValidationAt = now,
                Version = 1
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
            if (!CanApplyClassification(current))
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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
    public Task<WorkflowClaimResult> TryClaimAsync(Guid id, string actor, TimeSpan leaseDuration, string correlationId, CancellationToken cancellationToken) =>
        ClaimTransitionAsync(id, cancellationToken, current =>
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
            {
                return new WorkflowClaimResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
            }

            if (current.WorkflowState == OperationalRecordWorkflowState.CreatingJira)
            {
                return new WorkflowClaimResult(WorkflowAcquireDisposition.InProgress, current);
            }

            if (current.ClaimExpiresAt > now && !string.Equals(current.ClaimedBy, actor, StringComparison.OrdinalIgnoreCase))
            {
                return new WorkflowClaimResult(WorkflowAcquireDisposition.AlreadyClaimed, current);
            }

            OperationalRecord claimed = current with
            {
                ClaimedBy = actor,
                ClaimedAt = now,
                ClaimExpiresAt = now.Add(leaseDuration),
                CorrelationId = correlationId,
                UpdatedAt = now,
                Version = current.Version + 1
            };
            _records[id] = claimed;
            return new WorkflowClaimResult(WorkflowAcquireDisposition.Acquired, claimed);
        });

    /// <inheritdoc />
    public async Task<OperationalRecord?> ReleaseClaimAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_records.TryGetValue(id, out OperationalRecord? current))
            {
                return null;
            }

            if (!string.Equals(current.ClaimedBy, actor, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            OperationalRecord released = current with
            {
                ClaimedBy = null,
                ClaimedAt = null,
                ClaimExpiresAt = null,
                CorrelationId = correlationId,
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
            };
            _records[id] = released;
            return released;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordSourceValidationAsync(Guid id, DateTimeOffset validatedAt, string correlationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            OperationalRecord current = Required(id);
            OperationalRecord updated = current with
            {
                LastSourceValidationAt = validatedAt,
                CorrelationId = correlationId,
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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

            if (!ClaimOwnedBy(current, actor))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyClaimed, current);
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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

            if (!ClaimOwnedBy(current, actor))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyClaimed, current);
            }

            if (current.WorkflowState == OperationalRecordWorkflowState.ClosingOperationalRecord)
            {
                return ClaimOwnedBy(current, actor)
                    ? new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, current)
                    : new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = current.Version + 1
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

    private async Task<WorkflowClaimResult> ClaimTransitionAsync(
        Guid id,
        CancellationToken cancellationToken,
        Func<OperationalRecord, WorkflowClaimResult> transition)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _records.TryGetValue(id, out OperationalRecord? current)
                ? transition(current)
                : new WorkflowClaimResult(WorkflowAcquireDisposition.NotFound, null);
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

    private static bool CanApplyClassification(OperationalRecord record) =>
        string.IsNullOrWhiteSpace(record.JiraIssueKey)
        && !record.ReconciliationRequired
        && record.WorkflowState is OperationalRecordWorkflowState.Imported
            or OperationalRecordWorkflowState.Classified
            or OperationalRecordWorkflowState.NeedsManualReview
            or OperationalRecordWorkflowState.Eligible;

    private bool ClaimOwnedBy(OperationalRecord record, string actor) =>
        string.Equals(record.ClaimedBy, actor, StringComparison.OrdinalIgnoreCase)
        && record.ClaimExpiresAt > _timeProvider.GetUtcNow();
}
