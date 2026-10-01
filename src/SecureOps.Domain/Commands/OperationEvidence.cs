namespace SecureOps.Domain.Commands;

/// <summary>Minimal trusted profile captured at the action, never reconstructed from display-name matching.</summary>
public sealed record OperationActor(Guid Id, string Kind, string? DisplayName, string? Account, string? Mail);

/// <summary>Technical executor is not the initiating human or an assumed source-system closer.</summary>
public sealed record OperationExecutor(string Component, string Instance, string? Principal = null);

/// <summary>Versioned append-only operational evidence. Agent/delegation execution is not enabled.</summary>
public sealed record OperationEvidence(Guid Id, Guid CommandId, string RecordType, string RecordId, string Action,
    long InputVersion, DateTimeOffset OccurredAt, string Outcome, OperationActor Initiator,
    OperationExecutor? Executor, OperationActor? Verifier, string CorrelationId, string? CausationId = null,
    string? ExternalReference = null, string? SourceCloser = null, int SchemaVersion = 1, Guid? AssigneeId = null,
    OperationExecutor? VerificationExecutor = null);
