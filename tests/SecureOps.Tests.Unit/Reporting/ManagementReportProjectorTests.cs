using FluentAssertions;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Tests.Unit.Reporting;

public sealed class ManagementReportProjectorTests
{
    private static readonly ReportingWindow _window = new(
        "7d",
        new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Project_CountsTerminalIdentityOutcomesWithoutIntermediateDoubleCounting()
    {
        ManagementReportingData data = Data(auditCounts:
        [
            Count(AuditActions.IdentityLookupRequested, 100),
            Count(AuditActions.IdentityLookupProviderCall, 80),
            Count(AuditActions.IdentityLookupCacheHit, 20),
            Count(AuditActions.IdentityLookupSucceeded, 2),
            Count(AuditActions.IdentityLookupNotFound, 1),
            Count(AuditActions.IdentityLookupRejected, 1),
            Count(AuditActions.IdentityLookupFailed, 1),
            Count(AuditActions.IdentityLookupProviderTimeout, 1),
            Count(AuditActions.IdentityLookupForbidden, 1)
        ], identityUniqueOperators: 3);

        ManagementReportResponse report = new ManagementReportProjector().Project(_window, data);
        IdentityLookupMetricsResponse result = report.IdentityLookup;

        result.TotalLookups.Should().Be(7);
        result.SuccessfulLookups.Should().Be(2);
        result.NotFound.Should().Be(1);
        result.RejectedOrInvalid.Should().Be(1);
        result.ProviderUnavailable.Should().Be(2);
        result.AuthorizationDenied.Should().Be(1);
        result.UniqueActiveOperators.Should().Be(3);
        result.Trend.Sum(point => point.Total).Should().Be(7);
        report.PlatformAdoption.OperationsByWorkflow
            .Single(item => item.Name == "IdentityLookup").Count.Should().Be(6);
    }

    [Fact]
    public void Project_MapsWorkflowConcurrencyReconciliationRetryAndDurationEvidence()
    {
        ManagementReportingData data = Data(
            auditCounts:
            [
                Count(AuditActions.JiraPreviewGenerated, 4),
                Count(AuditActions.JiraCreateFailed, 2),
                Count(AuditActions.OperationalRecordCloseFailed, 1),
                Count(AuditActions.OperationalRecordConflict, 3),
                Count(AuditActions.JiraDuplicateCreatePrevented, 2),
                Count(AuditActions.OperationalRecordSourceChanged, 5, OperationalErrorCodes.OperationalRecordChanged),
                Count(AuditActions.OperationalRecordSourceChanged, 2, OperationalErrorCodes.OperationalRecordNoLongerOpen)
            ],
            workflowCounts:
            [
                new ReportingWorkflowCount("Imported", 10),
                new ReportingWorkflowCount("Eligible", 7),
                new ReportingWorkflowCount("JiraCreated", 5),
                new ReportingWorkflowCount("Completed", 4)
            ],
            reconciliation: 2,
            retryOutcomes: new ReportingRetryOutcomes(5, 2, 1),
            durations: [new ReportingDurationStatistics(
                ManagementReportingDurationKeys.ClaimToCompletion, 2, 10, 15, 20)]);

        ManagementReportResponse report = new ManagementReportProjector().Project(_window, data);

        report.OperationalWorkflow.RecordsImported.Should().Be(10);
        report.OperationalWorkflow.Previewed.Should().Be(4);
        report.OperationalWorkflow.SourceChangedPrevented.Should().Be(5);
        report.OperationalWorkflow.ClosedOrMissingPrevented.Should().Be(2);
        report.OperationalWorkflow.Failures.Should().Be(3);
        report.OperationalWorkflow.ReconciliationRequired.Should().Be(2);
        report.OperationalWorkflow.DuplicateCreatePrevented.Should().Be(2);
        report.OperationalWorkflow.Retries.Should().Be(new RetryOutcomeMetricsResponse(5, 2, 1, 2));
        report.SecurityAndQuality.ConcurrencyConflicts.Should().Be(3);
        report.SecurityAndQuality.ReconciliationEvents.Should().Be(2);
        report.SecurityAndQuality.RateLimitEvents.Should().BeNull();
        report.OperationalWorkflow.Durations.Single(item =>
            item.Key == ManagementReportingDurationKeys.ClaimToCompletion).SampleCount.Should().Be(2);
    }

    [Fact]
    public void Project_EmptyData_ReturnsZerosAndExplicitUnavailableMetrics()
    {
        ManagementReportResponse report = new ManagementReportProjector().Project(_window, Data());

        report.IdentityLookup.TotalLookups.Should().Be(0);
        report.OperationalWorkflow.Completed.Should().Be(0);
        report.PlatformAdoption.MonthlyActiveUsers.Should().Be(0);
        report.SecurityAndQuality.RateLimitEvents.Should().BeNull();
        report.IdentityLookup.Trend.Should().OnlyContain(point => point.Total == 0);
        report.DataLimitations.Should().NotBeEmpty();
        report.Coverage.CoverageFromUtc.Should().BeNull();
        report.Coverage.CoverageComplete.Should().BeFalse();
        report.Limitations.Should().Contain(item =>
            item.Code == ManagementReportingLimitationCodes.HistoryBeforePersistenceUnavailable);
    }

    [Fact]
    public void Project_DurationMetricsUseStableKeysIndependentOfInputOrderAndDisplayText()
    {
        ManagementReportingData data = Data(
            durations:
            [
                new ReportingDurationStatistics(ManagementReportingDurationKeys.ClaimToCompletion, 3, 4, 5, 6),
                new ReportingDurationStatistics(ManagementReportingDurationKeys.ImportToPreview, 7, 8, 9, 10),
                new ReportingDurationStatistics(ManagementReportingDurationKeys.ClaimToJiraCreation, 11, 12, 13, 14)
            ]);

        ManagementReportResponse report = new ManagementReportProjector().Project(_window, data);
        var indexed = report.OperationalWorkflow.Durations.ToDictionary(item => item.Key, StringComparer.Ordinal);
        DurationStatisticsResponse localized = indexed[ManagementReportingDurationKeys.ClaimToCompletion] with
        {
            Definition = "Localized duration label"
        };

        indexed.Keys.Should().BeEquivalentTo(
            ManagementReportingDurationKeys.ImportToPreview,
            ManagementReportingDurationKeys.ClaimToJiraCreation,
            ManagementReportingDurationKeys.ClaimToCompletion);
        indexed[ManagementReportingDurationKeys.ImportToPreview].SampleCount.Should().Be(7);
        indexed[ManagementReportingDurationKeys.ClaimToJiraCreation].AverageSeconds.Should().Be(13);
        localized.Key.Should().Be(ManagementReportingDurationKeys.ClaimToCompletion);
        localized.SampleCount.Should().Be(3);
    }

    [Fact]
    public void Project_LimitationsExposeStableCodesWithoutDependingOnEnglishMessages()
    {
        ManagementReportResponse report = new ManagementReportProjector().Project(_window, Data());
        var localized = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManagementReportingLimitationCodes.RateLimitRejectionsUnavailable] = "localized-1",
            [ManagementReportingLimitationCodes.AccessVersionConflictHistoryUnavailable] = "localized-2",
            [ManagementReportingLimitationCodes.OperationalSourceOutageHistoryUnavailable] = "localized-3",
            [ManagementReportingLimitationCodes.BulkIdentityInvalidItemHistoryUnavailable] = "localized-4",
            [ManagementReportingLimitationCodes.DuplicateCreatePreventionHistoryIncomplete] = "localized-5",
            [ManagementReportingLimitationCodes.ElapsedDurationsNotActiveEffort] = "localized-6",
            [ManagementReportingLimitationCodes.HistoryBeforePersistenceUnavailable] = "localized-7"
        };

        report.Limitations.Select(item => localized[item.Code]).Should().OnlyContain(text =>
            text.StartsWith("localized-", StringComparison.Ordinal));
        report.Limitations.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Message));

        DataLimitationResponse future = new("FutureLimitation", "Future fallback message");
        future.Message.Should().Be("Future fallback message");
    }

    [Fact]
    public void Project_CoverageDistinguishesPartialHistoryFromCompleteMeasuredZero()
    {
        DateTimeOffset partialBoundary = _window.FromInclusiveUtc.AddDays(2);
        ManagementReportResponse partial = new ManagementReportProjector().Project(
            _window,
            Data(coverageFromUtc: partialBoundary));
        ManagementReportResponse completeZero = new ManagementReportProjector().Project(
            _window,
            Data(coverageFromUtc: _window.FromInclusiveUtc.AddTicks(-1)));

        partial.IdentityLookup.TotalLookups.Should().Be(0);
        partial.Coverage.Should().Be(new ReportingEvidenceCoverageResponse(
            _window.FromInclusiveUtc, _window.ToExclusiveUtc, partialBoundary, false));
        partial.Limitations.Should().Contain(item =>
            item.Code == ManagementReportingLimitationCodes.HistoryBeforePersistenceUnavailable);

        completeZero.IdentityLookup.TotalLookups.Should().Be(0);
        completeZero.Coverage.CoverageComplete.Should().BeTrue();
        completeZero.Limitations.Should().NotContain(item =>
            item.Code == ManagementReportingLimitationCodes.HistoryBeforePersistenceUnavailable);
    }

    [Fact]
    public void Project_CoverageBeforeFirstEvidenceRemainsIncompleteAndLaterWindowIsComplete()
    {
        DateTimeOffset firstEvidence = _window.ToExclusiveUtc.AddDays(1);
        ManagementReportResponse beforeEvidence = new ManagementReportProjector().Project(
            _window,
            Data(coverageFromUtc: firstEvidence));
        ReportingWindow laterWindow = new(
            "custom",
            firstEvidence.AddDays(1),
            firstEvidence.AddDays(2));
        ManagementReportResponse afterEvidence = new ManagementReportProjector().Project(
            laterWindow,
            Data(coverageFromUtc: firstEvidence));

        beforeEvidence.Coverage.CoverageComplete.Should().BeFalse();
        afterEvidence.Coverage.CoverageComplete.Should().BeTrue();
    }

    [Fact]
    public void Project_CoveragePreservesTodaySevenThirtyAndCustomRequestedWindows()
    {
        ReportingWindow[] windows =
        [
            new("today", new DateTimeOffset(_window.ToExclusiveUtc.UtcDateTime.Date, TimeSpan.Zero), _window.ToExclusiveUtc),
            _window,
            new("30d", _window.ToExclusiveUtc.AddDays(-30), _window.ToExclusiveUtc),
            new("custom", _window.ToExclusiveUtc.AddHours(-6), _window.ToExclusiveUtc)
        ];

        foreach (ReportingWindow window in windows)
        {
            ManagementReportResponse report = new ManagementReportProjector().Project(
                window,
                Data(coverageFromUtc: window.FromInclusiveUtc));

            report.Coverage.RequestedFromUtc.Should().Be(window.FromInclusiveUtc);
            report.Coverage.RequestedToUtc.Should().Be(window.ToExclusiveUtc);
            report.Coverage.CoverageComplete.Should().BeTrue();
        }
    }

    [Fact]
    public void Project_ReportsDirectoryExplorerAdoptionWithoutChangingIdentityTotals()
    {
        ManagementReportingData data = Data(auditCounts:
        [
            Count(AuditActions.DirectoryGroupQueryCompleted, 3),
            Count(AuditActions.DirectoryGroupQueryRejected, 1),
            Count(AuditActions.DirectoryGroupQueryFailed, 1),
            Count(AuditActions.DirectoryGroupQueryForbidden, 2)
        ]);

        ManagementReportResponse report = new ManagementReportProjector().Project(_window, data);

        report.IdentityLookup.TotalLookups.Should().Be(0);
        report.PlatformAdoption.OperationsByWorkflow.Single(item => item.Name == "DirectoryExplorer").Count.Should().Be(5);
        report.SecurityAndQuality.AuthorizationFailures.Should().Be(2);
    }

    [Fact]
    public void Project_ReportsReliableSessionLifecycleAggregates()
    {
        ManagementReportingData data = Data(auditCounts:
        [
            Count(AuditActions.ApplicationSessionStarted, 9),
            Count(AuditActions.ApplicationSessionIdleTimedOut, 2),
            Count(AuditActions.ApplicationSessionAbsoluteTimedOut, 1),
            Count(AuditActions.ApplicationSessionLoggedOut, 3),
            Count(AuditActions.ApplicationSessionRevoked, 1),
            Count(AuditActions.ApplicationSessionAccessDisabled, 1),
            Count(AuditActions.ApplicationSessionAccessChanged, 1)
        ]);

        ManagementReportResponse report = new ManagementReportProjector().Project(_window, data);

        report.SessionGovernance.Should().Be(new SessionGovernanceMetricsResponse(9, 2, 1, 3, 1, 1, 1));
        report.PlatformAdoption.OperationsByWorkflow.Should().BeEmpty();
    }

    [Fact]
    public void ProjectOperators_PreservesServerPaginationAndDoesNotAddDirectoryProfileData()
    {
        OperatorActivityDataPage data = new(250,
        [
            new OperatorActivityData("CONTOSO\\operator-a", 15, _window.FromInclusiveUtc, _window.ToExclusiveUtc.AddMinutes(-1), 5, 3, 2, 5)
        ], _window.FromInclusiveUtc.AddDays(-1));

        OperatorActivityPageResponse result = new ManagementReportProjector().ProjectOperators(_window, 2, 100, data);

        result.TotalItems.Should().Be(250);
        result.Page.Should().Be(2);
        result.Items.Should().ContainSingle();
        result.Items[0].Actor.Should().Be("CONTOSO\\operator-a");
        result.Items[0].OperationsByWorkflow.Sum(item => item.Count).Should().Be(15);
        result.Items[0].OperationsByWorkflow.Single(item => item.Name == "DirectoryExplorer").Count.Should().Be(3);
        result.Coverage.CoverageComplete.Should().BeTrue();
    }

    private static ReportingAuditCount Count(string action, long count, string? detailCode = null) =>
        new(new DateTime(2026, 8, 20), action, detailCode, count);

    private static ManagementReportingData Data(
        IReadOnlyList<ReportingAuditCount>? auditCounts = null,
        IReadOnlyList<ReportingWorkflowCount>? workflowCounts = null,
        long identityUniqueOperators = 0,
        long reconciliation = 0,
        ReportingRetryOutcomes? retryOutcomes = null,
        IReadOnlyList<ReportingDurationStatistics>? durations = null,
        DateTimeOffset? coverageFromUtc = null) =>
        new(
            auditCounts ?? [],
            workflowCounts ?? [],
            identityUniqueOperators,
            new ReportingActiveUsers(0, 0, 0, 0),
            reconciliation,
            retryOutcomes ?? new ReportingRetryOutcomes(0, 0, 0),
            durations ?? [],
            coverageFromUtc);
}
