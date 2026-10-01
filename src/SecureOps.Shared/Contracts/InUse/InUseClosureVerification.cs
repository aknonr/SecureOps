namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Evidence-based presentation and local attestation eligibility; never infers closure from acknowledgement.</summary>
public static class InUseClosureVerification
{
    /// <summary>Only explicit activity verification, never acknowledgement or a local attestation.</summary>
    public static bool ActivityVerified(InUseExecution operation) => operation.VerificationMode == "WasasActivityManual"
        && operation.Evidence.LastOrDefault(e => e.Step == "Bpm")?.Outcome == "Verified";
    /// <summary>True only for a completed execution with authoritative final source evidence.</summary>
    public static bool SourceVerified(InUseExecution operation) => operation.State == "Completed"
        && operation.Evidence.LastOrDefault(e => e.Step == "Closure")?.Outcome == "Verified";

    /// <summary>Returns the attributed manual event without changing its verification category.</summary>
    public static InUseStepEvidence? ManualConfirmation(InUseExecution operation) => operation.Evidence
        .LastOrDefault(e => e.Step == "ManualVerification" && e.Outcome == "ManuallyConfirmed" && e.ConfirmedBy.HasValue);

    /// <summary>Only stopped closure attempts with independently verified attachment proof can be attested.</summary>
    public static bool CanConfirm(InUseExecution operation) => !string.IsNullOrWhiteSpace(operation.SourceCode)
        && !ActivityVerified(operation)
        && operation.State is "Unconfirmed" or "Unknown" or "Blocked"
        && ManualConfirmation(operation) is null
        && operation.Evidence.LastOrDefault(e => e.Step == "Attachment")?.Outcome == "Verified"
        && operation.Evidence.LastOrDefault(e => e.Step == "Bpm")?.Outcome is "Acknowledged" or "Verified" or "Unknown";

    /// <summary>Whether the closure request's response is uncertain, rather than an earlier upload/property failure.</summary>
    public static bool ClosureUnknown(InUseExecution operation) => operation.Evidence.LastOrDefault(e => e.Step == "Bpm")?.Outcome == "Unknown";

    /// <summary>Request acknowledgement is not final OR state evidence.</summary>
    public static bool Acknowledged(InUseExecution operation) => operation.Evidence.LastOrDefault(e => e.Step == "Bpm")?.Outcome is "Acknowledged" or "Verified";
}
