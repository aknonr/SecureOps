using FluentAssertions;
using SecureOps.Infrastructure.Commands;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class CommandIdempotencyStoreTests
{
    [Fact]
    public async Task SameKey_ReplaysCompletedStateAcrossServiceInstances()
    {
        ManualTimeProvider time = new();
        InMemoryCommandIdempotencyStore durableBoundary = new(time);
        ICommandIdempotencyStore firstProcess = durableBoundary;
        ICommandIdempotencyStore restartedProcess = durableBoundary;

        CommandBeginResult first = await firstProcess.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-a", TimeSpan.FromMinutes(2), CancellationToken.None);
        await firstProcess.CompleteAsync("Create", "target-1", "opaque-key-00000001", first.ExecutionToken!.Value, CancellationToken.None);
        CommandBeginResult replay = await restartedProcess.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-a", TimeSpan.FromMinutes(2), CancellationToken.None);

        first.Disposition.Should().Be(CommandBeginDisposition.Acquired);
        replay.Disposition.Should().Be(CommandBeginDisposition.Completed);
    }

    [Fact]
    public async Task SameKey_DifferentActor_IsRejected()
    {
        InMemoryCommandIdempotencyStore store = new(new ManualTimeProvider());
        _ = await store.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-a", TimeSpan.FromMinutes(2), CancellationToken.None);

        CommandBeginResult result = await store.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-b", TimeSpan.FromMinutes(2), CancellationToken.None);

        result.Disposition.Should().Be(CommandBeginDisposition.ActorConflict);
    }

    [Fact]
    public async Task ExpiredExecutionLease_CanBeRecovered()
    {
        ManualTimeProvider time = new();
        InMemoryCommandIdempotencyStore store = new(time);
        _ = await store.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-a", TimeSpan.FromMinutes(2), CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(3));

        CommandBeginResult result = await store.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-b", TimeSpan.FromMinutes(2), CancellationToken.None);

        result.Disposition.Should().Be(CommandBeginDisposition.Acquired);
    }

    [Fact]
    public async Task ExpiredOwner_CannotCompleteReacquiredExecution()
    {
        ManualTimeProvider time = new();
        InMemoryCommandIdempotencyStore store = new(time);
        CommandBeginResult first = await store.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-a", TimeSpan.FromMinutes(2), CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(3));
        CommandBeginResult second = await store.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-b", TimeSpan.FromMinutes(2), CancellationToken.None);

        await store.CompleteAsync("Create", "target-1", "opaque-key-00000001", first.ExecutionToken!.Value, CancellationToken.None);
        await store.FailAsync("Create", "target-1", "opaque-key-00000001", second.ExecutionToken!.Value, "SyntheticFailure", CancellationToken.None);
        CommandBeginResult replay = await store.TryBeginAsync("Create", "target-1", "opaque-key-00000001", "actor-b", TimeSpan.FromMinutes(2), CancellationToken.None);

        replay.Disposition.Should().Be(CommandBeginDisposition.Failed);
        replay.ErrorCode.Should().Be("SyntheticFailure");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
