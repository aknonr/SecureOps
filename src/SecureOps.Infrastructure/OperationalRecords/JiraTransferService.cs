using Microsoft.Extensions.Logging;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Coordinates idempotent Jira creation and resumable source-record close/update.</summary>
public sealed class JiraTransferService : IJiraTransferService
{
    private readonly IOperationalRecordRepository _repository;
    private readonly IJiraIssueDraftService _draftService;
    private readonly IJiraClient _jiraClient;
    private readonly IOperationalRecordClient _sourceClient;
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<JiraTransferService> _logger;

    /// <summary>Initializes the transfer service.</summary>
    public JiraTransferService(
        IOperationalRecordRepository repository,
        IJiraIssueDraftService draftService,
        IJiraClient jiraClient,
        IOperationalRecordClient sourceClient,
        IAuditWriter auditWriter,
        ILogger<JiraTransferService> logger)
    {
        _repository = repository;
        _draftService = draftService;
        _jiraClient = jiraClient;
        _sourceClient = sourceClient;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationalRecordResult<JiraIssueDraft>> PreviewAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken)
    {
        OperationalRecord? record = await _repository.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false);
        }

        OperationalRecordResult<JiraIssueDraft> draftResult = await _draftService.BuildAsync(record, cancellationToken);
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
    public async Task<OperationalRecordResult<OperationalRecord>> CreateAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken)
    {
        OperationalRecord? record = await _repository.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false);
        }

        OperationalRecordResult<JiraIssueDraft> draftResult = await _draftService.BuildAsync(record, cancellationToken);
        if (!draftResult.IsSuccess)
        {
            return new OperationalRecordResult<OperationalRecord>(null, draftResult.Failure);
        }

        return await CreateFromDraftAsync(draftResult.Value!, context, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationalRecordResult<OperationalRecord>> RetryAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken)
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
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.WorkflowConflict, "jira-reconciliation", false);
        }

        if (!string.IsNullOrWhiteSpace(record.JiraIssueKey))
        {
            return await CloseOperationalRecordAsync(record, context, cancellationToken);
        }

        if (record.WorkflowState != OperationalRecordWorkflowState.JiraCreateFailed)
        {
            return OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordInvalidState, "workflow", false);
        }

        OperationalRecordResult<JiraIssueDraft> draftResult = await _draftService.BuildAsync(record, cancellationToken);
        return draftResult.IsSuccess
            ? await CreateFromDraftAsync(draftResult.Value!, context, cancellationToken)
            : new OperationalRecordResult<OperationalRecord>(null, draftResult.Failure);
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

    private static OperationalRecordFailure? MapAcquireFailure(WorkflowAcquireDisposition disposition) => disposition switch
    {
        WorkflowAcquireDisposition.Acquired => null,
        WorkflowAcquireDisposition.NotFound => new(OperationalErrorCodes.OperationalRecordNotFound, "repository", false),
        WorkflowAcquireDisposition.InvalidState => new(OperationalErrorCodes.OperationalRecordInvalidState, "workflow", false),
        WorkflowAcquireDisposition.Conflict => new(OperationalErrorCodes.WorkflowConflict, "workflow", true),
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
