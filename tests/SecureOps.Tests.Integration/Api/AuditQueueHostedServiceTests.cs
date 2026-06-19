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
    public async Task BackgroundSinkFailure_MarksAuditUnhealthyAndNewFailClosedWritesFail()
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

        await writer.WriteAsync(CreateEvent("trace-accepted"), CancellationToken.None);
        await service.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => !healthState.IsHealthy);
        Func<Task> act = () => writer.WriteAsync(CreateEvent("trace-after-failure"), CancellationToken.None);

        await act.Should().ThrowAsync<AuditWriteUnavailableException>()
            .WithMessage("*unhealthy*");
        healthState.LastErrorCode.Should().Be("AuditSinkUnavailable");
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Critical);

        await service.StopAsync(CancellationToken.None);
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, timeout.Token);
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
