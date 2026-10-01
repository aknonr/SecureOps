namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Exact report confirmation; a different command ID cannot duplicate the same logical snapshot.</summary>
public sealed record StartInUseExecutionRequest(Guid CommandId, long ExpectedVersion, string ReportSha256);

/// <summary>Safe operator readiness, separate from local report readiness.</summary>
public sealed record InUseExecutionReadiness(bool Available, string Code, string Explanation);

/// <summary>Durable original human intent and frozen reviewed source context.</summary>
public sealed record InUseExecutionIntent(Guid OperationId, Guid RecordId, long ReportVersion, long SourceVersion,
    string SourceHash, string ReportSha256, string FileName, Guid InitiatorId, string InitiatorLabel,
    DateTimeOffset RequestedAt, string ConfigurationFingerprint, string ReviewHash, InUseSource Source)
{
    /// <summary>Frozen at submission. Missing historical metadata retains the original readback contract.</summary>
    public string VerificationMode { get; init; } = "SourceReadback";
}

/// <summary>Persisted step result, separate from transport acknowledgement and final OR observation.</summary>
public sealed record InUseStepEvidence(string Step, string Outcome, string? RemoteId, string Code,
    DateTimeOffset At, string Executor)
{
    /// <summary>Trusted confirming human, distinct from the original initiator and technical executor.</summary>
    public Guid? ConfirmedBy { get; init; }
    /// <summary>Trusted display label frozen when manual confirmation is recorded.</summary>
    public string? ConfirmedByLabel { get; init; }
}

/// <summary>Operator-visible state without workbook bytes, secret configuration or executor leases.</summary>
public sealed record InUseExecution(Guid OperationId, Guid RecordId, long ReportVersion, string ReportSha256,
    string State, int Step, long Revision, Guid InitiatorId, string InitiatorLabel, DateTimeOffset CreatedAt,
    IReadOnlyList<InUseStepEvidence> Evidence)
{
    /// <summary>Exact OR from the immutable intent, not supplied by the confirming browser.</summary>
    public string SourceCode { get; init; } = "";
    /// <summary>Frozen intent meaning; historical closure requests retain their original terminology.</summary>
    public string VerificationMode { get; init; } = "SourceReadback";
}

/// <summary>Local human attestation only; never an authoritative source outcome or a retry command.</summary>
public sealed record ConfirmInUseClosureRequest(Guid OperationId, long ExpectedRevision, string SourceCode);

/// <summary>Current readiness and most recent durable execution; old blocked intents remain in the legacy record.</summary>
public sealed record InUseExecutionStatus(InUseExecutionReadiness Readiness, InUseExecution? Operation);
