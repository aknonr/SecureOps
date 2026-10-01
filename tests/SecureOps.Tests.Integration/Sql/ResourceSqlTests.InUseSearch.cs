using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [Fact]
    public Task InUse_MemorySearch_FindsPersistedReportersAcrossPages() =>
        VerifyReporterSearchAsync(new InMemoryInUseRepository(new InMemoryAuditWriter()), Guid.NewGuid());

    [LocalResourceSqlFact]
    public async Task InUse_SqlSearch_FindsPersistedReportersAcrossPages()
    {
        await using SqlConnection connection = new(Configuration().GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        await VerifyReporterSearchAsync(new SqlInUseRepository(Configuration()), actor.UserId);
    }

    private static async Task VerifyReporterSearchAsync(IInUseRepository repository, Guid actor)
    {
        string prefix = "SEARCH-" + Guid.NewGuid().ToString("N");
        InUseSource template = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0];
        InUseSource[] sources = Enumerable.Range(0, 30).Select(i => template with
        {
            Id = prefix + i,
            Code = prefix + i.ToString("D2"),
            Title = "Synthetic search",
            Servers = [new("100", new Dictionary<string, InUseEvidence>
            { ["HOSTNAME"] = new(prefix + "-HOSTI-" + i, "Synthetic source") })
            {
                RelatedRequestReporter = i < 25 ? null : new(prefix + i, "100", "123", "SourceId", "123", "OR-123",
                    prefix + " I\u015e\u0131k \u0130pek", prefix + "_LOGIN", "ExactMatch", "Returned", "Returned", DateTimeOffset.UtcNow)
            }]
        }).ToArray();
        // A second reporter on the same record must not duplicate SQL rows or inflate totals.
        sources[29] = sources[29] with
        {
            Servers = [sources[29].Servers[0], sources[29].Servers[0] with
        { Id = "101", RelatedRequestReporter = sources[29].Servers[0].RelatedRequestReporter! with { ServiceItemId = "101", Display = prefix + " \u00d6zge" } }]
        };
        var audit = new AuditEvent { Actor = actor.ToString("D"), Action = "InUseSearchTest", CorrelationId = prefix };
        (await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new(sources, false), null, audit, _token)).Should().BeTrue();
        InUsePage first = await repository.QueryAsync(new(Search: prefix), actor, _token);
        first.Items.Should().HaveCount(25);
        first.Total.Should().Be(30);
        (await repository.QueryAsync(new(Search: prefix + " \u0131\u015f\u0131k ipek", PageSize: 2, Page: 2), actor, _token)).Items.Should().HaveCount(2);
        InUsePage matches = await repository.QueryAsync(new(Search: prefix + "_login"), actor, _token);
        matches.Total.Should().Be(5);
        (await repository.QueryAsync(new(Search: prefix + " \u00f6zge"), actor, _token)).Total.Should().Be(1);
        (await repository.QueryAsync(new(Search: prefix + "-hosti-29"), actor, _token)).Total.Should().Be(1);
        (await repository.QueryAsync(new(Search: prefix + "_login", View: "mine"), actor, _token)).Total.Should().Be(0);
        InUseRecord assigned = matches.Items[0];
        (await repository.SaveAsync(assigned with { Version = assigned.Version + 1, AssigneeId = actor }, assigned.Version, audit, _token)).Should().BeTrue();
        (await repository.QueryAsync(new(Search: prefix + "_login", View: "mine"), actor, _token)).Total.Should().Be(1);
        (await repository.QueryAsync(new(Search: prefix + "_login", View: "unassigned"), actor, _token)).Total.Should().Be(4);
        await repository.RefreshAsync((await repository.StateAsync(_token)).Version, null, "SyntheticFailure", audit, _token);
        InUsePage stale = await repository.QueryAsync(new(Search: prefix + "_login"), actor, _token);
        stale.Total.Should().Be(5);
        stale.Refresh.Stale.Should().BeTrue();
        stale.Items.SelectMany(r => r.Source.Servers).Select(s => s.RelatedRequestReporter!.State).Should().OnlyContain(s => s == "Stale");
    }
}
