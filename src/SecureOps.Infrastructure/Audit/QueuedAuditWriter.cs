using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Audit writer that only enqueues events on the request path.
/// </summary>
public sealed class QueuedAuditWriter : IAuditWriter
{
    private readonly AuditQueue _queue;
    private readonly AuditOptions _options;
    private readonly IAuditStoreHealthState _healthState;
    private readonly ILogger<QueuedAuditWriter> _logger;

    /// <summary>
    /// Initializes a queued audit writer.
    /// </summary>
    /// <param name="queue">Audit queue.</param>
    /// <param name="options">Audit options.</param>
    /// <param name="healthState">Audit store health state.</param>
    /// <param name="logger">Logger.</param>
    public QueuedAuditWriter(
        AuditQueue queue,
        IOptions<AuditOptions> options,
        IAuditStoreHealthState healthState,
        ILogger<QueuedAuditWriter> logger)
    {
        _queue = queue;
        _options = options.Value;
        _healthState = healthState;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_healthState.IsHealthy && IsFailClosed())
        {
            throw new AuditWriteUnavailableException("Audit store is unhealthy.");
        }

        if (_queue.TryEnqueue(auditEvent))
        {
            return Task.CompletedTask;
        }

        if (IsFailClosed())
        {
            throw new AuditWriteUnavailableException("Audit queue is full.");
        }

        _logger.LogCritical(
            "Audit queue is full; dropping audit event {AuditAction}. CorrelationId: {CorrelationId}",
            auditEvent.Action,
            auditEvent.CorrelationId);
        return Task.CompletedTask;
    }

    private bool IsFailClosed()
    {
        return _options.FailClosed
            || string.Equals(_options.Queue.FullBehavior, "FailClosed", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Direct audit writer used only when queueing is disabled.
/// </summary>
public sealed class DirectAuditWriter : IAuditWriter
{
    private readonly IAuditEventSink _sink;
    private readonly IAuditStoreHealthState _healthState;

    /// <summary>
    /// Initializes a direct audit writer.
    /// </summary>
    /// <param name="sink">Audit event sink.</param>
    /// <param name="healthState">Audit store health state.</param>
    public DirectAuditWriter(IAuditEventSink sink, IAuditStoreHealthState healthState)
    {
        _sink = sink;
        _healthState = healthState;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _sink.WriteBatchAsync([auditEvent], cancellationToken);
            _healthState.MarkHealthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _healthState.MarkUnhealthy("AuditSinkUnavailable");
            throw;
        }
    }
}

/// <summary>
/// Exception thrown when audit cannot accept an event.
/// </summary>
public sealed class AuditWriteUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new audit write unavailable exception.
    /// </summary>
    /// <param name="message">Failure message.</param>
    public AuditWriteUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a safe audit failure while retaining the internal cause.</summary>
    public AuditWriteUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
