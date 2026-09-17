using SecureOps.Domain.Commands;

namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Bounded record-scoped evidence; missing historical facts stay absent.</summary>
public sealed record OperationHistoryResponse(IReadOnlyList<OperationEvidence> Events, SourceClosureObservation? Closure = null);
