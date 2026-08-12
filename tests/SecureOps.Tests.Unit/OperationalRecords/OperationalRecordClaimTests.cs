using FluentAssertions;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class OperationalRecordClaimTests
{
    [Fact]
    public async Task TwoActors_CannotOwnSameActiveClaim()
    {
        ManualTimeProvider time = new();
        InMemoryOperationalRecordRepository repository = new(time);
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);

        WorkflowClaimResult first = await repository.TryClaimAsync(record.Id, "actor-a", TimeSpan.FromMinutes(2), "correlation-a", CancellationToken.None);
        WorkflowClaimResult second = await repository.TryClaimAsync(record.Id, "actor-b", TimeSpan.FromMinutes(2), "correlation-b", CancellationToken.None);

        first.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        second.Disposition.Should().Be(WorkflowAcquireDisposition.AlreadyClaimed);
        second.Record!.ClaimedBy.Should().Be("actor-a");
    }

    [Fact]
    public async Task ExpiredClaim_CanBeRecoveredByAnotherActor()
    {
        ManualTimeProvider time = new();
        InMemoryOperationalRecordRepository repository = new(time);
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        _ = await repository.TryClaimAsync(record.Id, "actor-a", TimeSpan.FromMinutes(2), "correlation-a", CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(3));

        WorkflowClaimResult recovered = await repository.TryClaimAsync(record.Id, "actor-b", TimeSpan.FromMinutes(2), "correlation-b", CancellationToken.None);

        recovered.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        recovered.Record!.ClaimedBy.Should().Be("actor-b");
    }

    [Fact]
    public async Task ExpiredCloseClaim_CanResumeWithoutReacquiringJiraCreate()
    {
        ManualTimeProvider time = new();
        InMemoryOperationalRecordRepository repository = new(time);
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        string transferKey = OperationalRecordIdempotency.Create(record.SourceRecordId, "mapping-v1");
        _ = await repository.MarkPreviewedAsync(record.Id, "mapping-v1", transferKey, "actor-a", "correlation-a", CancellationToken.None);
        _ = await repository.TryClaimAsync(record.Id, "actor-a", TimeSpan.FromMinutes(2), "correlation-a", CancellationToken.None);
        _ = await repository.TryAcquireCreateAsync(record.Id, "mapping-v1", transferKey, "actor-a", "correlation-a", CancellationToken.None);
        _ = await repository.RecordJiraCreatedAsync(record.Id, "TEST-100", "actor-a", "correlation-a", CancellationToken.None);
        _ = await repository.TryAcquireCloseAsync(record.Id, "actor-a", "correlation-a", CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(3));

        WorkflowClaimResult claim = await repository.TryClaimAsync(record.Id, "actor-b", TimeSpan.FromMinutes(2), "correlation-b", CancellationToken.None);
        WorkflowAcquireResult close = await repository.TryAcquireCloseAsync(record.Id, "actor-b", "correlation-b", CancellationToken.None);

        claim.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        close.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        close.Record!.JiraIssueKey.Should().Be("TEST-100");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
