namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Persists batches of audit events.
/// </summary>
public interface IAuditEventSink
{
    /// <summary>
    /// Persists a batch of audit events.
    /// </summary>
    /// <param name="events">Events to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the batch has been persisted.</returns>
    public Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken);
}
