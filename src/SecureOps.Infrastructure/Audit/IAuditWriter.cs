namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Writes append-only audit events.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Writes one audit event.
    /// </summary>
    /// <param name="auditEvent">Audit event to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the write finishes.</returns>
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
