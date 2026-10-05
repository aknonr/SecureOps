using System.Collections;
using FluentAssertions;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Tests.Unit.Audit;

public sealed class InMemoryAuditWriterTests
{
    [Fact]
    public async Task Batch_EnumerationFailureDoesNotAppendAPrefix()
    {
        InMemoryAuditWriter writer = new();
        AuditEvent existing = new() { Actor = "synthetic", Action = "Existing" };
        await writer.WriteAsync(existing, TestContext.Current.CancellationToken);

        Action append = () => writer.WriteBatchAsync(new FailingBatch(), TestContext.Current.CancellationToken);

        append.Should().Throw<InvalidOperationException>();
        writer.Events.Should().ContainSingle().Which.Should().BeSameAs(existing);
    }

    [Fact]
    public void Batch_CancelledBeforePublicationAppendsNothing()
    {
        InMemoryAuditWriter writer = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Action append = () => writer.WriteBatchAsync([new AuditEvent { Actor = "synthetic", Action = "Cancelled" }], cancellation.Token);

        append.Should().Throw<OperationCanceledException>();
        writer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Batch_SuccessPublishesAllEventsInOrder()
    {
        InMemoryAuditWriter writer = new();
        AuditEvent first = new() { Actor = "synthetic", Action = "First" };
        AuditEvent second = new() { Actor = "synthetic", Action = "Second" };

        await writer.WriteBatchAsync([first, second], TestContext.Current.CancellationToken);

        writer.Events.Should().Equal(first, second);
    }

    private sealed class FailingBatch : IReadOnlyCollection<AuditEvent>
    {
        public int Count => 2;
        public IEnumerator<AuditEvent> GetEnumerator()
        {
            yield return new AuditEvent { Actor = "synthetic", Action = "UncommittedPrefix" };
            throw new InvalidOperationException("Synthetic batch preparation failure.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
