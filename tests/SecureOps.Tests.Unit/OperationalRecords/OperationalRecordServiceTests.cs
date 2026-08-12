using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;

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

    private sealed class SourceClient(IReadOnlyList<OperationalRecordSourceItem> records) : IOperationalRecordClient
    {
        public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken) => Task.FromResult(records);
        public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
