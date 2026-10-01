namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Version-bound suggestion, not an assignment or a capability grant.</summary>
public sealed record InUseReporterSuggestion(string State, string? ReporterLabel, InUseAssignee? Candidate,
    long RecordVersion, long SourceVersion, string? Fingerprint, string? MappingRevision,
    string? ReviewReference, IReadOnlyList<InUseRelatedRequestReporter> Relations)
{
    /// <summary>Original provider/tenant namespace, not inferred after a later refresh.</summary>
    public string? IdentityScope { get; init; }
    /// <summary>Reviewed mapping expiry at proposal time.</summary>
    public DateTimeOffset? MappingValidUntil { get; init; }
}

/// <summary>Explicit source-bound decision by the authenticated assignment actor.</summary>
public sealed record InUseReporterDecision(string Decision, InUseReporterSuggestion Proposal,
    Guid ActorId, string ActorLabel, DateTimeOffset At);
