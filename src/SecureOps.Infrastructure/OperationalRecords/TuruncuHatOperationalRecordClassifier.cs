using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Classifies records returned by the reviewed legacy source filter.</summary>
public sealed class TuruncuHatOperationalRecordClassifier : IOperationalRecordClassifier
{
    /// <inheritdoc />
    public OperationalRecordClassificationResult Classify(OperationalRecordSourceItem source)
    {
        bool valid = source.IsOpen
            && long.TryParse(source.SourceRecordId, out long id)
            && id > 0
            && !string.IsNullOrWhiteSpace(source.OrCode)
            && !string.IsNullOrWhiteSpace(source.Title)
            && !string.IsNullOrWhiteSpace(source.Description);

        return valid
            ? new OperationalRecordClassificationResult(
                OperationalRecordClassification.OperationalSupport,
                true,
                "Eligible under the reviewed legacy source filter.")
            : new OperationalRecordClassificationResult(
                OperationalRecordClassification.NeedsManualReview,
                false,
                "The source projection is invalid or no longer active.");
    }
}
