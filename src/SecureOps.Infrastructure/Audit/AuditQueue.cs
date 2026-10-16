using System.Threading.Channels;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Bounded in-memory audit queue.
/// </summary>
public sealed class AuditQueue : IAuditQueueMetrics
{
    private readonly Channel<AuditQueueItem> _channel;
    private readonly int _capacity;
    private readonly object _enqueueGate = new();
    private int _count;

    /// <summary>
    /// Initializes a new audit queue.
    /// </summary>
    /// <param name="options">Audit options.</param>
    public AuditQueue(Microsoft.Extensions.Options.IOptions<AuditOptions> options)
    {
        _capacity = options.Value.Queue.Capacity;
        _channel = Channel.CreateBounded<AuditQueueItem>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
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
    public ChannelReader<AuditQueueItem> Reader => _channel.Reader;

    /// <summary>
    /// Attempts to enqueue an event without blocking on persistence.
    /// </summary>
    /// <param name="auditEvent">Audit event.</param>
    /// <returns>True when queued; otherwise false.</returns>
    public bool TryEnqueue(AuditEvent auditEvent) => TryEnqueue(new AuditQueueItem(auditEvent));

    /// <summary>Attempts to enqueue an item awaiting durable acknowledgement.</summary>
    public bool TryEnqueue(AuditQueueItem item)
    {
        lock (_enqueueGate)
        {
            if (Volatile.Read(ref _count) >= _capacity)
            {
                return false;
            }

            Interlocked.Increment(ref _count);
            if (!_channel.Writer.TryWrite(item))
            {
                Interlocked.Decrement(ref _count);
                return false;
            }

            return true;
        }
    }

    /// <summary>Stops acceptance and fails all items still awaiting processing.</summary>
    public void FailPending()
    {
        _channel.Writer.TryComplete();
        while (_channel.Reader.TryRead(out AuditQueueItem? item))
        {
            MarkDequeued();
            item.Fail();
        }
    }

    /// <summary>
    /// Marks one event as removed from the queue.
    /// </summary>
    public void MarkDequeued()
    {
        Interlocked.Decrement(ref _count);
    }
}

/// <summary>An audit event and its durable acknowledgement.</summary>
public sealed class AuditQueueItem(AuditEvent auditEvent)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the event to persist.</summary>
    public AuditEvent Event { get; } = auditEvent;

    /// <summary>Gets the durable acknowledgement task.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Completes the durable acknowledgement after persistence succeeds.</summary>
    public void Succeed() => _completion.TrySetResult();

    /// <summary>Fails the durable acknowledgement when persistence is unavailable.</summary>
    public void Fail()
    {
        _completion.TrySetException(new AuditWriteUnavailableException("Audit persistence was not acknowledged."));
        _ = _completion.Task.Exception;
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
