using FluentAssertions;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class OperationalRecordClassifierTests
{
    [Fact]
    public void Classify_WithoutApprovedRules_FailsClosedToManualReview()
    {
        ManualReviewOperationalRecordClassifier classifier = new();

        OperationalRecordClassificationResult result = classifier.Classify(TestRecord.SourceItem());

        result.Classification.Should().Be(OperationalRecordClassification.NeedsManualReview);
        result.JiraEligible.Should().BeFalse();
        result.EligibilityReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FakeProvider_ReturnsDeterministicSyntheticScenarios()
    {
        FakeOperationalRecordClient client = new();

        IReadOnlyList<OperationalRecordSourceItem> first = await client.GetActiveAsync(100, CancellationToken.None);
        IReadOnlyList<OperationalRecordSourceItem> second = await client.GetActiveAsync(100, CancellationToken.None);

        first.Should().Equal(second);
        first.Should().HaveCount(4);
        first.Should().OnlyContain(item => item.SourceRecordId.StartsWith("synthetic-or-", StringComparison.Ordinal));
        (await client.GetByIdAsync(first.Single(item => item.OrCode == "SYN-OR-200").SourceRecordId, CancellationToken.None))!
            .VersionToken.Should().Be("stale-v2");
        (await client.GetByIdAsync(first.Single(item => item.OrCode == "SYN-OR-300").SourceRecordId, CancellationToken.None))!
            .IsOpen.Should().BeFalse();
        (await client.GetByIdAsync(first.Single(item => item.OrCode == "SYN-OR-400").SourceRecordId, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task FakeProvider_ClassifierAndRequesterAreExactAndSyntheticOnly()
    {
        FakeOperationalRecordClient client = new();
        OperationalRecordSourceItem source = (await client.GetActiveAsync(1, CancellationToken.None)).Single();
        FakeOperationalRecordClassifier classifier = new();
        FakeRequesterResolver requester = new();

        classifier.Classify(source).JiraEligible.Should().BeTrue();
        classifier.Classify(source with { SourceRecordId = "not-synthetic" }).JiraEligible.Should().BeFalse();
        (await requester.ResolveExactAsync(source.Requester!, CancellationToken.None)).Status.Should().Be(RequesterResolutionStatus.Found);
        (await requester.ResolveExactAsync("unknown.requester", CancellationToken.None)).Status.Should().Be(RequesterResolutionStatus.NotFound);
    }

    [Fact]
    public async Task DisabledProvider_FailsClosedWithoutExternalIo()
    {
        DisabledOperationalRecordClient client = new();

        Func<Task> act = async () => await client.GetActiveAsync(100, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(SecureOps.Shared.Contracts.Api.OperationalErrorCodes.OperationalSourceUnavailable);
        exception.Retryable.Should().BeFalse();
    }
}
