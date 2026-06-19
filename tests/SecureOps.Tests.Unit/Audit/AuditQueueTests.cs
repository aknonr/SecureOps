using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Audit;

public sealed class AuditQueueTests
{
    [Fact]
    public async Task QueuedAuditWriter_WhenQueueHasCapacity_DoesNotCallFileIoOnRequestPath()
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

        await Task.WhenAll(writes);

        queue.Count.Should().Be(15);
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

        await writer.WriteAsync(CreateEvent(1), CancellationToken.None);
        Func<Task> act = () => writer.WriteAsync(CreateEvent(2), CancellationToken.None);

        await act.Should().ThrowAsync<AuditWriteUnavailableException>();
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
