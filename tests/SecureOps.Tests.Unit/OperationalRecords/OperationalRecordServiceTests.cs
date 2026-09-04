using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class OperationalRecordServiceTests
{
    [Fact]
    public async Task ListAsync_ImportsClassifiesAndAuditsWithoutPersonalProfileData()
    {
        InMemoryOperationalRecordRepository repository = new();
        InMemoryAuditWriter audit = new();
        OperationalRecordService service = new(
            new SourceClient([TestRecord.SourceItem()]),
            new ManualReviewOperationalRecordClassifier(),
            repository,
            audit,
            Options.Create(new OperationalRecordsOptions()),
            NullLogger<OperationalRecordService>.Instance);

        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await service.ListAsync(
            new OperationalRecordCommandContext("test:operator", "correlation-list", null),
            CancellationToken.None);

        IReadOnlyList<OperationalRecord> records = result.Value!;
        records.Should().ContainSingle().Which.WorkflowState.Should().Be(OperationalRecordWorkflowState.NeedsManualReview);
        audit.Events.Select(item => item.Action).Should().Contain([AuditActions.OperationalRecordImported, AuditActions.OperationalRecordClassified]);
        audit.Events.Should().OnlyContain(item => item.Actor == "test:operator");
    }

    [Fact]
    public async Task ListAsync_DescriptionBoundaryAndSourceOffset_RoundTripWithoutTruncation()
    {
        DateTimeOffset sourceCreatedAt = new(2026, 9, 2, 12, 30, 0, TimeSpan.FromHours(3));
        OperationalRecordSourceItem source = TestRecord.SourceItem() with
        {
            Description = new string('x', 8000),
            CreatedAt = sourceCreatedAt
        };
        OperationalRecordService service = Service(source);

        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await service.ListAsync(
            new OperationalRecordCommandContext("test:operator", "correlation-boundary", null),
            CancellationToken.None);

        OperationalRecord record = result.Value!.Should().ContainSingle().Which;
        record.Description.Should().HaveLength(8000);
        record.CreatedAt.Should().Be(sourceCreatedAt);
        record.CreatedAt!.Value.Offset.Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public async Task ListAsync_DescriptionAboveApplicationBoundary_FailsBeforePersistence()
    {
        OperationalRecordSourceItem source = TestRecord.SourceItem() with { Description = new string('x', 8001) };
        OperationalRecordService service = Service(source);

        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await service.ListAsync(
            new OperationalRecordCommandContext("test:operator", "correlation-too-long", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.Code.Should().Be(OperationalErrorCodes.OperationalRecordQueryFailed);
        result.Failure.Stage.Should().Be("source-validation");
    }

    [Fact]
    public async Task ListAsync_TuruncuHatProviderDoesNotReturnPreviouslyPersistedSyntheticRows()
    {
        InMemoryOperationalRecordRepository repository = new();
        _ = await repository.UpsertImportedAsync(
            TestRecord.SourceItem("synthetic-old-record", "SYN-OR-OLD"),
            "correlation-old",
            CancellationToken.None);
        OperationalRecordSourceItem corporateRecord = TestRecord.SourceItem("1683742", "OR-00668218") with
        {
            Title = "Safe corporate title",
            Description = "Safe corporate description"
        };
        OperationalRecordService service = new(
            new SourceClient([corporateRecord]),
            new TuruncuHatOperationalRecordClassifier(),
            repository,
            new InMemoryAuditWriter(),
            Options.Create(new OperationalRecordsOptions { SourceProvider = "TuruncuHat" }),
            NullLogger<OperationalRecordService>.Instance);

        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await service.ListAsync(
            new OperationalRecordCommandContext("test:operator", "correlation-corporate", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        OperationalRecord record = result.Value!.Should().ContainSingle().Which;
        record.SourceRecordId.Should().Be("1683742");
        record.WorkflowState.Should().Be(OperationalRecordWorkflowState.NeedsManualReview);
        record.JiraEligible.Should().BeFalse();
    }

    [Theory]
    [InlineData("synthetic-live-record", "OR-100")]
    [InlineData("1001", "SYN-OR-100")]
    public async Task ListAsync_TuruncuHatProviderRejectsSyntheticSourceRowsBeforePersistence(string sourceRecordId, string orCode)
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecordService service = new(
            new SourceClient([TestRecord.SourceItem(sourceRecordId, orCode)]),
            new TuruncuHatOperationalRecordClassifier(),
            repository,
            new InMemoryAuditWriter(),
            Options.Create(new OperationalRecordsOptions { SourceProvider = "TuruncuHat" }),
            NullLogger<OperationalRecordService>.Instance);

        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await service.ListAsync(
            new OperationalRecordCommandContext("test:operator", "correlation-synthetic", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.Stage.Should().Be("source-validation");
        (await repository.ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_TuruncuHatFailureDoesNotReturnPersistedSyntheticRows()
    {
        InMemoryOperationalRecordRepository repository = new();
        _ = await repository.UpsertImportedAsync(
            TestRecord.SourceItem("synthetic-old-record", "SYN-OR-OLD"),
            "correlation-old",
            CancellationToken.None);
        OperationalRecordService service = new(
            new FailingSourceClient(),
            new TuruncuHatOperationalRecordClassifier(),
            repository,
            new InMemoryAuditWriter(),
            Options.Create(new OperationalRecordsOptions { SourceProvider = "TuruncuHat" }),
            NullLogger<OperationalRecordService>.Instance);

        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await service.ListAsync(
            new OperationalRecordCommandContext("test:operator", "correlation-source-failure", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Failure!.Stage.Should().Be("source-query");
    }

    [Fact]
    public async Task GetAsync_TuruncuHatProviderDoesNotExposePersistedSyntheticRow()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord synthetic = await repository.UpsertImportedAsync(
            TestRecord.SourceItem("synthetic-old-record", "SYN-OR-OLD"),
            "correlation-old",
            CancellationToken.None);
        OperationalRecordService service = new(
            new SourceClient([]),
            new TuruncuHatOperationalRecordClassifier(),
            repository,
            new InMemoryAuditWriter(),
            Options.Create(new OperationalRecordsOptions { SourceProvider = "TuruncuHat" }),
            NullLogger<OperationalRecordService>.Instance);

        OperationalRecordResult<OperationalRecord> result = await service.GetAsync(synthetic.Id, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.Code.Should().Be(OperationalErrorCodes.OperationalRecordNotFound);
    }

    private static OperationalRecordService Service(OperationalRecordSourceItem source) => new(
        new SourceClient([source]),
        new ManualReviewOperationalRecordClassifier(),
        new InMemoryOperationalRecordRepository(),
        new InMemoryAuditWriter(),
        Options.Create(new OperationalRecordsOptions()),
        NullLogger<OperationalRecordService>.Instance);

    private sealed class SourceClient(IReadOnlyList<OperationalRecordSourceItem> records) : IOperationalRecordClient
    {
        public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken) => Task.FromResult(records);
        public Task<OperationalRecordSourceItem?> GetByIdAsync(string sourceRecordId, CancellationToken cancellationToken) =>
            Task.FromResult(records.SingleOrDefault(record => string.Equals(record.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase)));
        public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingSourceClient : IOperationalRecordClient
    {
        public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<OperationalRecordSourceItem>>(
                new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceUnavailable, true));

        public Task<OperationalRecordSourceItem?> GetByIdAsync(string sourceRecordId, CancellationToken cancellationToken) =>
            Task.FromResult<OperationalRecordSourceItem?>(null);

        public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
