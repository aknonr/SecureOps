using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Keeps real source records in manual review until a Jira-eligibility rule is approved.</summary>
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

        return new OperationalRecordClassificationResult(
            OperationalRecordClassification.NeedsManualReview,
            false,
            valid
                ? "The real source record requires operator review before Jira eligibility is established."
                : "The source projection is invalid or no longer active.");
    }
}
