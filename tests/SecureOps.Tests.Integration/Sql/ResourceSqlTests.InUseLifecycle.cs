using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.InUse.Execution;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task InUseLifecycle_RevokedActorCannotDiscard()
    {
        ExecutionFixture f = await ExecutionAsync();
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        await sql.ExecuteAsync("UPDATE security.RoleAssignments SET RevokedAt=SYSDATETIMEOFFSET(),RevokedByCorporateIdentity='synthetic-revocation' WHERE UserId=@id AND RevokedAt IS NULL;", new { id = f.Intent.InitiatorId });
        var repository = new SqlInUseRepository(Configuration());
        (await repository.SaveAsync(f.Record with { Version = f.Record.Version + 1, Discarded = true, Draft = null }, f.Record.Version, LifecycleAudit(f), _token)).Should().BeFalse();
        (await repository.GetAsync(f.Record.Id, _token))!.Version.Should().Be(f.Record.Version);
    }

    [LocalResourceSqlFact]
    public async Task InUseLifecycle_HistoryRemainsButCannotBeReused_AfterRepositoryRestart()
    {
        ExecutionFixture f = await ExecutionAsync();
        var repository = new SqlInUseRepository(Configuration());
        InUseRecord reviewed = f.Record with { Version = f.Record.Version + 1 };
        AuditEvent saved = LifecycleAudit(f, "InUseDraftSaved");
        (await repository.SaveAsync(reviewed, f.Record.Version, saved, _token)).Should().BeTrue();
        string key = InUseReviewHistory.Identity(reviewed.Source, reviewed.Source.Servers[0])!;
        InUseServerReview previous = (await repository.HistoryAsync(key, reviewed.Source.Code, 1, 50, _token)).Items.Single();
        previous.Invalidated.Should().BeFalse();
        InUseRecord discarded = reviewed with { Version = reviewed.Version + 1, Draft = null, Discarded = true, InvalidatedReviewsThrough = reviewed.Version };
        (await repository.SaveAsync(discarded, reviewed.Version, LifecycleAudit(f), _token)).Should().BeTrue();
        var restarted = new SqlInUseRepository(Configuration());
        (await restarted.ReviewAsync(previous.Id, _token))!.Invalidated.Should().BeTrue();
        (await restarted.HistoryAsync(key, reviewed.Source.Code, 1, 50, _token)).Items.Single().Invalidated.Should().BeTrue();
        (await restarted.QueryAsync(new(Search: reviewed.Source.Code), f.Intent.InitiatorId, _token)).Total.Should().Be(0);
        (await restarted.QueryAsync(new(Search: reviewed.Source.Code, Status: "Discarded"), f.Intent.InitiatorId, _token)).Total.Should().Be(1);
        InUseRecord fresh = discarded with { Version = discarded.Version + 1, Discarded = false };
        (await restarted.SaveAsync(fresh, discarded.Version, LifecycleAudit(f), _token)).Should().BeTrue();
        (await restarted.ReviewAsync(previous.Id, _token))!.Invalidated.Should().BeTrue();
    }

    [LocalResourceSqlFact]
    public async Task InUseLifecycle_ConcurrentExecutionAndDiscard_OnlyOneCommits()
    {
        ExecutionFixture f = await ExecutionAsync();
        var repository = new SqlInUseRepository(Configuration());
        InUseRecord next = f.Record with { Version = f.Record.Version + 1, Discarded = true, Draft = null, InvalidatedReviewsThrough = f.Record.Version };
        Task<bool> discard = repository.SaveAsync(next, f.Record.Version, LifecycleAudit(f), _token);
        Task<InUseResult<InUseExecution>> create = f.Store.CreateAsync(f.Intent, f.Bytes, _token);
        await Task.WhenAll(discard, create);
        (discard.Result != (create.Result.Error is null)).Should().BeTrue();
        InUseRecord stored = (await new SqlInUseRepository(Configuration()).GetAsync(f.Record.Id, _token))!;
        stored.Discarded.Should().Be(discard.Result);
    }

    [LocalResourceSqlFact]
    public async Task InUseLifecycle_RunningAndUnknownCommands_BlockDiscardWithoutRemovingEvidence()
    {
        ExecutionFixture f = await ExecutionAsync();
        await f.Store.CreateAsync(f.Intent, f.Bytes, _token);
        InUseExecutionLease? lease = await f.Store.ClaimAsync(f.Intent.OperationId, f.Intent.ConfigurationFingerprint, "Synthetic.Worker", _token);
        lease.Should().NotBeNull();
        var repository = new SqlInUseRepository(Configuration());
        InUseRecord next = f.Record with { Version = f.Record.Version + 1, Discarded = true, Draft = null };
        (await repository.SaveAsync(next, f.Record.Version, LifecycleAudit(f), _token)).Should().BeFalse();
        await f.Store.CompleteAsync(lease!, new("Unknown", "SyntheticResponseLost"), "Synthetic.Worker", _token);
        (await repository.SaveAsync(next, f.Record.Version, LifecycleAudit(f), _token)).Should().BeFalse();
        (await f.Store.LatestAsync(f.Record.Id, _token))!.State.Should().Be("Unknown");
        (await repository.GetAsync(f.Record.Id, _token))!.Draft.Should().NotBeNull();
    }

    private static AuditEvent LifecycleAudit(ExecutionFixture f, string action = "InUseDraftLifecycle") => new()
    {
        Actor = f.Intent.InitiatorId.ToString("D"),
        Action = action,
        Operation = new(Guid.NewGuid(), Guid.NewGuid(), "InUse", f.Record.Id.ToString("D"), action, f.Record.Version,
            DateTimeOffset.UtcNow, "Saved", new(f.Intent.InitiatorId, "Human", "Synthetic reviewer", null, null),
            new("SecureOps.Api", "synthetic-test"), null, "synthetic-lifecycle")
    };
}
