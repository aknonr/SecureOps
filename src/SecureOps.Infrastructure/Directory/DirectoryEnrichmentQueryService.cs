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

/// <summary>Audited, cached, bounded Directory Explorer Phase 2 service.</summary>
public sealed class DirectoryEnrichmentQueryService : IDirectoryEnrichmentQueryService
{
    private const string MembershipsOperation = "principal-memberships";
    private const string MembershipPathsOperation = "principal-membership-paths";
    private const string AccountHealthOperation = "principal-account-health";
    private const string ServiceEvidenceOperation = "principal-service-evidence";
    private const string PrivilegedMembershipsOperation = "principal-privileged-memberships";
    private readonly IIdentityAccountNormalizer _accountNormalizer;
    private readonly DirectoryExactInputNormalizer _groupNormalizer;
    private readonly IDirectoryEnrichmentProvider _provider;
    private readonly DirectoryMembershipGraphBuilder _graphBuilder;
    private readonly DirectoryQueryCache _cache;
    private readonly IAuditWriter _auditWriter;
    private readonly DirectoryExplorerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DirectoryEnrichmentQueryService> _logger;

    /// <summary>Initializes the enrichment service.</summary>
    public DirectoryEnrichmentQueryService(
        IIdentityAccountNormalizer accountNormalizer,
        DirectoryExactInputNormalizer groupNormalizer,
        IDirectoryEnrichmentProvider provider,
        DirectoryMembershipGraphBuilder graphBuilder,
        DirectoryQueryCache cache,
        IAuditWriter auditWriter,
        IOptions<DirectoryExplorerOptions> options,
        TimeProvider timeProvider,
        ILogger<DirectoryEnrichmentQueryService> logger)
    {
        _accountNormalizer = accountNormalizer;
        _groupNormalizer = groupNormalizer;
        _provider = provider;
        _graphBuilder = graphBuilder;
        _cache = cache;
        _auditWriter = auditWriter;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryPrincipalMembershipsResponse>> GetMembershipsAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool validPurpose = DirectoryLookupPurpose.TryNormalize(request.Purpose, _options.MaxPurposeLength, out string? purpose);
        NormalizedInput? input = Normalize(request.Account, validPurpose);
        if (input is null)
        {
            return await InvalidAsync<DirectoryPrincipalMembershipsResponse>(
                MembershipsOperation, request.Account, null, purpose, context, cancellationToken);
        }

        if (!await RequestedAsync(MembershipsOperation, input.Value.Account, null, purpose, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryPrincipalMembershipsResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            DirectoryMembershipGraph? graph = await WithTraversalTimeoutAsync(
                token => GetGraphAsync(input.Value.Account, request.Refresh, token),
                cancellationToken);
            if (graph is null)
            {
                return await NotFoundAsync<DirectoryPrincipalMembershipsResponse>(
                    OperationalErrorCodes.DirectoryPrincipalNotFound,
                    MembershipsOperation, input.Value.Account, null, purpose, stopwatch.Elapsed,
                    QueryMetrics.Empty, context, cancellationToken);
            }

            DirectoryPrincipalMembershipsResponse response = Memberships(graph);
            return await SuccessAsync(response, MembershipsOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed, Metrics(graph, response.DirectGroups.Count, response.TransitiveGroups.Count,
                    response.DirectGroups.Count + response.TransitiveGroups.Count), context, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryPrincipalMembershipsResponse>(
                exception, MembershipsOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed, QueryMetrics.Empty, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryMembershipPathResponse>> GetMembershipPathsAsync(
        DirectoryMembershipPathRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool validPurpose = DirectoryLookupPurpose.TryNormalize(request.Purpose, _options.MaxPurposeLength, out string? purpose);
        NormalizedInput? input = Normalize(request.Account, validPurpose);
        DirectoryInputNormalizationResult target = _groupNormalizer.NormalizeGroup(request.TargetGroup);
        if (input is null || !target.IsValid || target.Value is null)
        {
            return await InvalidAsync<DirectoryMembershipPathResponse>(
                MembershipPathsOperation, request.Account, request.TargetGroup, purpose, context, cancellationToken);
        }

        if (!await RequestedAsync(MembershipPathsOperation, input.Value.Account, target.Value, purpose, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryMembershipPathResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            (DirectoryGroupRecord? targetGroup, DirectoryMembershipGraph? graph) = await WithTraversalTimeoutAsync(
                async token =>
                {
                    DirectoryGroupRecord? group = await GetGroupAsync(target.Value, request.Refresh, token);
                    DirectoryMembershipGraph? membershipGraph = group is null
                        ? null
                        : await GetGraphAsync(input.Value.Account, request.Refresh, token);
                    return (group, membershipGraph);
                },
                cancellationToken);
            if (targetGroup is null)
            {
                return await NotFoundAsync<DirectoryMembershipPathResponse>(
                    OperationalErrorCodes.DirectoryGroupNotFound,
                    MembershipPathsOperation, input.Value.Account, target.Value, purpose, stopwatch.Elapsed,
                    QueryMetrics.Empty, context, cancellationToken);
            }

            if (graph is null)
            {
                return await NotFoundAsync<DirectoryMembershipPathResponse>(
                    OperationalErrorCodes.DirectoryPrincipalNotFound,
                    MembershipPathsOperation, input.Value.Account, target.Value, purpose, stopwatch.Elapsed,
                    QueryMetrics.Empty, context, cancellationToken);
            }

            string? targetKey = DirectoryMembershipGraph.GroupKey(targetGroup);
            IReadOnlyList<IReadOnlyList<string>> paths = [];
            bool pathsTruncated = false;
            if (targetKey is not null)
            {
                paths = FindPaths(graph, targetKey, out pathsTruncated);
            }
            DirectoryMembershipPathResponse response = new(
                paths.Count > 0,
                targetKey is not null && graph.DirectGroupKeys.Contains(targetKey, StringComparer.Ordinal),
                paths.Select(path => MapPath(graph, path)).ToArray(),
                pathsTruncated,
                MapTraversal(graph.Traversal));
            return await SuccessAsync(response, MembershipPathsOperation, input.Value.Account, target.Value, purpose,
                stopwatch.Elapsed, Metrics(graph, response.IsDirect ? 1 : 0, response.IsMember && !response.IsDirect ? 1 : 0,
                    response.Paths.Count), context, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryMembershipPathResponse>(
                exception, MembershipPathsOperation, input.Value.Account, target.Value, purpose,
                stopwatch.Elapsed, QueryMetrics.Empty, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryAccountHealthResponse>> GetAccountHealthAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool validPurpose = DirectoryLookupPurpose.TryNormalize(request.Purpose, _options.MaxPurposeLength, out string? purpose);
        NormalizedInput? input = Normalize(request.Account, validPurpose);
        if (input is null)
        {
            return await InvalidAsync<DirectoryAccountHealthResponse>(
                AccountHealthOperation, request.Account, null, purpose, context, cancellationToken);
        }

        if (!await RequestedAsync(AccountHealthOperation, input.Value.Account, null, purpose, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryAccountHealthResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            DirectoryPrincipalEnrichmentRecord? principal = await GetPrincipalAsync(
                input.Value.Account, 0, request.Refresh, cancellationToken);
            if (principal is null)
            {
                return await NotFoundAsync<DirectoryAccountHealthResponse>(
                    OperationalErrorCodes.DirectoryPrincipalNotFound,
                    AccountHealthOperation, input.Value.Account, null, purpose, stopwatch.Elapsed,
                    QueryMetrics.Empty, context, cancellationToken);
            }

            DirectoryAccountHealthResponse response = new(
                principal.Enabled,
                principal.Locked,
                principal.PasswordLastSetUtc,
                PasswordAgeDays(principal.PasswordLastSetUtc),
                principal.PasswordNeverExpires,
                principal.AccountExpiresUtc,
                principal.MustChangePassword,
                principal.LastLogonTimestampUtc,
                true);
            return await SuccessAsync(response, AccountHealthOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed, QueryMetrics.One, context, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryAccountHealthResponse>(
                exception, AccountHealthOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed, QueryMetrics.Empty, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryServiceEvidenceResponse>> GetServiceEvidenceAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool validPurpose = DirectoryLookupPurpose.TryNormalize(request.Purpose, _options.MaxPurposeLength, out string? purpose);
        NormalizedInput? input = Normalize(request.Account, validPurpose);
        if (input is null)
        {
            return await InvalidAsync<DirectoryServiceEvidenceResponse>(
                ServiceEvidenceOperation, request.Account, null, purpose, context, cancellationToken);
        }

        if (!await RequestedAsync(ServiceEvidenceOperation, input.Value.Account, null, purpose, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryServiceEvidenceResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            DirectoryPrincipalEnrichmentRecord? principal = await GetPrincipalAsync(
                input.Value.Account, _options.MaxSpnsPerPrincipal, request.Refresh, cancellationToken);
            if (principal is null)
            {
                return await NotFoundAsync<DirectoryServiceEvidenceResponse>(
                    OperationalErrorCodes.DirectoryPrincipalNotFound,
                    ServiceEvidenceOperation, input.Value.Account, null, purpose, stopwatch.Elapsed,
                    QueryMetrics.Empty, context, cancellationToken);
            }

            DirectoryMembershipGraph? graph = null;
            try
            {
                graph = await WithTraversalTimeoutAsync(
                    token => GetGraphAsync(input.Value.Account, request.Refresh, token),
                    cancellationToken);
            }
            catch (Exception exception) when (IsExpectedProviderFailure(exception))
            {
                _logger.LogWarning(
                    "Directory membership evidence was unavailable while principal evidence succeeded. Operation: {Operation}. CorrelationId: {CorrelationId}",
                    ServiceEvidenceOperation,
                    context.CorrelationId);
            }

            DirectoryPrincipalMembershipsResponse? memberships = graph is null ? null : Memberships(graph);
            DirectoryServiceEvidenceResponse response = new(
                principal.ServicePrincipalNames,
                principal.ServicePrincipalNameCount,
                principal.ServicePrincipalNamesTruncated,
                principal.ManagedBy,
                principal.AccountExpiresUtc,
                principal.PasswordLastSetUtc,
                PasswordAgeDays(principal.PasswordLastSetUtc),
                principal.AccountTypeEvidence,
                memberships?.DirectGroups.Count,
                memberships?.TransitiveGroups.Count,
                graph is null ? null : MapTraversal(graph.Traversal),
                graph is not null);
            return await SuccessAsync(response, ServiceEvidenceOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed,
                graph is null
                    ? QueryMetrics.Empty with { ResultCount = 1, Truncated = true }
                    : Metrics(graph, response.DirectGroupCount!.Value, response.TransitiveGroupCount!.Value, 1),
                context,
                cancellationToken,
                graph is null ? "SucceededPartial" : "Succeeded");
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryServiceEvidenceResponse>(
                exception, ServiceEvidenceOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed, QueryMetrics.Empty, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryQueryResult<DirectoryPrivilegedMembershipResponse>> GetPrivilegedMembershipsAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool validPurpose = DirectoryLookupPurpose.TryNormalize(request.Purpose, _options.MaxPurposeLength, out string? purpose);
        NormalizedInput? input = Normalize(request.Account, validPurpose);
        if (input is null)
        {
            return await InvalidAsync<DirectoryPrivilegedMembershipResponse>(
                PrivilegedMembershipsOperation, request.Account, null, purpose, context, cancellationToken);
        }

        if (!await RequestedAsync(PrivilegedMembershipsOperation, input.Value.Account, null, purpose, context, cancellationToken))
        {
            return AuditUnavailable<DirectoryPrivilegedMembershipResponse>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            (DirectoryMembershipGraph? graph, IReadOnlyList<DirectoryPrivilegedMembershipDto> groups) =
                await WithTraversalTimeoutAsync(
                    async token =>
                    {
                        DirectoryMembershipGraph? membershipGraph = await GetGraphAsync(input.Value.Account, request.Refresh, token);
                        IReadOnlyList<DirectoryPrivilegedMembershipDto> results = membershipGraph is null
                            ? []
                            : await PrivilegedGroupsAsync(membershipGraph, request.Refresh, token);
                        return (membershipGraph, results);
                    },
                    cancellationToken);
            if (graph is null)
            {
                return await NotFoundAsync<DirectoryPrivilegedMembershipResponse>(
                    OperationalErrorCodes.DirectoryPrincipalNotFound,
                    PrivilegedMembershipsOperation, input.Value.Account, null, purpose, stopwatch.Elapsed,
                    QueryMetrics.Empty, context, cancellationToken);
            }

            DirectoryPrivilegedMembershipResponse response = new(groups, MapTraversal(graph.Traversal));
            int direct = groups.Count(group => group.Direct);
            int transitive = groups.Count(group => group.Transitive);
            return await SuccessAsync(response, PrivilegedMembershipsOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed, Metrics(graph, direct, transitive, groups.Count), context, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            return await ProviderFailureAsync<DirectoryPrivilegedMembershipResponse>(
                exception, PrivilegedMembershipsOperation, input.Value.Account, null, purpose,
                stopwatch.Elapsed, QueryMetrics.Empty, context, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<DirectoryPrivilegedMembershipDto>> PrivilegedGroupsAsync(
        DirectoryMembershipGraph graph,
        bool refresh,
        CancellationToken cancellationToken)
    {
        List<DirectoryPrivilegedMembershipDto> results = [];
        foreach (string configured in _options.PrivilegedGroupIdentifiers
                     .Select(value => value.Trim())
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            DirectoryInputNormalizationResult normalized = _groupNormalizer.NormalizeGroup(configured);
            DirectoryGroupRecord? group = await GetGroupAsync(normalized.Value!, refresh, cancellationToken);
            if (group is null)
            {
                results.Add(new DirectoryPrivilegedMembershipDto(configured, false, null, false, false, [], false));
                continue;
            }

            string? key = DirectoryMembershipGraph.GroupKey(group);
            IReadOnlyList<IReadOnlyList<string>> paths = [];
            bool pathsTruncated = false;
            if (key is not null)
            {
                paths = FindPaths(graph, key, out pathsTruncated);
            }
            bool direct = key is not null && graph.DirectGroupKeys.Contains(key, StringComparer.Ordinal);
            bool transitive = paths.Any(path => path.Count > 1);
            results.Add(new DirectoryPrivilegedMembershipDto(
                configured,
                true,
                MapGroup(group),
                direct,
                transitive,
                paths.Select(path => MapPath(graph, path)).ToArray(),
                pathsTruncated));
        }

        return results;
    }

    private DirectoryPrincipalMembershipsResponse Memberships(DirectoryMembershipGraph graph)
    {
        var directSet = graph.DirectGroupKeys.ToHashSet(StringComparer.Ordinal);
        DirectoryMembershipGroupDto[] direct = graph.DirectGroupKeys
            .Select(key => new DirectoryMembershipGroupDto(
                MapGroup(graph.Groups[key]),
                1,
                graph.IsAlsoTransitivelyReachable(key)))
            .ToArray();
        DirectoryMembershipGroupDto[] transitive = graph.MinimumDepths
            .Where(pair => pair.Value >= 2 && !directSet.Contains(pair.Key))
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DirectoryMembershipGroupDto(MapGroup(graph.Groups[pair.Key]), pair.Value, false))
            .ToArray();
        return new DirectoryPrincipalMembershipsResponse(direct, transitive, MapTraversal(graph.Traversal));
    }

    private async Task<DirectoryMembershipGraph?> GetGraphAsync(string account, bool refresh, CancellationToken cancellationToken) =>
        await _cache.GetOrCreateAsync(
            $"{_provider.ProviderName}|phase2-graph|{account}",
            refresh,
            token => _graphBuilder.BuildAsync(account, token),
            cancellationToken);

    private async Task<DirectoryPrincipalEnrichmentRecord?> GetPrincipalAsync(
        string account,
        int maxSpns,
        bool refresh,
        CancellationToken cancellationToken) =>
        await _cache.GetOrCreateAsync(
            $"{_provider.ProviderName}|phase2-principal|{account}|{maxSpns}",
            refresh,
            token => WithTimeoutAsync(
                providerToken => _provider.FindPrincipalAsync(account, maxSpns, providerToken),
                _options.ProviderTimeoutSeconds,
                token),
            cancellationToken);

    private async Task<DirectoryGroupRecord?> GetGroupAsync(string group, bool refresh, CancellationToken cancellationToken) =>
        await _cache.GetOrCreateAsync(
            $"{_provider.ProviderName}|phase2-group|{group}",
            refresh,
            token => WithTimeoutAsync(
                providerToken => _provider.FindGroupAsync(group, providerToken),
                _options.ProviderTimeoutSeconds,
                token),
            cancellationToken);

    private async Task<T> WithTraversalTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        await WithTimeoutAsync(operation, _options.TraversalTimeoutSeconds, cancellationToken);

    private static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return await operation(timeout.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The bounded directory operation timed out.", exception);
        }
    }

    private NormalizedInput? Normalize(string? account, bool validPurpose)
    {
        IdentityAccountNormalizationResult normalized = _accountNormalizer.Normalize(account);
        return normalized.IsValid && normalized.NormalizedAccount is not null && validPurpose
            ? new NormalizedInput(normalized.NormalizedAccount)
            : null;
    }

    private int? PasswordAgeDays(DateTimeOffset? passwordLastSet)
    {
        if (passwordLastSet is null || passwordLastSet > _timeProvider.GetUtcNow())
        {
            return null;
        }

        return (int)Math.Floor((_timeProvider.GetUtcNow() - passwordLastSet.Value).TotalDays);
    }

    private IReadOnlyList<IReadOnlyList<string>> FindPaths(
        DirectoryMembershipGraph graph,
        string targetKey,
        out bool truncated) => graph.FindPaths(targetKey, _options.MaxMembershipPaths, out truncated);

    private static DirectoryMembershipPathDto MapPath(
        DirectoryMembershipGraph graph,
        IReadOnlyList<string> path) => new(path.Select(key => MapGroup(graph.Groups[key])).ToArray());

    private static DirectoryGroupSummaryDto MapGroup(DirectoryGroupRecord group) => new(
        group.StableIdentifier,
        group.Name,
        group.SamAccountName,
        group.DistinguishedName,
        group.Description,
        group.Category,
        group.Scope,
        group.MembershipKind);

    private static DirectoryTraversalMetadataDto MapTraversal(DirectoryTraversalState traversal) => new(
        traversal.NodesVisited,
        traversal.EdgesVisited,
        traversal.MaximumDepthReached,
        traversal.CycleDetected,
        traversal.DepthLimitReached,
        traversal.NodeLimitReached,
        traversal.EdgeLimitReached,
        traversal.ProviderResultLimitReached,
        traversal.IsTruncated);

    private static QueryMetrics Metrics(
        DirectoryMembershipGraph graph,
        int directCount,
        int transitiveCount,
        int resultCount) => new(
            directCount,
            transitiveCount,
            resultCount,
            graph.Traversal.NodesVisited,
            graph.Traversal.EdgesVisited,
            graph.Traversal.IsTruncated);

    private Task<bool> RequestedAsync(
        string operation,
        string target,
        string? secondaryTarget,
        string? purpose,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken) => AuditAsync(
            AuditActions.DirectoryGroupQueryRequested,
            operation,
            target,
            secondaryTarget,
            purpose,
            "Requested",
            TimeSpan.Zero,
            QueryMetrics.Empty,
            context,
            cancellationToken);

    private async Task<DirectoryQueryResult<T>> InvalidAsync<T>(
        string operation,
        string? target,
        string? secondaryTarget,
        string? purpose,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool audited = await AuditAsync(
            AuditActions.DirectoryGroupQueryRejected,
            operation,
            target,
            secondaryTarget,
            purpose,
            "Rejected",
            TimeSpan.Zero,
            QueryMetrics.Empty,
            context,
            cancellationToken);
        return audited
            ? DirectoryQueryResult<T>.Failure(DirectoryQueryStatus.Invalid, OperationalErrorCodes.DirectoryInvalidInput)
            : AuditUnavailable<T>();
    }

    private async Task<DirectoryQueryResult<T>> SuccessAsync<T>(
        T response,
        string operation,
        string target,
        string? secondaryTarget,
        string? purpose,
        TimeSpan duration,
        QueryMetrics metrics,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken,
        string outcome = "Succeeded")
    {
        bool audited = await AuditAsync(
            AuditActions.DirectoryGroupQueryCompleted,
            operation,
            target,
            secondaryTarget,
            purpose,
            outcome,
            duration,
            metrics,
            context,
            cancellationToken);
        return audited ? DirectoryQueryResult<T>.Success(response) : AuditUnavailable<T>();
    }

    private async Task<DirectoryQueryResult<T>> NotFoundAsync<T>(
        string errorCode,
        string operation,
        string target,
        string? secondaryTarget,
        string? purpose,
        TimeSpan duration,
        QueryMetrics metrics,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool audited = await AuditAsync(
            AuditActions.DirectoryGroupQueryCompleted,
            operation,
            target,
            secondaryTarget,
            purpose,
            "NotFound",
            duration,
            metrics,
            context,
            cancellationToken);
        return audited
            ? DirectoryQueryResult<T>.Failure(DirectoryQueryStatus.NotFound, errorCode)
            : AuditUnavailable<T>();
    }

    private async Task<DirectoryQueryResult<T>> ProviderFailureAsync<T>(
        Exception exception,
        string operation,
        string target,
        string? secondaryTarget,
        string? purpose,
        TimeSpan duration,
        QueryMetrics metrics,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool limit = exception is DirectoryQueryLimitExceededException;
        bool timeout = exception is TimeoutException;
        string errorCode = limit
            ? OperationalErrorCodes.DirectoryQueryLimitExceeded
            : timeout ? OperationalErrorCodes.DirectoryProviderTimeout : OperationalErrorCodes.DirectoryProviderUnavailable;
        bool audited = await AuditAsync(
            AuditActions.DirectoryGroupQueryFailed,
            operation,
            target,
            secondaryTarget,
            purpose,
            limit ? "LimitExceeded" : timeout ? "ProviderTimeout" : "ProviderUnavailable",
            duration,
            metrics,
            context,
            cancellationToken);
        _logger.LogWarning(
            "Directory enrichment failed safely. Operation: {Operation}. ErrorCode: {ErrorCode}. CorrelationId: {CorrelationId}",
            operation,
            errorCode,
            context.CorrelationId);
        return audited
            ? DirectoryQueryResult<T>.Failure(
                limit ? DirectoryQueryStatus.LimitExceeded : timeout ? DirectoryQueryStatus.ProviderTimeout : DirectoryQueryStatus.ProviderUnavailable,
                errorCode)
            : AuditUnavailable<T>();
    }

    private async Task<bool> AuditAsync(
        string action,
        string operation,
        string? target,
        string? secondaryTarget,
        string? purpose,
        string outcome,
        TimeSpan duration,
        QueryMetrics metrics,
        DirectoryQueryExecutionContext context,
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
                    secondaryTargetHash = AuditAccountHasher.HashAccountInput(secondaryTarget),
                    secondaryTargetLength = secondaryTarget?.Trim().Length,
                    purposeHash = AuditAccountHasher.HashAccountInput(purpose),
                    purposeLength = purpose?.Trim().Length,
                    outcome,
                    durationMs = Math.Round(duration.TotalMilliseconds, 2),
                    metrics.DirectCount,
                    metrics.TransitiveCount,
                    metrics.ResultCount,
                    metrics.NodesVisited,
                    metrics.EdgesVisited,
                    limitReached = metrics.Truncated
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Directory enrichment audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return false;
        }
    }

    private static bool IsExpectedProviderFailure(Exception exception) =>
        exception is TimeoutException or DirectoryQueryLimitExceededException or DirectoryProviderUnavailableException;

    private static DirectoryQueryResult<T> AuditUnavailable<T>() =>
        DirectoryQueryResult<T>.Failure(DirectoryQueryStatus.AuditUnavailable, OperationalErrorCodes.AuditStoreUnavailable);

    private readonly record struct NormalizedInput(string Account);
    private readonly record struct QueryMetrics(
        int DirectCount,
        int TransitiveCount,
        int ResultCount,
        int NodesVisited,
        int EdgesVisited,
        bool Truncated)
    {
        public static QueryMetrics Empty => new(0, 0, 0, 0, 0, false);
        public static QueryMetrics One => new(0, 0, 1, 0, 0, false);
    }
}
