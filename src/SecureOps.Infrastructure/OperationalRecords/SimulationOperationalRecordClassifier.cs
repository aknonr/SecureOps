using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Allows only the fixed synthetic TEST simulation dataset into the Jira workflow.</summary>
public sealed class SimulationOperationalRecordClassifier : IOperationalRecordClassifier
{
    /// <inheritdoc />
    public OperationalRecordClassificationResult Classify(OperationalRecordSourceItem sourceItem)
    {
        ArgumentNullException.ThrowIfNull(sourceItem);
        return SimulationOperationalRecordClient.IsSimulationSourceId(sourceItem.SourceRecordId)
            ? new OperationalRecordClassificationResult(
                OperationalRecordClassification.OperationalSupport,
                true,
                "Eligible synthetic TEST simulation record.")
            : new OperationalRecordClassificationResult(
                OperationalRecordClassification.NeedsManualReview,
                false,
                "The record is outside the fixed TEST simulation dataset.");
    }
}
