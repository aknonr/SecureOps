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
}
