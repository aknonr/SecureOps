namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Minimal source-backed list states. No inference from absent rows or manual confirmation.</summary>
public static class InUseActivity
{
    /// <summary>A normalized value needs source provenance; untrusted display text is not evidence.</summary>
    public static bool Has(InUseEvidence? evidence, string value) => evidence?.Value == value
        && !string.IsNullOrWhiteSpace(evidence.Source);

    /// <summary>
    /// The source says the record is no longer assigned to the WASAS group (server-classified, ADR-0020 amendment
    /// 2026-10-17). It leaves the main list and is tracked only: a group change is not proof of approval, because
    /// sending back changes the group too.
    /// </summary>
    public static bool LeftTeam(InUseEvidence? currentStage) => Has(currentStage, NotAssignedToWasas);

    /// <summary>Server-classified <c>CurrentStage</c> value: a filled assigned group differs from the configured WASAS group.</summary>
    public const string NotAssignedToWasas = "NotAssignedToWasas";

    /// <summary>Pending is a review candidate, not permission to submit a corporate approval.</summary>
    public static string Status(InUseRecord record) =>
        record.ActivityVerifiedAt.HasValue || Has(record.Source.WasasActivity, "Completed") ? "Completed"
        : Has(record.Source.Lifecycle, "Closed") ? "OrClosed"
        : LeftTeam(record.Source.CurrentStage) ? "NotAssigned"
        : !record.Discarded && !record.SourceObservationMissing && !record.HasActiveExecution
            && Has(record.Source.WasasActivity, "Pending") ? "Pending" : "VerificationPending";
}
