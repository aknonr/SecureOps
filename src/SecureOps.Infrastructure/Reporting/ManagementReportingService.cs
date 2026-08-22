using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>HTTP-origin context for a privileged report read.</summary>
public sealed record ManagementReportingContext(string Actor, string CorrelationId, string? SourceIp);

/// <summary>Success or stable failure from the reporting service.</summary>
public sealed record ManagementReportingResult<T>(T? Value, string? ErrorCode)
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => ErrorCode is null;

    /// <summary>Creates a successful result.</summary>
    public static ManagementReportingResult<T> Success(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    public static ManagementReportingResult<T> Fail(string errorCode) => new(default, errorCode);
}

/// <summary>Backend-authoritative management reporting use cases.</summary>
public interface IManagementReportingService
{
    /// <summary>Returns one bounded aggregate summary.</summary>
    public Task<ManagementReportingResult<ManagementReportResponse>> GetSummaryAsync(
        string? selection,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ManagementReportingContext context,
        CancellationToken cancellationToken);

    /// <summary>Returns one bounded, paginated per-operator aggregate page.</summary>
    public Task<ManagementReportingResult<OperatorActivityPageResponse>> GetOperatorsAsync(
        string? selection,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        ManagementReportingContext context,
        CancellationToken cancellationToken);
}

/// <summary>Validates, audits, and projects privileged management reports.</summary>
public sealed class ManagementReportingService : IManagementReportingService
{
    /// <summary>Maximum operator page size.</summary>
    public const int MaximumPageSize = 100;

    private readonly ReportingWindowResolver _windowResolver;
    private readonly IManagementReportingRepository _repository;
    private readonly ManagementReportProjector _projector;
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<ManagementReportingService> _logger;

    /// <summary>Initializes the reporting service.</summary>
    public ManagementReportingService(
        ReportingWindowResolver windowResolver,
        IManagementReportingRepository repository,
        ManagementReportProjector projector,
        IAuditWriter auditWriter,
        ILogger<ManagementReportingService> logger)
    {
        _windowResolver = windowResolver;
        _repository = repository;
        _projector = projector;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ManagementReportingResult<ManagementReportResponse>> GetSummaryAsync(
        string? selection,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ManagementReportingContext context,
        CancellationToken cancellationToken)
    {
        ReportingWindowResolution resolved = _windowResolver.Resolve(selection, from, to);
        if (!resolved.IsValid)
        {
            return await ValidationFailureAsync<ManagementReportResponse>("summary", context, cancellationToken);
        }

        ReportingWindow window = resolved.Window!;
        if (!await TryAuditAsync(AuditActions.ManagementReportRequested, "summary", window, context, null, cancellationToken))
        {
            return ManagementReportingResult<ManagementReportResponse>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        try
        {
            ManagementReportingData data = await _repository.GetSummaryAsync(window, cancellationToken);
            ManagementReportResponse response = _projector.Project(window, data);
            if (!await TryAuditAsync(AuditActions.ManagementReportViewed, "summary", window, context, null, cancellationToken))
            {
                return ManagementReportingResult<ManagementReportResponse>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
            }

            return ManagementReportingResult<ManagementReportResponse>.Success(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Management summary failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            _ = await TryAuditAsync(AuditActions.ManagementReportFailed, "summary", window, context, OperationalErrorCodes.ReportingUnavailable, CancellationToken.None);
            return ManagementReportingResult<ManagementReportResponse>.Fail(OperationalErrorCodes.ReportingUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<ManagementReportingResult<OperatorActivityPageResponse>> GetOperatorsAsync(
        string? selection,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        ManagementReportingContext context,
        CancellationToken cancellationToken)
    {
        ReportingWindowResolution resolved = _windowResolver.Resolve(selection, from, to);
        if (!resolved.IsValid || page < 1 || pageSize < 1 || pageSize > MaximumPageSize)
        {
            return await ValidationFailureAsync<OperatorActivityPageResponse>("operators", context, cancellationToken);
        }

        ReportingWindow window = resolved.Window!;
        if (!await TryAuditAsync(AuditActions.ManagementReportRequested, "operators", window, context, null, cancellationToken))
        {
            return ManagementReportingResult<OperatorActivityPageResponse>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        try
        {
            OperatorActivityDataPage data = await _repository.GetOperatorActivityAsync(window, page, pageSize, cancellationToken);
            OperatorActivityPageResponse response = _projector.ProjectOperators(window, page, pageSize, data);
            if (!await TryAuditAsync(AuditActions.ManagementReportViewed, "operators", window, context, null, cancellationToken))
            {
                return ManagementReportingResult<OperatorActivityPageResponse>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
            }

            return ManagementReportingResult<OperatorActivityPageResponse>.Success(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Operator management report failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            _ = await TryAuditAsync(AuditActions.ManagementReportFailed, "operators", window, context, OperationalErrorCodes.ReportingUnavailable, CancellationToken.None);
            return ManagementReportingResult<OperatorActivityPageResponse>.Fail(OperationalErrorCodes.ReportingUnavailable);
        }
    }

    private async Task<ManagementReportingResult<T>> ValidationFailureAsync<T>(
        string report,
        ManagementReportingContext context,
        CancellationToken cancellationToken)
    {
        bool audited = await TryAuditAsync(
            AuditActions.ManagementReportFailed,
            report,
            null,
            context,
            OperationalErrorCodes.ReportingValidationFailed,
            cancellationToken);
        return audited
            ? ManagementReportingResult<T>.Fail(OperationalErrorCodes.ReportingValidationFailed)
            : ManagementReportingResult<T>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
    }

    private async Task<bool> TryAuditAsync(
        string action,
        string report,
        ReportingWindow? window,
        ManagementReportingContext context,
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
                    report,
                    selection = window?.Selection,
                    fromInclusiveUtc = window?.FromInclusiveUtc,
                    toExclusiveUtc = window?.ToExclusiveUtc,
                    errorCode
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Management report audit failed. Action: {Action}. CorrelationId: {CorrelationId}", action, context.CorrelationId);
            return false;
        }
    }
}
