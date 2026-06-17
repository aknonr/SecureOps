using System.Collections.Concurrent;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// In-memory audit writer for development and tests.
/// </summary>
public sealed class InMemoryAuditWriter : IAuditWriter
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    /// <summary>
    /// Gets the captured audit events.
    /// </summary>
    public IReadOnlyCollection<AuditEvent> Events => _events.ToArray();

    /// <inheritdoc />
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }
}
