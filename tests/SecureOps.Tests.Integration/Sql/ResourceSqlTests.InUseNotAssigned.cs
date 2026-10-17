using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [Fact]
    public Task InUse_MemoryNotAssigned_DropsFromReviewOnlyWithProvenance() =>
        VerifyNotAssignedAsync(new InMemoryInUseRepository(new InMemoryAuditWriter()), Guid.NewGuid());

    [LocalResourceSqlFact]
    public async Task InUse_SqlNotAssigned_DropsFromReviewOnlyWithProvenance()
    {
        await using SqlConnection connection = new(Configuration().GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        await VerifyNotAssignedAsync(new SqlInUseRepository(Configuration()), actor.UserId);
    }

    // TH-11: a source group that is not WASAS leaves the main list (tracking only) while an unknown or unproven
    // assignment keeps the record in review, identically in memory and SQL.
    private static async Task VerifyNotAssignedAsync(IInUseRepository repository, Guid actor)
    {
        string prefix = "ASSIGN-" + Guid.NewGuid().ToString("N");
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0] with { Id = prefix, Code = prefix, WasasActivity = null, Lifecycle = null };
        var audit = new AuditEvent { Actor = actor.ToString("D"), Action = "SyntheticInUseAssignment", CorrelationId = prefix };
        async Task Refresh(InUseSource row) => (await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([row], true), null, audit, _token)).Should().BeTrue();
        async Task<InUsePage> View(string view) => await repository.QueryAsync(new(Search: prefix, View: view), actor, _token);

        foreach (InUseEvidence? stage in new InUseEvidence?[]
        { null, new("AssignedToWasas", "Synthetic rule"), new("NotAssignedToWasas", ""), new("NotAssignedToWasas", " \t"), new("notassignedtowasas", "Synthetic rule"), new("NotAssignedToWasas ", "Synthetic rule") })
        {
            await Refresh(source with { CurrentStage = stage });
            (await View("review")).Total.Should().Be(1, "an unknown or unproven assignment never drops a record");
            (await View("tracking")).Total.Should().Be(0);
        }

        await Refresh(source with { CurrentStage = new("NotAssignedToWasas", "Synthetic rule") });
        (await View("review")).Total.Should().Be(0);
        InUseRecord left = (await View("tracking")).Items.Single();
        left.ActivityStatus.Should().Be("NotAssigned");
        left.TrackingOnly.Should().BeTrue();

        await Refresh(source with { CurrentStage = new("NotAssignedToWasas", "Synthetic rule"), Lifecycle = new("Closed", "Synthetic rule") });
        (await View("tracking")).Items.Single().ActivityStatus.Should().Be("OrClosed");
        await Refresh(source with { CurrentStage = new("NotAssignedToWasas", "Synthetic rule"), WasasActivity = new("Completed", "Synthetic rule") });
        (await View("tracking")).Items.Single().ActivityStatus.Should().Be("Completed");
        // Back to unknown: the record returns to review (fail closed).
        await Refresh(source);
        (await View("review")).Total.Should().Be(1);
    }
}
