using FluentAssertions;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Tests.Unit.Reporting;

public sealed class FactManagementReportingRepositoryTests
{
    private static readonly DateTimeOffset _from = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ReportingWindow _window = new("7d", _from, _from.AddDays(7));

    [Fact]
    public async Task Summary_GroupsByUtcDayAndReadsSourceChangedErrorCode()
    {
        FactManagementReportingRepository repository = Repository(
        [
            Audit(_from.AddHours(1), "CONTOSO\\a", AuditActions.IdentityLookupSucceeded),
            Audit(_from.AddHours(2), "CONTOSO\\b", AuditActions.IdentityLookupSucceeded),
            Audit(_from.AddDays(1).AddHours(-1), "CONTOSO\\a", AuditActions.OperationalRecordSourceChanged, details: """{"errorCode":"SourceChanged"}"""),
            Audit(_from.AddDays(-1), "CONTOSO\\a", AuditActions.IdentityLookupSucceeded),
            Audit(_from.AddHours(3), "CONTOSO\\a", AuditActions.ManagementReportViewed)
        ]);

        ManagementReportingData data = await repository.GetSummaryAsync(_window, CancellationToken.None);

        data.AuditCounts.Should().BeEquivalentTo(
        [
            new ReportingAuditCount(_from.UtcDateTime.Date, AuditActions.IdentityLookupSucceeded, null, 2),
            new ReportingAuditCount(_from.UtcDateTime.Date, AuditActions.OperationalRecordSourceChanged, "SourceChanged", 1)
        ]);
        data.CoverageFromUtc.Should().Be(_from.AddDays(-1));
        data.Sources.Should().Be(new ReportingSources(ReportingSourceKind.InMemory, ReportingSourceKind.InMemory));
    }

    [Fact]
    public async Task Summary_CountsPeopleCaseInsensitivelyAndExcludesAnonymousAndSystemActors()
    {
        DateTimeOffset last = _window.ToExclusiveUtc.AddHours(-1);
        FactManagementReportingRepository repository = Repository(
        [
            Audit(last, "CONTOSO\\Ayse", AuditActions.IdentityLookupSucceeded),
            Audit(last, "contoso\\ayse", AuditActions.IdentityLookupNotFound),
            Audit(last, "anonymous", AuditActions.IdentityLookupSucceeded),
            Audit(last, "SYSTEM:worker", AuditActions.IdentityLookupSucceeded),
            Audit(_from.AddHours(1), "CONTOSO\\mehmet", AuditActions.ApplicationSessionStarted)
        ]);

        ManagementReportingData data = await repository.GetSummaryAsync(_window, CancellationToken.None);

        data.IdentityUniqueOperators.Should().Be(1);
        data.ActiveUsers.Should().Be(new ReportingActiveUsers(Daily: 1, Weekly: 2, Monthly: 2, SelectedWindow: 2));
    }

    [Fact]
    public async Task Summary_RetryOutcomePrefersSuccessAndCorrelatesCaseInsensitively()
    {
        FactManagementReportingRepository repository = Repository(
        [
            Audit(_from.AddHours(1), "CONTOSO\\a", AuditActions.WorkflowRetried, "corr-ok"),
            Audit(_from.AddHours(2), "CONTOSO\\a", AuditActions.JiraCreateFailed, "CORR-OK"),
            Audit(_from.AddHours(3), "CONTOSO\\a", AuditActions.WorkflowCompleted, "corr-ok"),
            Audit(_from.AddHours(1), "CONTOSO\\a", AuditActions.WorkflowRetried, "corr-failed"),
            Audit(_from.AddHours(2), "CONTOSO\\a", AuditActions.OperationalRecordCloseFailed, "corr-failed"),
            Audit(_from.AddHours(1), "CONTOSO\\a", AuditActions.WorkflowRetried, "corr-open")
        ]);

        ManagementReportingData data = await repository.GetSummaryAsync(_window, CancellationToken.None);

        data.RetryOutcomes.Should().Be(new ReportingRetryOutcomes(Requested: 3, Succeeded: 1, Failed: 1));
    }

    [Fact]
    public async Task Summary_DurationsUseFirstOccurrencesAndOnlyEndsInsideTheWindow()
    {
        var record = Guid.NewGuid();
        string details = $$"""{"operationalRecordId":"{{record}}"}""";
        FactManagementReportingRepository repository = Repository(
            [
                Audit(_from.AddMinutes(10), "CONTOSO\\a", AuditActions.OperationalRecordClaimed, "c1", details),
                Audit(_from.AddMinutes(12), "CONTOSO\\a", AuditActions.JiraCreated, "c1", details),
                Audit(_from.AddMinutes(20), "CONTOSO\\a", AuditActions.WorkflowCompleted, "c1", details)
            ],
            [
                new ReportingWorkflowFact(record, nameof(OperationalRecordWorkflowState.Imported), _from.AddDays(-2)),
                new ReportingWorkflowFact(record, nameof(OperationalRecordWorkflowState.Previewed), _from.AddMinutes(5)),
                new ReportingWorkflowFact(record, nameof(OperationalRecordWorkflowState.Previewed), _from.AddMinutes(30))
            ]);

        ManagementReportingData data = await repository.GetSummaryAsync(_window, CancellationToken.None);

        data.Durations.Should().BeEquivalentTo(
        [
            new ReportingDurationStatistics(ManagementReportingDurationKeys.ImportToPreview, 1, 2 * 86400 + 300, 2 * 86400 + 300, 2 * 86400 + 300),
            new ReportingDurationStatistics(ManagementReportingDurationKeys.ClaimToJiraCreation, 1, 120, 120, 120),
            new ReportingDurationStatistics(ManagementReportingDurationKeys.ClaimToCompletion, 1, 600, 600, 600)
        ]);
        data.WorkflowCounts.Should().BeEquivalentTo([new ReportingWorkflowCount(nameof(OperationalRecordWorkflowState.Previewed), 1)]);
    }

    [Fact]
    public async Task Operators_OrdersByVolumeThenNameAndPagesAfterCounting()
    {
        FactManagementReportingRepository repository = Repository(
        [
            Audit(_from.AddHours(1), "CONTOSO\\b", AuditActions.IdentityLookupSucceeded),
            Audit(_from.AddHours(2), "CONTOSO\\a", AuditActions.AccessApproved),
            Audit(_from.AddHours(3), "CONTOSO\\c", AuditActions.JiraCreated),
            Audit(_from.AddHours(4), "CONTOSO\\c", AuditActions.DirectoryGroupQueryCompleted),
            Audit(_from.AddHours(5), "CONTOSO\\c", AuditActions.IdentityLookupForbidden)
        ]);

        OperatorActivityDataPage page = await repository.GetOperatorActivityAsync(_window, page: 1, pageSize: 2, CancellationToken.None);

        page.TotalItems.Should().Be(3);
        page.Items.Select(item => item.Actor).Should().Equal("CONTOSO\\c", "CONTOSO\\a");
        page.Items[0].Should().BeEquivalentTo(new { OperationCount = 2L, DirectoryOperations = 1L, OperationalWorkflowOperations = 1L, IdentityOperations = 0L });
    }

    [Fact]
    public async Task InMemoryRecords_RecordTheTransitionsSqlWouldPersist()
    {
        InMemoryOperationalRecordRepository records = new();
        OperationalRecord imported = await records.UpsertImportedAsync(
            new OperationalRecordSourceItem("src-1", "OR-1", "Synthetic", "Synthetic", null, null, null, null, null),
            "corr",
            CancellationToken.None);
        await records.SetClassificationAsync(
            imported.Id,
            new OperationalRecordClassificationResult(OperationalRecordClassification.NeedsManualReview, false, "Synthetic"),
            "corr",
            CancellationToken.None);

        IReadOnlyList<ReportingWorkflowFact> history = await new InMemoryReportingWorkflowFacts(records)
            .ReadHistoryAsync(DateTimeOffset.MaxValue, CancellationToken.None);

        history.Select(item => item.WorkflowState).Should().Equal(
            nameof(OperationalRecordWorkflowState.Imported),
            nameof(OperationalRecordWorkflowState.Classified),
            nameof(OperationalRecordWorkflowState.NeedsManualReview));
    }

    [Fact]
    public async Task InMemoryAudit_SerializesDetailsLikeTheSqlWriter()
    {
        InMemoryAuditWriter writer = new();
        await writer.WriteAsync(new AuditEvent
        {
            OccurredAt = _from,
            Actor = "CONTOSO\\a",
            Action = AuditActions.OperationalRecordSourceChanged,
            Details = new { ErrorCode = "SourceChanged" }
        }, CancellationToken.None);

        ManagementReportingData data = await new FactManagementReportingRepository(
                new InMemoryReportingAuditFacts(writer), new InMemoryReportingWorkflowFacts(new InMemoryOperationalRecordRepository()))
            .GetSummaryAsync(_window, CancellationToken.None);

        data.AuditCounts.Should().ContainSingle().Which.DetailCode.Should().Be("SourceChanged");
    }

    private static FactManagementReportingRepository Repository(
        IReadOnlyList<ReportingAuditFact> audit,
        IReadOnlyList<ReportingWorkflowFact>? history = null) =>
        new(new StubAuditFacts(audit), new StubWorkflowFacts(history ?? [], []));

    private static ReportingAuditFact Audit(DateTimeOffset at, string actor, string action, string? correlation = null, string? details = null) =>
        new(at, actor, action, correlation, details);

    private sealed class StubAuditFacts(IReadOnlyList<ReportingAuditFact> rows) : IReportingAuditFacts
    {
        public ReportingSourceKind Kind => ReportingSourceKind.InMemory;

        public Task<IReadOnlyList<ReportingAuditFact>> ReadAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportingAuditFact>>([.. rows.Where(row => row.OccurredAt < toExclusive)]);
    }

    private sealed class StubWorkflowFacts(IReadOnlyList<ReportingWorkflowFact> history, IReadOnlyList<ReportingTransferFact> transfers)
        : IReportingWorkflowFacts
    {
        public ReportingSourceKind Kind => ReportingSourceKind.InMemory;

        public Task<IReadOnlyList<ReportingWorkflowFact>> ReadHistoryAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportingWorkflowFact>>([.. history.Where(row => row.OccurredAt < toExclusive)]);

        public Task<IReadOnlyList<ReportingTransferFact>> ReadTransfersAsync(CancellationToken cancellationToken) => Task.FromResult(transfers);
    }
}
