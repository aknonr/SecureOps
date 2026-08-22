using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Audited, exact-input, bounded Directory Explorer service.</summary>
public sealed class DirectoryGroupQueryService : IDirectoryGroupQueryService
{
    private const string PrincipalGroupsOperation = "principal-direct-groups";
    private const string GroupLookupOperation = "group-metadata";
    private const string GroupMembersOperation = "group-direct-members";
    private readonly IIdentityAccountNormalizer _accountNormalizer;
    private readonly DirectoryExactInputNormalizer _groupNormalizer;
    private readonly IDirectoryGroupProvider _provider;
    private readonly DirectoryContinuationTokenCodec _tokens;
    private readonly DirectoryQueryCache _cache;
    private readonly IAuditWriter _auditWriter;
    private readonly DirectoryExplorerOptions _options;
    private readonly ILogger<DirectoryGroupQueryService> _logger;

    /// <summary>Initializes the query service.</summary>
    public DirectoryGroupQueryService(
        IIdentityAccountNormalizer accountNormalizer,
        DirectoryExactInputNormalizer groupNormalizer,
        IDirectoryGroupProvider provider,
        DirectoryContinuationTokenCodec tokens,
        DirectoryQueryCache cache,
        IAuditWriter auditWriter,
        IOptions<DirectoryExplorerOptions> options,
        ILogger<DirectoryGroupQueryService> logger)
    {
        _accountNormalizer = accountNormalizer;
        _groupNormalizer = groupNormalizer;
        _provider = provider;
        _tokens = tokens;
        _cache = cache;
        _auditWriter = auditWriter;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryGroupPageResponse>> GetPrincipalGroupsAsync(
        DirectoryPrincipalGroupsRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        IdentityAccountNormalizationResult normalized = _accountNormalizer.Normalize(request.Account);
        if (!normalized.IsValid || normalized.NormalizedAccount is null)
        {
            return await InvalidAsync<DirectoryGroupPageResponse>(PrincipalGroupsOperation, request.Account, request.Purpose, context, cancellationToken);
        }

        PageInput? page = Page(request.PageSize, request.ContinuationToken, request.Refresh, PrincipalGroupsOperation, normalized.NormalizedAccount);
        if (page is null || !ValidPurpose(request.Purpose))
        {
            return await InvalidAsync<DirectoryGroupPageResponse>(PrincipalGroupsOperation, request.Account, request.Purpose, context, cancellationToken);
        }

        if (!await AuditAsync(AuditActions.DirectoryGroupQueryRequested, PrincipalGroupsOperation, normalized.NormalizedAccount, request.Purpose, "Requested", 0, page.Value.PageSize, 0, request.ContinuationToken is not null, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryGroupPageResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            string cacheKey = CacheKey(PrincipalGroupsOperation, normalized.NormalizedAccount, page.Value);
            DirectoryProviderPage<DirectoryGroupRecord>? providerPage = await _cache.GetOrCreateAsync(
                cacheKey,
                request.Refresh,
                token => _provider.GetPrincipalDirectGroupsAsync(
                    normalized.NormalizedAccount,
                    page.Value.Offset,
                    page.Value.PageSize,
                    _options.ProviderResultLimit,
                    token).WaitAsync(TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds), token),
                cancellationToken);
            if (providerPage is null)
            {
                return await CompleteFailureAsync<DirectoryGroupPageResponse>(
                    DirectoryQueryStatus.NotFound, OperationalErrorCodes.DirectoryPrincipalNotFound, PrincipalGroupsOperation,
                    normalized.NormalizedAccount, request.Purpose, stopwatch.Elapsed, page.Value, context, cancellationToken);
            }

            DirectoryGroupSummaryDto[] items = providerPage.Items.Select(MapGroupSummary).ToArray();
            string? continuation = providerPage.HasMore
                ? _tokens.Create(PrincipalGroupsOperation, normalized.NormalizedAccount, page.Value.Offset + items.Length)
                : null;
            DirectoryGroupPageResponse response = new(items, page.Value.PageSize, continuation);
            return await CompleteSuccessAsync(response, PrincipalGroupsOperation, normalized.NormalizedAccount, request.Purpose,
                stopwatch.Elapsed, page.Value, items.Length, context, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryGroupPageResponse>(exception, PrincipalGroupsOperation, normalized.NormalizedAccount,
                request.Purpose, stopwatch.Elapsed, page.Value, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryGroupDetailResponse>> GetGroupAsync(
        DirectoryGroupLookupRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        DirectoryInputNormalizationResult normalized = _groupNormalizer.NormalizeGroup(request.Group);
        if (!normalized.IsValid || normalized.Value is null || !ValidPurpose(request.Purpose))
        {
            return await InvalidAsync<DirectoryGroupDetailResponse>(GroupLookupOperation, request.Group, request.Purpose, context, cancellationToken);
        }

        if (!await AuditAsync(AuditActions.DirectoryGroupQueryRequested, GroupLookupOperation, normalized.Value, request.Purpose, "Requested", 0, null, 0, false, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryGroupDetailResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            DirectoryGroupRecord? group = await _cache.GetOrCreateAsync(
                $"{_provider.ProviderName}|{GroupLookupOperation}|{normalized.Value}",
                request.Refresh,
                token => _provider.FindGroupAsync(normalized.Value, token)
                    .WaitAsync(TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds), token),
                cancellationToken);
            if (group is null)
            {
                return await CompleteFailureAsync<DirectoryGroupDetailResponse>(
                    DirectoryQueryStatus.NotFound, OperationalErrorCodes.DirectoryGroupNotFound, GroupLookupOperation,
                    normalized.Value, request.Purpose, stopwatch.Elapsed, default, context, cancellationToken);
            }

            DirectoryGroupDetailResponse response = new(MapGroupDetail(group));
            return await CompleteSuccessAsync(response, GroupLookupOperation, normalized.Value, request.Purpose,
                stopwatch.Elapsed, default, 1, context, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryGroupDetailResponse>(exception, GroupLookupOperation, normalized.Value,
                request.Purpose, stopwatch.Elapsed, default, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryMemberPageResponse>> GetGroupMembersAsync(
        DirectoryGroupMembersRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        DirectoryInputNormalizationResult normalized = _groupNormalizer.NormalizeGroup(request.Group);
        if (!normalized.IsValid || normalized.Value is null)
        {
            return await InvalidAsync<DirectoryMemberPageResponse>(GroupMembersOperation, request.Group, request.Purpose, context, cancellationToken);
        }

        PageInput? page = Page(request.PageSize, request.ContinuationToken, request.Refresh, GroupMembersOperation, normalized.Value);
        if (page is null || !ValidPurpose(request.Purpose))
        {
            return await InvalidAsync<DirectoryMemberPageResponse>(GroupMembersOperation, request.Group, request.Purpose, context, cancellationToken);
        }

        if (!await AuditAsync(AuditActions.DirectoryGroupQueryRequested, GroupMembersOperation, normalized.Value, request.Purpose, "Requested", 0, page.Value.PageSize, 0, request.ContinuationToken is not null, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryMemberPageResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            string cacheKey = CacheKey(GroupMembersOperation, normalized.Value, page.Value);
            DirectoryProviderPage<DirectoryMemberRecord>? providerPage = await _cache.GetOrCreateAsync(
                cacheKey,
                request.Refresh,
                token => _provider.GetDirectMembersAsync(normalized.Value, page.Value.Offset, page.Value.PageSize,
                    _options.ProviderResultLimit, token).WaitAsync(TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds), token),
                cancellationToken);
            if (providerPage is null)
            {
                return await CompleteFailureAsync<DirectoryMemberPageResponse>(
                    DirectoryQueryStatus.NotFound, OperationalErrorCodes.DirectoryGroupNotFound, GroupMembersOperation,
                    normalized.Value, request.Purpose, stopwatch.Elapsed, page.Value, context, cancellationToken);
            }

            DirectoryMemberDto[] items = providerPage.Items.Select(MapMember).ToArray();
            string? continuation = providerPage.HasMore
                ? _tokens.Create(GroupMembersOperation, normalized.Value, page.Value.Offset + items.Length)
                : null;
            DirectoryMemberPageResponse response = new(items, page.Value.PageSize, continuation);
            return await CompleteSuccessAsync(response, GroupMembersOperation, normalized.Value, request.Purpose,
                stopwatch.Elapsed, page.Value, items.Length, context, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryMemberPageResponse>(exception, GroupMembersOperation, normalized.Value,
                request.Purpose, stopwatch.Elapsed, page.Value, context, cancellationToken);
        }
    }

    private PageInput? Page(int? requestedSize, string? token, bool refresh, string operation, string target)
    {
        int pageSize = requestedSize ?? _options.DefaultPageSize;
        if (pageSize < 1 || pageSize > _options.MaxPageSize || (refresh && !string.IsNullOrWhiteSpace(token))
            || !_tokens.TryRead(token, operation, target, out int offset) || offset > _options.ProviderResultLimit)
        {
            return null;
        }

        return new PageInput(offset, pageSize);
    }

    private bool ValidPurpose(string? purpose) => !string.IsNullOrWhiteSpace(purpose)
        && purpose.Trim().Length <= _options.MaxPurposeLength
        && !purpose.Any(char.IsControl);

    private async Task<DirectoryQueryResult<T>> InvalidAsync<T>(
        string operation, string? target, string? purpose, DirectoryQueryExecutionContext context, CancellationToken cancellationToken)
    {
        bool audited = await AuditAsync(AuditActions.DirectoryGroupQueryRejected, operation, target, purpose, "Rejected", 0, null, 0, false, context, cancellationToken);
        return audited
            ? DirectoryQueryResult<T>.Failure(DirectoryQueryStatus.Invalid, OperationalErrorCodes.DirectoryInvalidInput)
            : AuditUnavailable<T>();
    }

    private async Task<DirectoryQueryResult<T>> CompleteSuccessAsync<T>(
        T response, string operation, string target, string? purpose, TimeSpan duration, PageInput page,
        int resultCount, DirectoryQueryExecutionContext context, CancellationToken cancellationToken)
    {
        bool audited = await AuditAsync(AuditActions.DirectoryGroupQueryCompleted, operation, target, purpose, "Succeeded",
            duration.TotalMilliseconds, page.PageSize == 0 ? null : page.PageSize, resultCount, page.Offset > 0, context, cancellationToken);
        return audited ? DirectoryQueryResult<T>.Success(response) : AuditUnavailable<T>();
    }

    private async Task<DirectoryQueryResult<T>> CompleteFailureAsync<T>(
        DirectoryQueryStatus status, string errorCode, string operation, string target, string? purpose, TimeSpan duration,
        PageInput page, DirectoryQueryExecutionContext context, CancellationToken cancellationToken)
    {
        bool audited = await AuditAsync(AuditActions.DirectoryGroupQueryCompleted, operation, target, purpose, "NotFound",
            duration.TotalMilliseconds, page.PageSize == 0 ? null : page.PageSize, 0, page.Offset > 0, context, cancellationToken);
        return audited ? DirectoryQueryResult<T>.Failure(status, errorCode) : AuditUnavailable<T>();
    }

    private async Task<DirectoryQueryResult<T>> ProviderFailureAsync<T>(
        Exception exception, string operation, string target, string? purpose, TimeSpan duration, PageInput page,
        DirectoryQueryExecutionContext context, CancellationToken cancellationToken)
    {
        bool limit = exception is DirectoryQueryLimitExceededException;
        string errorCode = limit ? OperationalErrorCodes.DirectoryQueryLimitExceeded : OperationalErrorCodes.DirectoryProviderUnavailable;
        bool audited = await AuditAsync(AuditActions.DirectoryGroupQueryFailed, operation, target, purpose,
            limit ? "LimitExceeded" : "ProviderUnavailable", duration.TotalMilliseconds,
            page.PageSize == 0 ? null : page.PageSize, 0, page.Offset > 0, context, cancellationToken);
        _logger.LogWarning("Directory query failed safely. Operation: {Operation}. ErrorCode: {ErrorCode}. CorrelationId: {CorrelationId}",
            operation, errorCode, context.CorrelationId);
        return audited
            ? DirectoryQueryResult<T>.Failure(limit ? DirectoryQueryStatus.LimitExceeded : DirectoryQueryStatus.ProviderUnavailable, errorCode)
            : AuditUnavailable<T>();
    }

    private async Task<bool> AuditAsync(
        string action, string operation, string? target, string? purpose, string outcome, double durationMs,
        int? pageSize, int resultCount, bool continuationUsed, DirectoryQueryExecutionContext context,
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
                    operation,
                    targetHash = AuditAccountHasher.HashAccountInput(target),
                    targetLength = target?.Trim().Length,
                    purposeHash = AuditAccountHasher.HashAccountInput(purpose),
                    purposeLength = purpose?.Trim().Length,
                    outcome,
                    durationMs = Math.Round(durationMs, 2),
                    pageSize,
                    resultCount,
                    continuationUsed
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Directory query audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return false;
        }
    }

    private string CacheKey(string operation, string target, PageInput page) =>
        $"{_provider.ProviderName}|{operation}|{target}|{page.Offset}|{page.PageSize}";

    private static bool IsExpectedProviderFailure(Exception exception) =>
        exception is TimeoutException or DirectoryQueryLimitExceededException or DirectoryProviderUnavailableException;

    private static DirectoryQueryResult<T> AuditUnavailable<T>() =>
        DirectoryQueryResult<T>.Failure(DirectoryQueryStatus.AuditUnavailable, OperationalErrorCodes.AuditStoreUnavailable);

    private static DirectoryGroupSummaryDto MapGroupSummary(DirectoryGroupRecord group) => new(
        group.StableIdentifier, group.Name, group.SamAccountName, group.DistinguishedName,
        group.Description, group.Category, group.Scope);

    private static DirectoryGroupDetailDto MapGroupDetail(DirectoryGroupRecord group) => new(
        group.StableIdentifier, group.Name, group.SamAccountName, group.DistinguishedName,
        group.Description, group.Category, group.Scope, group.ManagedBy, group.DirectMemberCount);

    private static DirectoryMemberDto MapMember(DirectoryMemberRecord member) => new(
        member.StableIdentifier, member.Name, member.SamAccountName, member.DistinguishedName, member.MemberType);

    private readonly record struct PageInput(int Offset, int PageSize);
}
