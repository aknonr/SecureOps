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
            durations: [new ReportingDurationStatistics("ClaimToCompleted", 2, 10, 15, 20)]);

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
        report.OperationalWorkflow.Durations.Single(item => item.Definition.Contains("completion", StringComparison.Ordinal)).SampleCount.Should().Be(2);
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
    public void ProjectOperators_PreservesServerPaginationAndDoesNotAddDirectoryProfileData()
    {
        OperatorActivityDataPage data = new(250,
        [
            new OperatorActivityData("CONTOSO\\operator-a", 15, _window.FromInclusiveUtc, _window.ToExclusiveUtc.AddMinutes(-1), 5, 3, 2, 5)
        ]);

        OperatorActivityPageResponse result = new ManagementReportProjector().ProjectOperators(_window, 2, 100, data);

        result.TotalItems.Should().Be(250);
        result.Page.Should().Be(2);
        result.Items.Should().ContainSingle();
        result.Items[0].Actor.Should().Be("CONTOSO\\operator-a");
        result.Items[0].OperationsByWorkflow.Sum(item => item.Count).Should().Be(15);
        result.Items[0].OperationsByWorkflow.Single(item => item.Name == "DirectoryExplorer").Count.Should().Be(3);
    }

    private static ReportingAuditCount Count(string action, long count, string? detailCode = null) =>
        new(new DateTime(2026, 8, 20), action, detailCode, count);

    private static ManagementReportingData Data(
        IReadOnlyList<ReportingAuditCount>? auditCounts = null,
        IReadOnlyList<ReportingWorkflowCount>? workflowCounts = null,
        long identityUniqueOperators = 0,
        long reconciliation = 0,
        ReportingRetryOutcomes? retryOutcomes = null,
        IReadOnlyList<ReportingDurationStatistics>? durations = null) =>
        new(
            auditCounts ?? [],
            workflowCounts ?? [],
            identityUniqueOperators,
            new ReportingActiveUsers(0, 0, 0, 0),
            reconciliation,
            retryOutcomes ?? new ReportingRetryOutcomes(0, 0, 0),
            durations ?? []);
}
