using FluentAssertions;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class OperationalRecordEnumContractTests
{
    [Fact]
    public void OperationalRecordClassification_V1NumericValuesRemainStable()
    {
        Enum.GetValues<OperationalRecordClassification>()
            .Select(value => (Name: value.ToString(), Value: (int)value))
            .Should().Equal([
                ("ServerRequest", 0),
                ("EnvironmentRequest", 1),
                ("SoftwareInstallation", 2),
                ("ConfigurationRequest", 3),
                ("OperationalSupport", 4),
                ("NotJiraEligible", 5),
                ("NeedsManualReview", 6),
                ("ServerRetirement", 7)
            ]);
    }

    [Fact]
    public void OperationalRecordWorkflowState_V1NumericValuesRemainStable()
    {
        Enum.GetValues<OperationalRecordWorkflowState>()
            .Select(value => (Name: value.ToString(), Value: (int)value))
            .Should().Equal([
                ("Imported", 0),
                ("Classified", 1),
                ("NeedsManualReview", 2),
                ("Eligible", 3),
                ("Previewed", 4),
                ("CreateRequested", 5),
                ("CreatingJira", 6),
                ("JiraCreated", 7),
                ("ClosingOperationalRecord", 8),
                ("Completed", 9),
                ("JiraCreateFailed", 10),
                ("OperationalRecordCloseFailed", 11)
            ]);
    }

    [Fact]
    public async Task SetClassificationAsync_AfterPreview_DoesNotRegressWorkflowState()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        WorkflowAcquireResult previewed = await repository.MarkPreviewedAsync(
            record.Id,
            "mapping-v1",
            OperationalRecordIdempotency.Create(record.SourceRecordId, "mapping-v1"),
            "test:publisher",
            "correlation-preview",
            CancellationToken.None);

        OperationalRecord refreshed = await repository.SetClassificationAsync(
            record.Id,
            new OperationalRecordClassificationResult(OperationalRecordClassification.NeedsManualReview, false, "Stale classification."),
            "correlation-refresh",
            CancellationToken.None);

        previewed.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        refreshed.WorkflowState.Should().Be(OperationalRecordWorkflowState.Previewed);
        refreshed.Classification.Should().Be(OperationalRecordClassification.OperationalSupport);
        refreshed.Version.Should().Be(previewed.Record!.Version);
    }

    [Fact]
    public async Task SetClassificationAsync_WhenReconciliationRequired_DoesNotRegressWorkflowState()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        string transferKey = OperationalRecordIdempotency.Create(record.SourceRecordId, "mapping-v1");
        _ = await repository.MarkPreviewedAsync(record.Id, "mapping-v1", transferKey, "test:publisher", "correlation-preview", CancellationToken.None);
        _ = await repository.TryClaimAsync(record.Id, "test:publisher", TimeSpan.FromMinutes(2), "correlation-claim", CancellationToken.None);
        _ = await repository.TryAcquireCreateAsync(record.Id, "mapping-v1", transferKey, "test:publisher", "correlation-create", CancellationToken.None);
        OperationalRecord failed = await repository.RecordFailureAsync(
            record.Id,
            WorkflowFailureStage.JiraCreate,
            SecureOps.Shared.Contracts.Api.OperationalErrorCodes.JiraUnavailable,
            true,
            "test:publisher",
            "correlation-failure",
            CancellationToken.None);

        OperationalRecord refreshed = await repository.SetClassificationAsync(
            record.Id,
            new OperationalRecordClassificationResult(OperationalRecordClassification.NeedsManualReview, false, "Stale classification."),
            "correlation-refresh",
            CancellationToken.None);

        failed.WorkflowState.Should().Be(OperationalRecordWorkflowState.JiraCreateFailed);
        refreshed.WorkflowState.Should().Be(OperationalRecordWorkflowState.JiraCreateFailed);
        refreshed.ReconciliationRequired.Should().BeTrue();
        refreshed.Version.Should().Be(failed.Version);
    }
}
