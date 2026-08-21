using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Exact synthetic-only classifier for the configured Fake source dataset.</summary>
public sealed class FakeOperationalRecordClassifier : IOperationalRecordClassifier
{
    /// <inheritdoc />
    public OperationalRecordClassificationResult Classify(OperationalRecordSourceItem sourceItem)
    {
        ArgumentNullException.ThrowIfNull(sourceItem);
        return FakeOperationalRecordClient.IsSyntheticSourceId(sourceItem.SourceRecordId)
            ? new OperationalRecordClassificationResult(
                OperationalRecordClassification.OperationalSupport,
                true,
                "Eligible synthetic workflow record.")
            : new OperationalRecordClassificationResult(
                OperationalRecordClassification.NeedsManualReview,
                false,
                "The record is not part of the configured synthetic dataset.");
    }
}
