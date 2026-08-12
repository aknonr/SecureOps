namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Deterministic strategy boundary for approved classification rules.</summary>
public interface IOperationalRecordClassifier
{
    /// <summary>Classifies one imported source item.</summary>
    public OperationalRecordClassificationResult Classify(OperationalRecordSourceItem sourceItem);
}
