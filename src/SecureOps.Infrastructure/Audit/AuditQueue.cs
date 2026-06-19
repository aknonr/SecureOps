using System.Threading.Channels;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Bounded in-memory audit queue.
/// </summary>
public sealed class AuditQueue : IAuditQueueMetrics
{
    private readonly Channel<AuditEvent> _channel;
    private readonly int _capacity;
    private int _count;

    /// <summary>
    /// Initializes a new audit queue.
    /// </summary>
    /// <param name="options">Audit options.</param>
    public AuditQueue(Microsoft.Extensions.Options.IOptions<AuditOptions> options)
    {
        _capacity = options.Value.Queue.Capacity;
        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public bool Enabled => true;

    /// <inheritdoc />
    public int? Capacity => _capacity;

    /// <inheritdoc />
    public int? Count => Volatile.Read(ref _count);

    /// <summary>
    /// Gets the channel reader.
    /// </summary>
    public ChannelReader<AuditEvent> Reader => _channel.Reader;

    /// <summary>
    /// Attempts to enqueue an event without blocking on persistence.
    /// </summary>
    /// <param name="auditEvent">Audit event.</param>
    /// <returns>True when queued; otherwise false.</returns>
    public bool TryEnqueue(AuditEvent auditEvent)
    {
        if (Volatile.Read(ref _count) >= _capacity)
        {
            return false;
        }

        if (!_channel.Writer.TryWrite(auditEvent))
        {
            return false;
        }

        Interlocked.Increment(ref _count);
        return true;
    }

    /// <summary>
    /// Marks one event as removed from the queue.
    /// </summary>
    public void MarkDequeued()
    {
        Interlocked.Decrement(ref _count);
    }
}

/// <summary>
/// Read-only audit queue metrics.
/// </summary>
public interface IAuditQueueMetrics
{
    /// <summary>
    /// Whether queueing is enabled.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Queue capacity.
    /// </summary>
    public int? Capacity { get; }

    /// <summary>
    /// Current queued event count.
    /// </summary>
    public int? Count { get; }
}

/// <summary>
/// Empty audit queue metrics for direct writers.
/// </summary>
public sealed class NullAuditQueueMetrics : IAuditQueueMetrics
{
    /// <inheritdoc />
    public bool Enabled => false;

    /// <inheritdoc />
    public int? Capacity => null;

    /// <inheritdoc />
    public int? Count => null;
}
