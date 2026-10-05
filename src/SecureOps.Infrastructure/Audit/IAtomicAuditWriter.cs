namespace SecureOps.Infrastructure.Audit;

/// <summary>Local audit writer that publishes a whole batch or leaves the audit unchanged on failure.</summary>
public interface IAtomicAuditWriter : IAuditWriter
{
    /// <summary>Appends all events atomically; cancellation or failure before completion appends none.</summary>
    public Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken);
}
