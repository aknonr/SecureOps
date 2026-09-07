using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Imports bounded source records and applies approved classification strategies.</summary>
public sealed class OperationalRecordService : IOperationalRecordService
{
    private readonly IOperationalRecordClient _client;
    private readonly IOperationalRecordClassifier _classifier;
    private readonly IOperationalRecordRepository _repository;
    private readonly IAuditWriter _auditWriter;
    private readonly OperationalRecordsOptions _options;
    private readonly ILogger<OperationalRecordService> _logger;

    /// <inheritdoc />
    public async Task<OperationalRecordPage> BrowseAsync(SecureOps.Shared.Contracts.OperationalRecords.OperationalRecordQuery query, CancellationToken cancellationToken)
    {
        OperationalRecordPage page = await _repository.BrowseAsync(query,
            string.Equals(_options.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase), cancellationToken);
        return page with { Items = page.Items.Select(SdmEvaluationEvidence.Project).ToArray() };
    }

    /// <summary>Initializes the operational-record service.</summary>
    public OperationalRecordService(
        IOperationalRecordClient client,
        IOperationalRecordClassifier classifier,
        IOperationalRecordRepository repository,
        IAuditWriter auditWriter,
        IOptions<OperationalRecordsOptions> options,
        ILogger<OperationalRecordService> logger)
    {
        _client = client;
        _classifier = classifier;
        _repository = repository;
        _auditWriter = auditWriter;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationalRecordResult<IReadOnlyList<OperationalRecord>>> ListAsync(OperationalRecordCommandContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<OperationalRecordSourceItem> sourceItems;
        try
        {
            sourceItems = await _client.GetActiveAsync(_options.MaxImportCount, cancellationToken);
        }
        catch (ExternalIntegrationException ex)
        {
            _logger.LogError(ex, "Operational-record source query failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Fail(ex.ErrorCode, "source-query", ex.Retryable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Operational-record source query failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Fail(OperationalErrorCodes.OperationalRecordQueryFailed, "source-query", true);
        }

        bool corporateSource = string.Equals(_options.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase);
        if (sourceItems.Count > _options.MaxImportCount
            || sourceItems.Any(item => !IsValid(item) || (corporateSource && IsSynthetic(item.SourceRecordId, item.OrCode))))
        {
            return OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Fail(OperationalErrorCodes.OperationalRecordQueryFailed, "source-validation", false);
        }

        foreach (OperationalRecordSourceItem sourceItem in sourceItems
            .GroupBy(item => item.SourceRecordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            OperationalRecord imported = await _repository.UpsertImportedAsync(sourceItem, context.CorrelationId, cancellationToken);
            if (corporateSource)
            {
                try
                {
                    await _repository.EvaluateAsync(imported.Id,
                        SdmEvaluationEvidence.FromSource(sourceItem, true,
                            _options.ReadOnlyIntegrationMode || !_options.ControlledTestWritesEnabled),
                        context, _auditWriter, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("SDM evidence persistence failed. CorrelationId: {CorrelationId}", context.CorrelationId);
                    return OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
                }
                continue;
            }
            if (!await TryAuditAsync(AuditActions.OperationalRecordImported, imported, context, "Imported", null, cancellationToken))
            {
                return OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
            }

            OperationalRecordClassificationResult classification = _classifier.Classify(sourceItem);
            OperationalRecord classified = await _repository.SetClassificationAsync(imported.Id, classification, context.CorrelationId, cancellationToken);
            if (!await TryAuditAsync(AuditActions.OperationalRecordClassified, classified, context, classified.WorkflowState.ToString(), null, cancellationToken))
            {
                return OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Fail(OperationalErrorCodes.AuditStoreUnavailable, "audit", true);
            }
        }

        IReadOnlyList<OperationalRecord> records = await _repository.ListAsync(cancellationToken);
        if (corporateSource)
        {
            records = records
                .Where(record => !IsSynthetic(record.SourceRecordId, record.OrCode))
                .ToArray();
        }

        return OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Success(records.Select(SdmEvaluationEvidence.Project).ToArray());
    }

    /// <inheritdoc />
    public async Task<OperationalRecordResult<OperationalRecord>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        OperationalRecord? record = await _repository.GetAsync(id, cancellationToken);
        return record is null
            || (string.Equals(_options.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase)
                && IsSynthetic(record.SourceRecordId, record.OrCode))
            ? OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false)
            : OperationalRecordResult<OperationalRecord>.Success(SdmEvaluationEvidence.Project(record));
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
                    classification = record.Classification.ToString(),
                    jiraEligible = record.JiraEligible,
                    result,
                    errorCode
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Operational-record audit failed. Action: {Action}. CorrelationId: {CorrelationId}", action, context.CorrelationId);
            return false;
        }
    }

    private static bool IsValid(OperationalRecordSourceItem item) =>
        !string.IsNullOrWhiteSpace(item.SourceRecordId) && item.SourceRecordId.Length <= 128
        && !string.IsNullOrWhiteSpace(item.OrCode) && item.OrCode.Length <= 64
        && !string.IsNullOrWhiteSpace(item.Title) && item.Title.Length <= 500
        && item.Description.Length <= 8000
        && (item.Requester?.Length ?? 0) <= 256
        && (item.Environment?.Length ?? 0) <= 128
        && (item.ServerReference?.Length ?? 0) <= 255
        && (item.ApplicationReference?.Length ?? 0) <= 255;

    private static bool IsSynthetic(string sourceRecordId, string orCode) =>
        SdmEvaluationEvidence.IsSynthetic(sourceRecordId) || SdmEvaluationEvidence.IsSynthetic(orCode);
}
