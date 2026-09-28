namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Minimal source-backed list states. No inference from absent rows or manual confirmation.</summary>
public static class InUseActivity
{
    /// <summary>A normalized value needs source provenance; untrusted display text is not evidence.</summary>
    public static bool Has(InUseEvidence? evidence, string value) => evidence?.Value == value
        && !string.IsNullOrWhiteSpace(evidence.Source);

    /// <summary>Pending is a review candidate, not permission to submit a corporate approval.</summary>
    public static string Status(InUseRecord record) =>
        record.ActivityVerifiedAt.HasValue || Has(record.Source.WasasActivity, "Completed") ? "Completed"
        : Has(record.Source.Lifecycle, "Closed") ? "OrClosed"
        : !record.Discarded && !record.SourceObservationMissing && !record.HasActiveExecution
            && Has(record.Source.WasasActivity, "Pending") ? "Pending" : "VerificationPending";
}
