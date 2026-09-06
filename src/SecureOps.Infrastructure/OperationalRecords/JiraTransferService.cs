using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Coordinates durable, claimed, source-validated Jira workflows.</summary>
public sealed class JiraTransferService : IJiraTransferService
{
    private const string CreateCommand = "OperationalRecords.CreateJira";
    private const string RetryCommand = "OperationalRecords.Retry";
    private readonly IOperationalRecordRepository _repository;
    private readonly IJiraIssueDraftService _draftService;
    private readonly IJiraClient _jiraClient;
    private readonly IOperationalRecordClient _sourceClient;
    private readonly ICommandIdempotencyStore _commandStore;
    private readonly IAuditWriter _auditWriter;
    private readonly OperationalRecordsOptions _operationalOptions;
    private readonly CommandIdempotencyOptions _commandOptions;
    private readonly ILogger<JiraTransferService> _logger;

    /// <summary>Initializes the transfer service.</summary>
    public JiraTransferService(
        IOperationalRecordRepository repository,
        IJiraIssueDraftService draftService,
        IJiraClient jiraClient,
        IOperationalRecordClient sourceClient,
        ICommandIdempotencyStore commandStore,
        IAuditWriter auditWriter,
        IOptions<OperationalRecordsOptions> operationalOptions,
        IOptions<CommandIdempotencyOptions> commandOptions,
        ILogger<JiraTransferService> logger)
    {
        _repository = repository;
        _draftService = draftService;
        _jiraClient = jiraClient;
        _sourceClient = sourceClient;
        _commandStore = commandStore;
        _auditWriter = auditWriter;
        _operationalOptions = operationalOptions.Value;
        _commandOptions = commandOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationalRecordResult<JiraIssueDraft>> PreviewAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken)
    {
        OperationalRecord? record = await _repository.GetAsync(id, cancellationToken);
        if (record is null || IsSyntheticCorporateRecord(record))
        {
            return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false);
        }

        OperationalRecordResult<JiraIssueDraft> draftResult = await _draftService.BuildAsync(record, context.Actor, cancellationToken);
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        JiraIssueDraft draft = draftResult.Value!;
        WorkflowAcquireResult transition = await _repository.MarkPreviewedAsync(id, draft.MappingVersion, draft.IdempotencyKey, context.Actor, context.CorrelationId, cancellationToken);
        OperationalRecordFailure? failure = MapAcquireFailure(transition.Disposition);
        if (failure is not null)
        {
            return new OperationalRecordResult<JiraIssueDraft>(null, failure);
        }

        if (!await TryAuditAsync(AuditActions.JiraPreviewGenerated, transition.Record!, context, "Previewed", null, cancellationToken))
        {
            return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
        }

        return OperationalRecordResult<JiraIssueDraft>.Success(draft);
    }

    /// <inheritdoc />
    public Task<OperationalRecordResult<OperationalRecord>> CreateAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
        _operationalOptions.ReadOnlyIntegrationMode
            ? Task.FromResult(ExternalWritesDisabled())
            : ExecuteCommandAsync(CreateCommand, id, context, ExecuteCreateClaimedAsync, cancellationToken);

    /// <inheritdoc />
    public Task<OperationalRecordResult<OperationalRecord>> RetryAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
        _operationalOptions.ReadOnlyIntegrationMode
            ? Task.FromResult(ExternalWritesDisabled())
            : ExecuteCommandAsync(RetryCommand, id, context, ExecuteRetryClaimedAsync, cancellationToken);

    private static OperationalRecordResult<OperationalRecord> ExternalWritesDisabled() =>
        OperationalRecordResult<OperationalRecord>.Fail(
            OperationalErrorCodes.ExternalWritesDisabled,
            "external-write-fence",
            false);

    private async Task<OperationalRecordResult<OperationalRecord>> ExecuteCommandAsync(
        string commandName,
        Guid id,
        OperationalRecordCommandContext context,
        Func<Guid, OperationalRecordCommandContext, CancellationToken, Task<OperationalRecordResult<OperationalRecord>>> operation,
        CancellationToken cancellationToken)
    {
        OperationalRecord? targetRecord = await _repository.GetAsync(id, cancellationToken);
        if (targetRecord is null || IsSyntheticCorporateRecord(targetRecord))
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false);
        }

        string targetId = id.ToString("D");
        string? key = context.IdempotencyKey;
        if (key is null && string.Equals(commandName, RetryCommand, StringComparison.Ordinal))
        {
            key = CommandIdempotency.Create(context.Actor, commandName, $"{targetId}:{targetRecord.Version}");
        }

        key ??= CommandIdempotency.Create(context.Actor, commandName, targetId);
        if (!CommandIdempotency.IsValid(key, _commandOptions.MaxKeyLength))
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.InvalidIdempotencyKey, "idempotency", false);
        }

        CommandBeginResult begin = await _commandStore.TryBeginAsync(
            commandName,
            targetId,
            key,
            context.Actor,
            TimeSpan.FromSeconds(_commandOptions.ExecutionLeaseSeconds),
            cancellationToken);
        if (begin.Disposition == CommandBeginDisposition.Completed)
        {
            OperationalRecord? completed = await _repository.GetAsync(id, cancellationToken);
            if (completed is not null
                && string.Equals(commandName, CreateCommand, StringComparison.Ordinal)
                && !await TryAuditAsync(
                    AuditActions.JiraDuplicateCreatePrevented,
                    completed,
                    context,
                    "IdempotentReplay",
                    null,
                    cancellationToken))
            {
                return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
            }

            return completed is null
                ? OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false)
                : OperationalRecordResult<OperationalRecord>.Success(completed);
        }

        if (begin.Disposition == CommandBeginDisposition.Failed)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(begin.ErrorCode ?? OperationalErrorCodes.WorkflowConflict, "idempotency", false);
        }

        if (begin.Disposition is CommandBeginDisposition.InProgress or CommandBeginDisposition.ActorConflict)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.WorkflowAlreadyInProgress, "idempotency", true);
        }

        Guid executionToken = begin.ExecutionToken
            ?? throw new InvalidOperationException("An acquired command execution must include a fencing token.");
        OperationalRecordResult<OperationalRecord> result;
        try
        {
            result = await ExecuteClaimedAsync(id, context, operation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await _commandStore.FailAsync(commandName, targetId, key, executionToken, OperationalErrorCodes.WorkflowConflict, CancellationToken.None);
            throw;
        }

        if (result.IsSuccess)
        {
            await _commandStore.CompleteAsync(commandName, targetId, key, executionToken, cancellationToken);
        }
        else
        {
            await _commandStore.FailAsync(commandName, targetId, key, executionToken, result.Failure!.Code, cancellationToken);
        }

        return result;
    }

    private bool IsSyntheticCorporateRecord(OperationalRecord record) =>
        string.Equals(_operationalOptions.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase)
        && (SdmEvaluationEvidence.IsSynthetic(record.SourceRecordId) || SdmEvaluationEvidence.IsSynthetic(record.OrCode));

    private async Task<OperationalRecordResult<OperationalRecord>> ExecuteClaimedAsync(
        Guid id,
        OperationalRecordCommandContext context,
        Func<Guid, OperationalRecordCommandContext, CancellationToken, Task<OperationalRecordResult<OperationalRecord>>> operation,
        CancellationToken cancellationToken)
    {
        WorkflowClaimResult claim = await _repository.TryClaimAsync(
            id,
            context.Actor,
            TimeSpan.FromSeconds(_operationalOptions.ClaimLeaseSeconds),
            context.CorrelationId,
            cancellationToken);
        if (claim.Disposition != WorkflowAcquireDisposition.Acquired)
        {
            if (claim.Record is not null)
            {
                string action = claim.Disposition == WorkflowAcquireDisposition.JiraAlreadyCreated
                    ? AuditActions.JiraDuplicateCreatePrevented
                    : AuditActions.OperationalRecordConflict;
                _ = await TryAuditAsync(action, claim.Record, context, claim.Disposition.ToString(), null, cancellationToken);
            }

            return new OperationalRecordResult<OperationalRecord>(null, MapClaimFailure(claim.Disposition));
        }

        if (!await TryAuditAsync(AuditActions.OperationalRecordClaimed, claim.Record!, context, "Claimed", null, cancellationToken))
        {
            await _repository.ReleaseClaimAsync(id, context.Actor, context.CorrelationId, CancellationToken.None);
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
        }

        OperationalRecordResult<OperationalRecord> result;
        try
        {
            result = await operation(id, context, cancellationToken);
        }
        finally
        {
            OperationalRecord? released = await _repository.ReleaseClaimAsync(id, context.Actor, context.CorrelationId, CancellationToken.None);
            if (released is not null)
            {
                _ = await TryAuditAsync(AuditActions.OperationalRecordClaimReleased, released, context, "Released", null, CancellationToken.None);
            }
        }

        if (result.IsSuccess)
        {
            OperationalRecord? latest = await _repository.GetAsync(id, cancellationToken);
            return latest is null ? result : OperationalRecordResult<OperationalRecord>.Success(latest);
        }

        return result;
    }

    private async Task<OperationalRecordResult<OperationalRecord>> ExecuteCreateClaimedAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken)
    {
        OperationalRecord? record = await _repository.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false);
        }

        OperationalRecordResult<JiraIssueDraft> draftResult = await _draftService.BuildAsync(record, context.Actor, cancellationToken);
        if (!draftResult.IsSuccess)
        {
            return new OperationalRecordResult<OperationalRecord>(null, draftResult.Failure);
        }

        OperationalRecordResult<OperationalRecord> freshness = await ValidateFreshnessAsync(record, WorkflowFailureStage.JiraCreate, context, cancellationToken);
        return freshness.IsSuccess
            ? await CreateFromDraftAsync(draftResult.Value!, context, cancellationToken)
            : freshness;
    }

    private async Task<OperationalRecordResult<OperationalRecord>> ExecuteRetryClaimedAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken)
    {
        OperationalRecord? record = await _repository.RecordRetryRequestedAsync(id, context.Actor, context.CorrelationId, cancellationToken);
        if (record is null)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false);
        }

        if (!await TryAuditAsync(AuditActions.WorkflowRetried, record, context, "RetryRequested", record.LastErrorCode, cancellationToken))
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
        }

        if (record.WorkflowState == OperationalRecordWorkflowState.Completed)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.WorkflowAlreadyCompleted, "workflow", false);
        }

        if (record.ReconciliationRequired || record.WorkflowState == OperationalRecordWorkflowState.CreatingJira)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.WorkflowAlreadyInProgress, "jira-reconciliation", false);
        }

        if (!string.IsNullOrWhiteSpace(record.JiraIssueKey))
        {
            return await CloseOperationalRecordAsync(record, context, cancellationToken);
        }

        if (record.WorkflowState != OperationalRecordWorkflowState.JiraCreateFailed)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordInvalidState, "workflow", false);
        }

        OperationalRecordResult<JiraIssueDraft> draftResult = await _draftService.BuildAsync(record, context.Actor, cancellationToken);
        if (!draftResult.IsSuccess)
        {
            return new OperationalRecordResult<OperationalRecord>(null, draftResult.Failure);
        }

        OperationalRecordResult<OperationalRecord> freshness = await ValidateFreshnessAsync(record, WorkflowFailureStage.JiraCreate, context, cancellationToken);
        return freshness.IsSuccess
            ? await CreateFromDraftAsync(draftResult.Value!, context, cancellationToken)
            : freshness;
    }

    private async Task<OperationalRecordResult<OperationalRecord>> CreateFromDraftAsync(
        JiraIssueDraft draft,
        OperationalRecordCommandContext context,
        CancellationToken cancellationToken)
    {
        WorkflowAcquireResult acquired = await _repository.TryAcquireCreateAsync(
            draft.OperationalRecordId,
            draft.MappingVersion,
            draft.IdempotencyKey,
            context.Actor,
            context.CorrelationId,
            cancellationToken);
        OperationalRecordFailure? acquireFailure = MapAcquireFailure(acquired.Disposition);
        if (acquireFailure is not null)
        {
            return new OperationalRecordResult<OperationalRecord>(null, acquireFailure);
        }

        OperationalRecord creating = acquired.Record!;
        if (!await TryAuditAsync(AuditActions.JiraCreateRequested, creating, context, "CreateRequested", null, cancellationToken))
        {
            await _repository.RecordFailureAsync(creating.Id, WorkflowFailureStage.JiraCreate, OperationalErrorCodes.AuditStoreUnavailable, false, context.Actor, context.CorrelationId, cancellationToken);
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
        }

        JiraIssueCreationResult created;
        try
        {
            created = await _jiraClient.CreateIssueAsync(draft, cancellationToken);
        }
        catch (ExternalIntegrationException ex)
        {
            return await RecordJiraFailureAsync(creating, ex.ErrorCode, ex.Retryable, ex.OutcomeUnknown, context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Jira creation failed. OperationalRecordId: {OperationalRecordId}. CorrelationId: {CorrelationId}", creating.Id, context.CorrelationId);
            return await RecordJiraFailureAsync(creating, OperationalErrorCodes.JiraCreateFailed, true, true, context, cancellationToken);
        }

        if (!IsValidIssueKey(created.IssueKey))
        {
            return await RecordJiraFailureAsync(creating, OperationalErrorCodes.JiraValidationFailed, false, true, context, cancellationToken);
        }

        OperationalRecord jiraCreated = await _repository.RecordJiraCreatedAsync(creating.Id, created.IssueKey, context.Actor, context.CorrelationId, cancellationToken);
        if (!await TryAuditAsync(AuditActions.JiraCreated, jiraCreated, context, "Created", null, cancellationToken))
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
        }

        return await CloseOperationalRecordAsync(jiraCreated, context, cancellationToken);
    }

    private async Task<OperationalRecordResult<OperationalRecord>> CloseOperationalRecordAsync(
        OperationalRecord record,
        OperationalRecordCommandContext context,
        CancellationToken cancellationToken)
    {
        OperationalRecordResult<OperationalRecord> freshness = await ValidateFreshnessAsync(record, WorkflowFailureStage.OperationalRecordClose, context, cancellationToken);
        if (!freshness.IsSuccess)
        {
            return freshness;
        }

        WorkflowAcquireResult acquired = await _repository.TryAcquireCloseAsync(record.Id, context.Actor, context.CorrelationId, cancellationToken);
        if (acquired.Disposition == WorkflowAcquireDisposition.AlreadyCompleted)
        {
            return OperationalRecordResult<OperationalRecord>.Success(acquired.Record!);
        }

        OperationalRecordFailure? acquireFailure = MapAcquireFailure(acquired.Disposition);
        if (acquireFailure is not null)
        {
            return new OperationalRecordResult<OperationalRecord>(null, acquireFailure);
        }

        OperationalRecord closing = acquired.Record!;
        if (!await TryAuditAsync(AuditActions.OperationalRecordCloseRequested, closing, context, "CloseRequested", null, cancellationToken))
        {
            await _repository.RecordFailureAsync(closing.Id, WorkflowFailureStage.OperationalRecordClose, OperationalErrorCodes.AuditStoreUnavailable, false, context.Actor, context.CorrelationId, cancellationToken);
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
        }

        try
        {
            await _sourceClient.CloseAsync(closing.SourceRecordId, closing.OrCode, closing.JiraIssueKey!, cancellationToken);
        }
        catch (ExternalIntegrationException ex)
        {
            return await RecordCloseFailureAsync(closing, ex.ErrorCode, ex.Retryable, context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Operational-record close failed. OperationalRecordId: {OperationalRecordId}. CorrelationId: {CorrelationId}", closing.Id, context.CorrelationId);
            return await RecordCloseFailureAsync(closing, OperationalErrorCodes.OperationalRecordCloseFailed, true, context, cancellationToken);
        }

        OperationalRecord completed = await _repository.RecordCompletedAsync(closing.Id, context.Actor, context.CorrelationId, cancellationToken);
        _ = await TryAuditAsync(AuditActions.OperationalRecordClosed, completed, context, "Closed", null, cancellationToken);
        _ = await TryAuditAsync(AuditActions.WorkflowCompleted, completed, context, "Completed", null, cancellationToken);
        return OperationalRecordResult<OperationalRecord>.Success(completed);
    }

    private async Task<OperationalRecordResult<OperationalRecord>> ValidateFreshnessAsync(
        OperationalRecord record,
        WorkflowFailureStage failureStage,
        OperationalRecordCommandContext context,
        CancellationToken cancellationToken)
    {
        OperationalRecordSourceItem? current;
        try
        {
            current = await _sourceClient.GetByIdAsync(record.SourceRecordId, cancellationToken);
        }
        catch (ExternalIntegrationException ex)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(ex.ErrorCode, "source-validation", ex.Retryable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Operational-record source validation failed. OperationalRecordId: {OperationalRecordId}. CorrelationId: {CorrelationId}", record.Id, context.CorrelationId);
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalSourceUnavailable, "source-validation", true);
        }

        string? errorCode = current is null || !current.IsOpen
            ? OperationalErrorCodes.OperationalRecordNoLongerOpen
            : !string.Equals(OperationalRecordSourceConcurrency.Create(current), record.SourceConcurrencyToken, StringComparison.Ordinal)
                ? OperationalErrorCodes.OperationalRecordChanged
                : null;
        if (errorCode is not null)
        {
            OperationalRecord failed = await _repository.RecordFailureAsync(record.Id, failureStage, errorCode, false, context.Actor, context.CorrelationId, cancellationToken);
            _ = await TryAuditAsync(AuditActions.OperationalRecordSourceChanged, failed, context, "Rejected", errorCode, cancellationToken);
            return OperationalRecordResult<OperationalRecord>.Fail(errorCode, "source-validation", false);
        }

        OperationalRecord validated = await _repository.RecordSourceValidationAsync(record.Id, DateTimeOffset.UtcNow, context.CorrelationId, cancellationToken);
        return OperationalRecordResult<OperationalRecord>.Success(validated);
    }

    private async Task<OperationalRecordResult<OperationalRecord>> RecordJiraFailureAsync(
        OperationalRecord record,
        string errorCode,
        bool retryable,
        bool reconciliationRequired,
        OperationalRecordCommandContext context,
        CancellationToken cancellationToken)
    {
        OperationalRecord failed = await _repository.RecordFailureAsync(record.Id, WorkflowFailureStage.JiraCreate, errorCode, reconciliationRequired, context.Actor, context.CorrelationId, cancellationToken);
        _ = await TryAuditAsync(AuditActions.JiraCreateFailed, failed, context, "Failed", errorCode, cancellationToken);
        return OperationalRecordResult<OperationalRecord>.Fail(errorCode, reconciliationRequired ? "jira-reconciliation" : "jira-create", retryable && !reconciliationRequired);
    }

    private async Task<OperationalRecordResult<OperationalRecord>> RecordCloseFailureAsync(
        OperationalRecord record,
        string errorCode,
        bool retryable,
        OperationalRecordCommandContext context,
        CancellationToken cancellationToken)
    {
        OperationalRecord failed = await _repository.RecordFailureAsync(record.Id, WorkflowFailureStage.OperationalRecordClose, errorCode, false, context.Actor, context.CorrelationId, cancellationToken);
        _ = await TryAuditAsync(AuditActions.OperationalRecordCloseFailed, failed, context, "Failed", errorCode, cancellationToken);
        return OperationalRecordResult<OperationalRecord>.Fail(errorCode, "source-close", retryable);
    }

    private async Task<bool> TryAuditAsync(
        string action,
        OperationalRecord record,
        OperationalRecordCommandContext context,
        string result,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditWriter.WriteAsync(new AuditEvent
            {
                Actor = context.Actor,
                Action = action,
                CorrelationId = context.CorrelationId,
                SourceIp = context.SourceIp,
                Details = new
                {
                    operationalRecordId = record.Id,
                    sourceRecordId = record.SourceRecordId,
                    orCode = record.OrCode,
                    jiraIssueKey = record.JiraIssueKey,
                    workflowState = record.WorkflowState.ToString(),
                    result,
                    errorCode
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Jira workflow audit failed. Action: {Action}. CorrelationId: {CorrelationId}", action, context.CorrelationId);
            return false;
        }
    }

    private static OperationalRecordFailure MapClaimFailure(WorkflowAcquireDisposition disposition) => disposition switch
    {
        WorkflowAcquireDisposition.NotFound => new(OperationalErrorCodes.OperationalRecordNotFound, "repository", false),
        WorkflowAcquireDisposition.AlreadyCompleted => new(OperationalErrorCodes.WorkflowAlreadyCompleted, "workflow", false),
        WorkflowAcquireDisposition.AlreadyClaimed => new(OperationalErrorCodes.OperationalRecordAlreadyClaimed, "claim", true),
        WorkflowAcquireDisposition.InProgress or WorkflowAcquireDisposition.Conflict => new(OperationalErrorCodes.WorkflowAlreadyInProgress, "claim", true),
        _ => new(OperationalErrorCodes.WorkflowConflict, "claim", false)
    };

    private static OperationalRecordFailure? MapAcquireFailure(WorkflowAcquireDisposition disposition) => disposition switch
    {
        WorkflowAcquireDisposition.Acquired => null,
        WorkflowAcquireDisposition.NotFound => new(OperationalErrorCodes.OperationalRecordNotFound, "repository", false),
        WorkflowAcquireDisposition.InvalidState => new(OperationalErrorCodes.OperationalRecordInvalidState, "workflow", false),
        WorkflowAcquireDisposition.Conflict or WorkflowAcquireDisposition.InProgress => new(OperationalErrorCodes.WorkflowAlreadyInProgress, "workflow", true),
        WorkflowAcquireDisposition.AlreadyClaimed => new(OperationalErrorCodes.OperationalRecordAlreadyClaimed, "claim", true),
        WorkflowAcquireDisposition.JiraAlreadyCreated => new(OperationalErrorCodes.JiraAlreadyCreated, "jira-create", false),
        WorkflowAcquireDisposition.AlreadyCompleted => new(OperationalErrorCodes.WorkflowAlreadyCompleted, "workflow", false),
        WorkflowAcquireDisposition.ReconciliationRequired => new(OperationalErrorCodes.WorkflowConflict, "jira-reconciliation", false),
        _ => new(OperationalErrorCodes.WorkflowConflict, "workflow", false)
    };

    private static bool IsValidIssueKey(string issueKey)
    {
        if (string.IsNullOrWhiteSpace(issueKey) || issueKey.Length > 64)
        {
            return false;
        }

        int separator = issueKey.LastIndexOf('-');
        return separator > 0
            && separator < issueKey.Length - 1
            && issueKey[..separator].All(character => char.IsAsciiLetterUpper(character) || char.IsDigit(character))
            && char.IsAsciiLetterUpper(issueKey[0])
            && issueKey[(separator + 1)..].All(char.IsDigit);
    }
}
