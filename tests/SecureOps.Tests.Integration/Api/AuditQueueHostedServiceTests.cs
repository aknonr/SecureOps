using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Api.Services;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Api;

public sealed class AuditQueueHostedServiceTests
{
    [Fact]
    public async Task DurableAcknowledgement_SinkFailure_FailsBatchAndMarksUnhealthy()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions
        {
            FailClosed = true,
            FlushIntervalSeconds = 1,
            Queue = new AuditQueueOptions
            {
                Capacity = 10,
                FullBehavior = "FailClosed"
            }
        });
        AuditQueue queue = new(options);
        AuditStoreHealthState healthState = new();
        CapturingLogger<AuditQueueHostedService> logger = new();
        AuditQueueHostedService service = new(
            queue,
            new FailingAuditEventSink(),
            options,
            healthState,
            logger);
        QueuedAuditWriter writer = new(
            queue,
            options,
            healthState,
            new CapturingLogger<QueuedAuditWriter>());

        Task[] writes = Enumerable.Range(0, 3)
            .Select(index => writer.WriteAsync(CreateEvent($"trace-{index}"), CancellationToken.None)).ToArray();
        await service.StartAsync(CancellationToken.None);

        foreach (Task write in writes)
        {
            Func<Task> pending = () => write.WaitAsync(TimeSpan.FromSeconds(3));
            await pending.Should().ThrowAsync<AuditWriteUnavailableException>();
        }

        Func<Task> act = () => writer.WriteAsync(CreateEvent("trace-after-failure"), CancellationToken.None);

        await act.Should().ThrowAsync<AuditWriteUnavailableException>()
            .WithMessage("*unhealthy*");
        healthState.LastErrorCode.Should().Be("AuditSinkUnavailable");
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Critical);
        queue.Count.Should().Be(0);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DurableAcknowledgement_SuccessfulFlush_CompletesAllCallers()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions());
        AuditQueue queue = new(options);
        AuditStoreHealthState health = new();
        ControlledAuditEventSink sink = new();
        using AuditQueueHostedService service = new(queue, sink, options, health, new CapturingLogger<AuditQueueHostedService>());
        QueuedAuditWriter writer = new(queue, options, health, new CapturingLogger<QueuedAuditWriter>());
        Task[] writes = Enumerable.Range(0, 3)
            .Select(index => writer.WriteAsync(CreateEvent($"trace-{index}"), default)).ToArray();
        try
        {
            await service.StartAsync(default);
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            writes.Should().OnlyContain(write => !write.IsCompleted);
            sink.Events.Should().HaveCount(3);
            queue.Count.Should().Be(0);
            sink.Release.SetResult();

            await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(3));
            health.IsHealthy.Should().BeTrue();
        }
        finally
        {
            await service.StopAsync(default);
        }
    }

    [Fact]
    public async Task DurableAcknowledgement_Shutdown_FailsInFlightAndPendingItems()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions());
        AuditQueue queue = new(options);
        AuditStoreHealthState health = new();
        ControlledAuditEventSink sink = new();
        using AuditQueueHostedService service = new(queue, sink, options, health, new CapturingLogger<AuditQueueHostedService>());
        QueuedAuditWriter writer = new(queue, options, health, new CapturingLogger<QueuedAuditWriter>());
        Task inFlight = writer.WriteAsync(CreateEvent("trace-in-flight"), default);
        await service.StartAsync(default);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task pending = writer.WriteAsync(CreateEvent("trace-pending"), default);
        queue.Count.Should().Be(1);

        await service.StopAsync(default);

        foreach (Task write in new[] { inFlight, pending })
        {
            Func<Task> act = () => write.WaitAsync(TimeSpan.FromSeconds(3));
            await act.Should().ThrowAsync<AuditWriteUnavailableException>();
        }

        queue.Count.Should().Be(0);
        Func<Task> afterStop = () => writer.WriteAsync(CreateEvent("trace-stopped"), default);
        await afterStop.Should().ThrowAsync<AuditWriteUnavailableException>();
    }

    [Fact]
    public async Task DurableAcknowledgement_ShutdownBeforeStart_FailsPendingItems()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions());
        AuditQueue queue = new(options);
        AuditStoreHealthState health = new();
        using AuditQueueHostedService service = new(queue, new ControlledAuditEventSink(), options, health, new CapturingLogger<AuditQueueHostedService>());
        QueuedAuditWriter writer = new(queue, options, health, new CapturingLogger<QueuedAuditWriter>());
        Task pending = writer.WriteAsync(CreateEvent("trace-pending"), default);

        await service.StopAsync(default);

        Func<Task> act = () => pending.WaitAsync(TimeSpan.FromSeconds(3));
        await act.Should().ThrowAsync<AuditWriteUnavailableException>();
        queue.Count.Should().Be(0);
    }

    private static AuditEvent CreateEvent(string correlationId)
    {
        return new AuditEvent
        {
            Actor = "CONTOSO\\lead.user",
            Action = "IdentityLookupRequested",
            CorrelationId = correlationId,
            Details = new { normalizedAccount = "sample-admin", resultStatus = "Requested" }
        };
    }

    private sealed class ControlledAuditEventSink : IAuditEventSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<AuditEvent> Events { get; private set; } = [];

        public async Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
        {
            Events = events;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailingAuditEventSink : IAuditEventSink
    {
        public Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
        {
            throw new IOException("Configured test sink failure.");
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, eventId.Id));
        }
    }

    private sealed record LogEntry(LogLevel Level, int EventId);
}
