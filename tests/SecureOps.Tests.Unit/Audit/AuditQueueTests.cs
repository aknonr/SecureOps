using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Audit;

public sealed class AuditQueueTests
{
    [Fact]
    public async Task QueuedAuditWriter_DurableAcknowledgement_CompletesAfterPersistence()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions
        {
            Queue = new AuditQueueOptions { Capacity = 15, FullBehavior = "FailClosed" }
        });
        AuditQueue queue = new(options);
        QueuedAuditWriter writer = new(queue, options, new AuditStoreHealthState(), NullLogger<QueuedAuditWriter>.Instance);

        Task[] writes = Enumerable.Range(0, 15)
            .Select(index => writer.WriteAsync(CreateEvent(index), CancellationToken.None))
            .ToArray();

        queue.Count.Should().Be(15);
        writes.Should().OnlyContain(write => !write.IsCompleted);
        while (queue.Reader.TryRead(out AuditQueueItem? item))
        {
            queue.MarkDequeued();
            item.Succeed();
        }

        await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(3));
        queue.Count.Should().Be(0);
        queue.Capacity.Should().Be(15);
    }

    [Fact]
    public async Task QueuedAuditWriter_WhenQueueFullAndFailClosed_Throws()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions
        {
            FailClosed = true,
            Queue = new AuditQueueOptions { Capacity = 1, FullBehavior = "FailClosed" }
        });
        AuditQueue queue = new(options);
        QueuedAuditWriter writer = new(queue, options, new AuditStoreHealthState(), NullLogger<QueuedAuditWriter>.Instance);

        queue.TryEnqueue(CreateEvent(1)).Should().BeTrue();
        Func<Task> act = () => writer.WriteAsync(CreateEvent(2), CancellationToken.None);

        await act.Should().ThrowAsync<AuditWriteUnavailableException>();
        queue.Count.Should().Be(1);
        queue.FailPending();
    }

    [Fact]
    public async Task QueuedAuditWriter_DurableAcknowledgement_TimeoutFailsClosed()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions());
        AuditQueue queue = new(options);
        QueuedAuditWriter writer = new(queue, options, new AuditStoreHealthState(), NullLogger<QueuedAuditWriter>.Instance);

        Func<Task> act = () => writer.WriteAsync(CreateEvent(1), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(35));

        try
        {
            await act.Should().ThrowAsync<AuditWriteUnavailableException>();
        }
        finally
        {
            queue.FailPending();
        }

        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task QueuedAuditWriter_DurableAcknowledgement_CancellationFailsClosed()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions());
        AuditQueue queue = new(options);
        QueuedAuditWriter writer = new(queue, options, new AuditStoreHealthState(), NullLogger<QueuedAuditWriter>.Instance);
        using CancellationTokenSource cancellation = new();
        Task write = writer.WriteAsync(CreateEvent(1), cancellation.Token);
        queue.Count.Should().Be(1);

        cancellation.Cancel();

        Func<Task> act = () => write.WaitAsync(TimeSpan.FromSeconds(3));
        await act.Should().ThrowAsync<AuditWriteUnavailableException>();
        queue.FailPending();
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task QueuedAuditWriter_FullNonProductionQueue_KeepsExplicitDropBehavior()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions
        {
            FailClosed = false,
            Queue = new AuditQueueOptions { Capacity = 1, FullBehavior = "DropAndCriticalLog" }
        });
        AuditQueue queue = new(options);
        queue.TryEnqueue(CreateEvent(1)).Should().BeTrue();
        QueuedAuditWriter writer = new(queue, options, new AuditStoreHealthState(), NullLogger<QueuedAuditWriter>.Instance);

        await writer.WriteAsync(CreateEvent(2), CancellationToken.None);

        queue.Count.Should().Be(1);
        queue.FailPending();
    }

    [Fact]
    public async Task QueuedAuditWriter_WhenAuditStoreUnhealthyAndFailClosed_ThrowsBeforeEnqueue()
    {
        IOptions<AuditOptions> options = Options.Create(new AuditOptions
        {
            FailClosed = true,
            Queue = new AuditQueueOptions { Capacity = 10, FullBehavior = "FailClosed" }
        });
        AuditQueue queue = new(options);
        AuditStoreHealthState healthState = new();
        healthState.MarkUnhealthy("AuditSinkUnavailable");
        QueuedAuditWriter writer = new(queue, options, healthState, NullLogger<QueuedAuditWriter>.Instance);

        Func<Task> act = () => writer.WriteAsync(CreateEvent(1), CancellationToken.None);

        await act.Should().ThrowAsync<AuditWriteUnavailableException>()
            .WithMessage("*unhealthy*");
        queue.Count.Should().Be(0);
    }

    private static AuditEvent CreateEvent(int index)
    {
        return new AuditEvent
        {
            Actor = "CONTOSO\\lead.user",
            Action = "IdentityLookupRequested",
            CorrelationId = $"trace-{index}",
            Details = new { normalizedAccount = $"pam{index}", resultStatus = "Requested" }
        };
    }
}
