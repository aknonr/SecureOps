using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Audited exact-input group analysis and formula-safe CSV export.</summary>
public sealed class DirectoryGroupAnalysisService : IDirectoryGroupAnalysisService
{
    private const string AnalysisOperation = "group-analysis";
    private readonly DirectoryExactInputNormalizer _normalizer;
    private readonly DirectoryGroupAnalysisBuilder _builder;
    private readonly DirectoryQueryCache _cache;
    private readonly IAuditWriter _audit;
    private readonly DirectoryExplorerOptions _options;
    private readonly ILogger<DirectoryGroupAnalysisService> _logger;

    /// <summary>Initializes the service.</summary>
    public DirectoryGroupAnalysisService(
        DirectoryExactInputNormalizer normalizer,
        DirectoryGroupAnalysisBuilder builder,
        DirectoryQueryCache cache,
        IAuditWriter audit,
        IOptions<DirectoryExplorerOptions> options,
        ILogger<DirectoryGroupAnalysisService> logger)
    {
        _normalizer = normalizer;
        _builder = builder;
        _cache = cache;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryGroupAnalysisResponse>> AnalyzeAsync(
        DirectoryGroupAnalysisRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        DirectoryInputNormalizationResult group = _normalizer.NormalizeGroup(request.Group);
        bool validPurpose = DirectoryLookupPurpose.TryNormalize(
            request.Purpose, _options.MaxPurposeLength, out string? purpose);
        if (!group.IsValid || group.Value is null || !validPurpose)
        {
            await AuditAsync(AuditActions.DirectoryGroupQueryRejected, AnalysisOperation, request.Group,
                purpose, "Rejected", TimeSpan.Zero, null, context, cancellationToken);
            return DirectoryQueryResult<DirectoryGroupAnalysisResponse>.Failure(
                DirectoryQueryStatus.Invalid, OperationalErrorCodes.DirectoryInvalidInput);
        }

        if (!await AuditAsync(AuditActions.DirectoryGroupQueryRequested, AnalysisOperation, group.Value,
                purpose, "Requested", TimeSpan.Zero, null, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryGroupAnalysisResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TraversalTimeoutSeconds));
            DirectoryGroupAnalysisResponse? response;
            try
            {
                response = await _cache.GetOrCreateAsync(
                    $"group-analysis|{_builder.ProviderName}|{group.Value}",
                    request.Refresh,
                    token => _builder.BuildAsync(group.Value, token),
                    timeout.Token);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new TimeoutException("The bounded group analysis timed out.", exception);
            }

            if (response is null)
            {
                await AuditAsync(AuditActions.DirectoryGroupAnalysisCompleted, AnalysisOperation, group.Value,
                    purpose, "NotFound", stopwatch.Elapsed, null, context, cancellationToken);
                return DirectoryQueryResult<DirectoryGroupAnalysisResponse>.Failure(
                    DirectoryQueryStatus.NotFound, OperationalErrorCodes.DirectoryGroupNotFound);
            }

            if (!await AuditAsync(AuditActions.DirectoryGroupAnalysisCompleted, AnalysisOperation, group.Value,
                    purpose, response.IsComplete ? "Succeeded" : "SucceededPartial", stopwatch.Elapsed,
                    response, context, cancellationToken))
            {
                return AuditUnavailable<DirectoryGroupAnalysisResponse>();
            }

            return DirectoryQueryResult<DirectoryGroupAnalysisResponse>.Success(response);
        }
        catch (Exception exception) when (exception is TimeoutException or DirectoryProviderUnavailableException or DirectoryQueryLimitExceededException)
        {
            bool timeout = exception is TimeoutException;
            string code = timeout ? OperationalErrorCodes.DirectoryProviderTimeout : OperationalErrorCodes.DirectoryProviderUnavailable;
            await AuditAsync(AuditActions.DirectoryGroupQueryFailed, AnalysisOperation, group.Value,
                purpose, timeout ? "ProviderTimeout" : "ProviderUnavailable", stopwatch.Elapsed,
                null, context, cancellationToken);
            _logger.LogWarning("Group analysis failed safely. ErrorCode: {ErrorCode}. CorrelationId: {CorrelationId}",
                code, context.CorrelationId);
            return DirectoryQueryResult<DirectoryGroupAnalysisResponse>.Failure(
                timeout ? DirectoryQueryStatus.ProviderTimeout : DirectoryQueryStatus.ProviderUnavailable, code);
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryGroupExportResult>> ExportAsync(
        DirectoryGroupExportRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Format, "Csv", StringComparison.OrdinalIgnoreCase)
            || !(string.Equals(request.Mode, "DirectMembers", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Mode, "EffectiveMembers", StringComparison.OrdinalIgnoreCase)))
        {
            bool rejectionAudited = await AuditAsync(AuditActions.DirectoryGroupMembershipExported, "group-export", request.Group,
                request.Purpose, "Rejected", TimeSpan.Zero,
                new { mode = SafeMode(request.Mode), format = SafeFormat(request.Format), rowCount = 0 },
                context, cancellationToken);
            return rejectionAudited
                ? DirectoryQueryResult<DirectoryGroupExportResult>.Failure(
                    DirectoryQueryStatus.Invalid, OperationalErrorCodes.DirectoryInvalidInput)
                : AuditUnavailable<DirectoryGroupExportResult>();
        }

        DirectoryQueryResult<DirectoryGroupAnalysisResponse> analysis = await AnalyzeAsync(
            new DirectoryGroupAnalysisRequest(request.Group, request.Purpose, request.Refresh),
            context,
            cancellationToken);
        if (analysis.Status != DirectoryQueryStatus.Success || analysis.Value is null)
        {
            if (!await AuditAsync(AuditActions.DirectoryGroupMembershipExported, "group-export", request.Group,
                    request.Purpose, analysis.Status.ToString(), TimeSpan.Zero,
                    new { mode = SafeMode(request.Mode), format = "Csv", rowCount = 0 },
                    context, cancellationToken))
            {
                return AuditUnavailable<DirectoryGroupExportResult>();
            }

            return DirectoryQueryResult<DirectoryGroupExportResult>.Failure(analysis.Status, analysis.ErrorCode!);
        }

        if (!analysis.Value.IsComplete)
        {
            if (!await AuditAsync(AuditActions.DirectoryGroupMembershipExported, "group-export", request.Group,
                    request.Purpose, "RejectedPartial", TimeSpan.Zero,
                    new { mode = SafeMode(request.Mode), format = "Csv", rowCount = 0 },
                    context, cancellationToken))
            {
                return AuditUnavailable<DirectoryGroupExportResult>();
            }

            return DirectoryQueryResult<DirectoryGroupExportResult>.Failure(
                DirectoryQueryStatus.LimitExceeded, OperationalErrorCodes.DirectoryTraversalPartial);
        }

        IReadOnlyList<DirectoryMemberDto> rows = string.Equals(request.Mode, "DirectMembers", StringComparison.OrdinalIgnoreCase)
            ? analysis.Value.DirectMembers
            : analysis.Value.EffectiveMembers;
        if (rows.Count > _options.MaxExportRows)
        {
            if (!await AuditAsync(AuditActions.DirectoryGroupMembershipExported, "group-export", request.Group,
                    request.Purpose, "RejectedLimit", TimeSpan.Zero,
                    new { mode = SafeMode(request.Mode), format = "Csv", rowCount = rows.Count },
                    context, cancellationToken))
            {
                return AuditUnavailable<DirectoryGroupExportResult>();
            }

            return DirectoryQueryResult<DirectoryGroupExportResult>.Failure(
                DirectoryQueryStatus.LimitExceeded, OperationalErrorCodes.DirectoryTraversalPartial);
        }

        byte[] content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(rows))).ToArray();
        var result = new DirectoryGroupExportResult(
            content,
            "text/csv; charset=utf-8",
            "secureops-group-members.csv",
            request.Mode!,
            rows.Count);
        bool audited = await AuditAsync(AuditActions.DirectoryGroupMembershipExported, "group-export", request.Group,
            request.Purpose, "Succeeded", TimeSpan.Zero,
            new { result.Mode, format = "Csv", result.RowCount }, context, cancellationToken);
        return audited
            ? DirectoryQueryResult<DirectoryGroupExportResult>.Success(result)
            : AuditUnavailable<DirectoryGroupExportResult>();
    }

    private async Task<bool> AuditAsync(
        string action,
        string operation,
        string? target,
        string? purpose,
        string outcome,
        TimeSpan duration,
        object? result,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            object? safeResult = result is DirectoryGroupAnalysisResponse response
                ? new
                {
                    directCount = response.DirectMembers.Count,
                    effectiveCount = response.EffectiveMembers.Count,
                    nestedGroupCount = response.DirectNestedGroups.Count,
                    parentCount = response.ParentMemberships.DirectParents.Count + response.ParentMemberships.TransitiveParents.Count,
                    response.IsComplete,
                    limitReached = !response.IsComplete
                }
                : result;
            await _audit.WriteAsync(new AuditEvent
            {
                Actor = context.Actor,
                Action = action,
                CorrelationId = context.CorrelationId,
                SourceIp = context.SourceIp,
                Details = new
                {
                    operation,
                    targetHash = AuditAccountHasher.HashAccountInput(target),
                    targetLength = target?.Trim().Length,
                    purposeHash = AuditAccountHasher.HashAccountInput(purpose),
                    purposeLength = purpose?.Length,
                    outcome,
                    durationMs = Math.Round(duration.TotalMilliseconds, 2),
                    result = safeResult
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Group analysis audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return false;
        }
    }

    private static string Csv(IReadOnlyList<DirectoryMemberDto> rows)
    {
        var builder = new StringBuilder("Name,SamAccountName,ObjectType,StableIdentifier,DistinguishedName\r\n");
        foreach (DirectoryMemberDto row in rows
                     .OrderBy(item => item.SamAccountName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendJoin(',', Cell(row.Name), Cell(row.SamAccountName), Cell(row.MemberType),
                Cell(row.StableIdentifier), Cell(row.DistinguishedName));
            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    private static string Cell(string? value)
    {
        string safe = value ?? string.Empty;
        if (safe.Length > 0 && (safe[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
        {
            safe = $"'{safe}";
        }

        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string SafeMode(string? mode) =>
        string.Equals(mode, "DirectMembers", StringComparison.OrdinalIgnoreCase) ? "DirectMembers"
        : string.Equals(mode, "EffectiveMembers", StringComparison.OrdinalIgnoreCase) ? "EffectiveMembers"
        : "Invalid";

    private static string SafeFormat(string? format) =>
        string.Equals(format, "Csv", StringComparison.OrdinalIgnoreCase) ? "Csv" : "Invalid";

    private static DirectoryQueryResult<T> AuditUnavailable<T>() =>
        DirectoryQueryResult<T>.Failure(DirectoryQueryStatus.AuditUnavailable, OperationalErrorCodes.AuditStoreUnavailable);
}
