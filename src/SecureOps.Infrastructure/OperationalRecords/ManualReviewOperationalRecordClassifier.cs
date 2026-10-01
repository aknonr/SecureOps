using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Fail-closed classifier used until approved corporate rules are supplied.</summary>
public sealed class ManualReviewOperationalRecordClassifier : IOperationalRecordClassifier
{
    /// <inheritdoc />
    public OperationalRecordClassificationResult Classify(OperationalRecordSourceItem sourceItem)
    {
        ArgumentNullException.ThrowIfNull(sourceItem);
        return new OperationalRecordClassificationResult(
            OperationalRecordClassification.NeedsManualReview,
            false,
            "No approved deterministic classification rule matched.");
    }
}
