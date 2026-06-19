using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;

namespace SecureOps.Api.Services;

/// <summary>
/// Background service that flushes queued audit events to the configured sink.
/// </summary>
public sealed class AuditQueueHostedService : BackgroundService
{
    private const int _maxBatchSize = 100;
    private readonly AuditQueue _queue;
    private readonly IAuditEventSink _sink;
    private readonly AuditOptions _options;
    private readonly IAuditStoreHealthState _healthState;
    private readonly ILogger<AuditQueueHostedService> _logger;

    /// <summary>
    /// Initializes a new audit queue hosted service.
    /// </summary>
    /// <param name="queue">Audit queue.</param>
    /// <param name="sink">Audit event sink.</param>
    /// <param name="options">Audit options.</param>
    /// <param name="healthState">Audit store health state.</param>
    /// <param name="logger">Logger.</param>
    public AuditQueueHostedService(
        AuditQueue queue,
        IAuditEventSink sink,
        Microsoft.Extensions.Options.IOptions<AuditOptions> options,
        IAuditStoreHealthState healthState,
        ILogger<AuditQueueHostedService> logger)
    {
        _queue = queue;
        _sink = sink;
        _options = options.Value;
        _healthState = healthState;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var flushInterval = TimeSpan.FromSeconds(_options.FlushIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            AuditEvent first;
            try
            {
                first = await _queue.Reader.ReadAsync(stoppingToken);
                _queue.MarkDequeued();
            }
            catch (OperationCanceledException)
            {
                break;
            }

            List<AuditEvent> batch = [first];
            DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(flushInterval);

            while (batch.Count < _maxBatchSize
                   && DateTimeOffset.UtcNow < deadline
                   && _queue.Reader.TryRead(out AuditEvent? next))
            {
                _queue.MarkDequeued();
                batch.Add(next);
            }

            try
            {
                await _sink.WriteBatchAsync(batch, stoppingToken);
                _healthState.MarkHealthy();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _healthState.MarkUnhealthy("AuditSinkUnavailable");
                _logger.LogCritical(
                    ex,
                    "Audit background persistence failed for {AuditEventCount} event(s).",
                    batch.Count);
            }
        }
    }
}
