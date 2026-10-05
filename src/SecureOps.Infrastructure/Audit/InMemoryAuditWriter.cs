namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// In-memory audit writer for development and tests.
/// </summary>
public sealed class InMemoryAuditWriter : IAtomicAuditWriter, IAuditEventSink
{
    private readonly Lock _gate = new();
    private readonly List<AuditEvent> _events = [];

    /// <summary>
    /// Gets the captured audit events.
    /// </summary>
    public IReadOnlyCollection<AuditEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => WriteBatchAsync([auditEvent], cancellationToken);

    /// <inheritdoc />
    public Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
    {
        AuditEvent[] batch = events.ToArray();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.EnsureCapacity(checked(_events.Count + batch.Length));
            _events.AddRange(batch);
        }

        return Task.CompletedTask;
    }
}
