using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Projects server-aggregated rows into the stable management reporting contract.</summary>
public sealed class ManagementReportProjector
{
    private static readonly IReadOnlyList<string> _legacyLimitations =
    [
        "Rate-limit rejections are not currently persisted as audit events; this metric is unavailable.",
        "Historical access-version conflicts and Operational Record source-query outages are unavailable when no audit event exists.",
        "Invalid items skipped inside historical bulk identity requests do not have individual terminal audit rows.",
        "Duplicate-create prevention is measurable only from the first release that writes its explicit audit event.",
        "Elapsed durations include waits and retries and are not active labor, time saved, or operator performance."
    ];

    private static readonly IReadOnlyList<LimitationDefinition> _limitationDefinitions =
    [
        new(ManagementReportingLimitationCodes.RateLimitRejectionsUnavailable,
            "Rate-limit rejections are not currently persisted as audit events; this metric is unavailable."),
        new(ManagementReportingLimitationCodes.AccessVersionConflictHistoryUnavailable,
            "Historical access-version conflicts are unavailable when no audit event exists."),
        new(ManagementReportingLimitationCodes.OperationalSourceOutageHistoryUnavailable,
            "Historical Operational Record source-query outages are unavailable when no audit event exists."),
        new(ManagementReportingLimitationCodes.BulkIdentityInvalidItemHistoryUnavailable,
            "Invalid items skipped inside historical bulk identity requests do not have individual terminal audit rows."),
        new(ManagementReportingLimitationCodes.DuplicateCreatePreventionHistoryIncomplete,
            "Duplicate-create prevention is measurable only from the first release that writes its explicit audit event."),
        new(ManagementReportingLimitationCodes.ElapsedDurationsNotActiveEffort,
            "Elapsed durations include waits and retries and are not active labor, time saved, or operator performance.")
    ];

    private static readonly IReadOnlyList<DurationDefinition> _durationDefinitions =
    [
        new(ManagementReportingDurationKeys.ImportToPreview,
            "First persisted import to first persisted preview"),
        new(ManagementReportingDurationKeys.ClaimToJiraCreation,
            "Workflow claim to durable Jira issue-key persistence"),
        new(ManagementReportingDurationKeys.ClaimToCompletion,
            "Workflow claim to durable workflow completion")
    ];

    /// <summary>Projects one summary without reading raw browser or directory data.</summary>
    public ManagementReportResponse Project(ReportingWindow window, ManagementReportingData data)
    {
        var actionCounts = data.AuditCounts
            .GroupBy(item => item.Action, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count), StringComparer.Ordinal);
        var workflowCounts = data.WorkflowCounts
            .ToDictionary(item => item.WorkflowState, item => item.Count, StringComparer.Ordinal);

        long succeeded = Action(actionCounts, AuditActions.IdentityLookupSucceeded);
        long notFound = Action(actionCounts, AuditActions.IdentityLookupNotFound);
        long rejected = Action(actionCounts, AuditActions.IdentityLookupRejected);
        long providerUnavailable = Action(actionCounts, AuditActions.IdentityLookupFailed)
            + Action(actionCounts, AuditActions.IdentityLookupProviderTimeout);
        long identityForbidden = Action(actionCounts, AuditActions.IdentityLookupForbidden);
        long directoryForbidden = Action(actionCounts, AuditActions.DirectoryGroupQueryForbidden);

        IdentityLookupMetricsResponse identity = new(
            succeeded + notFound + rejected + providerUnavailable + identityForbidden,
            succeeded,
            notFound,
            rejected,
            providerUnavailable,
            identityForbidden,
            data.IdentityUniqueOperators,
            IdentityTrend(window, data.AuditCounts));

        long retryUnresolved = Math.Max(
            0,
            data.RetryOutcomes.Requested - data.RetryOutcomes.Succeeded - data.RetryOutcomes.Failed);
        OperationalWorkflowMetricsResponse operational = new(
            Workflow(workflowCounts, "Imported"),
            Workflow(workflowCounts, "Eligible"),
            Action(actionCounts, AuditActions.JiraPreviewGenerated),
            Workflow(workflowCounts, "JiraCreated"),
            Workflow(workflowCounts, "Completed"),
            Detail(data.AuditCounts, AuditActions.OperationalRecordSourceChanged, OperationalErrorCodes.OperationalRecordChanged),
            Detail(data.AuditCounts, AuditActions.OperationalRecordSourceChanged, OperationalErrorCodes.OperationalRecordNoLongerOpen),
            Action(actionCounts, AuditActions.JiraCreateFailed) + Action(actionCounts, AuditActions.OperationalRecordCloseFailed),
            data.ReconciliationRequired,
            Action(actionCounts, AuditActions.JiraDuplicateCreatePrevented),
            new RetryOutcomeMetricsResponse(
                data.RetryOutcomes.Requested,
                data.RetryOutcomes.Succeeded,
                data.RetryOutcomes.Failed,
                retryUnresolved),
            Durations(data.Durations));

        PlatformAdoptionMetricsResponse adoption = new(
            data.ActiveUsers.Daily,
            data.ActiveUsers.Weekly,
            data.ActiveUsers.Monthly,
            data.ActiveUsers.SelectedWindow,
            OperationsByWorkflow(data.AuditCounts),
            ReportingMetricCatalog.AccessActivityActions
                .Select(action => new NamedCountResponse(action, Action(actionCounts, action)))
                .ToArray());

        SecurityQualityMetricsResponse security = new(
            Action(actionCounts, AuditActions.AuthorizationDenied) + identityForbidden + directoryForbidden,
            Action(actionCounts, AuditActions.OperationalRecordConflict),
            data.ReconciliationRequired,
            providerUnavailable,
            RateLimitEvents: null);

        SessionGovernanceMetricsResponse sessions = new(
            Action(actionCounts, AuditActions.ApplicationSessionStarted),
            Action(actionCounts, AuditActions.ApplicationSessionIdleTimedOut),
            Action(actionCounts, AuditActions.ApplicationSessionAbsoluteTimedOut),
            Action(actionCounts, AuditActions.ApplicationSessionLoggedOut),
            Action(actionCounts, AuditActions.ApplicationSessionRevoked),
            Action(actionCounts, AuditActions.ApplicationSessionAccessDisabled),
            Action(actionCounts, AuditActions.ApplicationSessionAccessChanged));

        ReportingEvidenceCoverageResponse coverage = Coverage(window, data.CoverageFromUtc);
        return new ManagementReportResponse(
            Window(window),
            identity,
            operational,
            adoption,
            sessions,
            security,
            _legacyLimitations,
            coverage,
            Limitations(coverage, data.Sources));
    }

    /// <summary>Projects an already paged server aggregate.</summary>
    public OperatorActivityPageResponse ProjectOperators(
        ReportingWindow window,
        int page,
        int pageSize,
        OperatorActivityDataPage data) =>
        new(
            Window(window),
            page,
            pageSize,
            data.TotalItems,
            data.Items.Select(item => new OperatorActivityResponse(
                item.Actor,
                item.OperationCount,
                item.FirstActivityAt,
                item.LastActivityAt,
                [
                    new NamedCountResponse("IdentityLookup", item.IdentityOperations),
                    new NamedCountResponse("DirectoryExplorer", item.DirectoryOperations),
                    new NamedCountResponse("Access", item.AccessOperations),
                    new NamedCountResponse("OperationalRecordJira", item.OperationalWorkflowOperations)
                ])).ToArray(),
            Coverage(window, data.CoverageFromUtc));

    private static IReadOnlyList<IdentityLookupTrendPointResponse> IdentityTrend(
        ReportingWindow window,
        IReadOnlyList<ReportingAuditCount> counts)
    {
        Dictionary<(DateOnly Date, string Action), long> indexed = counts
            .Where(item => ReportingMetricCatalog.IdentityTerminalActions.Contains(item.Action, StringComparer.Ordinal))
            .GroupBy(item => (DateOnly.FromDateTime(item.BucketDate), item.Action))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count));
        var first = DateOnly.FromDateTime(window.FromInclusiveUtc.UtcDateTime.Date);
        var last = DateOnly.FromDateTime(window.ToExclusiveUtc.UtcDateTime.AddTicks(-1).Date);
        List<IdentityLookupTrendPointResponse> result = [];
        for (DateOnly date = first; date <= last; date = date.AddDays(1))
        {
            long success = Indexed(indexed, date, AuditActions.IdentityLookupSucceeded);
            long notFound = Indexed(indexed, date, AuditActions.IdentityLookupNotFound);
            long rejected = Indexed(indexed, date, AuditActions.IdentityLookupRejected);
            long unavailable = Indexed(indexed, date, AuditActions.IdentityLookupFailed)
                + Indexed(indexed, date, AuditActions.IdentityLookupProviderTimeout);
            long forbidden = Indexed(indexed, date, AuditActions.IdentityLookupForbidden);
            result.Add(new IdentityLookupTrendPointResponse(
                date,
                success + notFound + rejected + unavailable + forbidden,
                success,
                notFound,
                rejected,
                unavailable,
                forbidden));
        }

        return result;
    }

    private static IReadOnlyList<NamedCountResponse> OperationsByWorkflow(IReadOnlyList<ReportingAuditCount> counts) =>
        counts
            .Where(item => ReportingMetricCatalog.AdoptionActions.Contains(item.Action, StringComparer.Ordinal))
            .Select(item => (Workflow: ReportingMetricCatalog.WorkflowFor(item.Action), item.Count))
            .Where(item => item.Workflow is not null)
            .GroupBy(item => item.Workflow!, StringComparer.Ordinal)
            .Select(group => new NamedCountResponse(group.Key, group.Sum(item => item.Count)))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<DurationStatisticsResponse> Durations(
        IReadOnlyList<ReportingDurationStatistics> durations)
    {
        var indexed = durations.ToDictionary(item => item.Key, StringComparer.Ordinal);
        return _durationDefinitions.Select(definition => Duration(indexed, definition)).ToArray();
    }

    private static DurationStatisticsResponse Duration(
        IReadOnlyDictionary<string, ReportingDurationStatistics> indexed,
        DurationDefinition definition) =>
        indexed.TryGetValue(definition.Key, out ReportingDurationStatistics? value)
            ? new DurationStatisticsResponse(
                definition.Key,
                definition.Label,
                value.SampleCount,
                value.MinimumSeconds,
                value.AverageSeconds,
                value.MaximumSeconds)
            : new DurationStatisticsResponse(definition.Key, definition.Label, 0, null, null, null);

    private static ReportingEvidenceCoverageResponse Coverage(
        ReportingWindow window,
        DateTimeOffset? coverageFrom)
    {
        DateTimeOffset? normalized = coverageFrom?.ToUniversalTime();
        return new ReportingEvidenceCoverageResponse(
            window.FromInclusiveUtc,
            window.ToExclusiveUtc,
            normalized,
            normalized is not null && normalized <= window.FromInclusiveUtc);
    }

    private static IReadOnlyList<DataLimitationResponse> Limitations(
        ReportingEvidenceCoverageResponse coverage,
        ReportingSources sources)
    {
        var limitations = _limitationDefinitions
            .Select(item => new DataLimitationResponse(item.Code, item.Message))
            .ToList();
        if (!coverage.CoverageComplete)
        {
            limitations.Add(new DataLimitationResponse(
                ManagementReportingLimitationCodes.HistoryBeforePersistenceUnavailable,
                "Historical evidence before coverageFromUtc is unavailable."));
        }

        if (!sources.IsDurable)
        {
            limitations.Add(new DataLimitationResponse(
                ManagementReportingLimitationCodes.NonDurableReportingSource,
                $"In-memory evidence is lost on restart and is not authoritative history. Audit: {sources.Audit}; workflow: {sources.Workflow}."));
        }

        return limitations;
    }

    private static ReportingWindowResponse Window(ReportingWindow window) =>
        new(window.Selection, window.FromInclusiveUtc, window.ToExclusiveUtc);

    private static long Action(IReadOnlyDictionary<string, long> counts, string action) =>
        counts.TryGetValue(action, out long value) ? value : 0;

    private static long Workflow(IReadOnlyDictionary<string, long> counts, string state) =>
        counts.TryGetValue(state, out long value) ? value : 0;

    private static long Detail(
        IEnumerable<ReportingAuditCount> counts,
        string action,
        string detailCode) =>
        counts.Where(item => item.Action == action && item.DetailCode == detailCode).Sum(item => item.Count);

    private static long Indexed(
        IReadOnlyDictionary<(DateOnly Date, string Action), long> counts,
        DateOnly date,
        string action) =>
        counts.TryGetValue((date, action), out long value) ? value : 0;

    private sealed record DurationDefinition(string Key, string Label);
    private sealed record LimitationDefinition(string Code, string Message);
}
