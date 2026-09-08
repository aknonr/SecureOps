using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task InUse_PersistedSearchAndPagination_HaveStableNonoverlappingPages()
    {
        IConfiguration configuration = Configuration();
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        SecureOps.Infrastructure.Resources.ResourceActor actor = await CreateActorAsync(connection);
        var repository = new SqlInUseRepository(configuration);
        string prefix = "INUSE-PAGE-" + Guid.NewGuid().ToString("N");
        InUseSource seed = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0];
        InUseSource[] sources = Enumerable.Range(1, 12).Select(i => seed with
        {
            Id = prefix + i,
            Code = prefix + "-" + i.ToString("D2"),
            Title = "Synthetic persisted pagination fixture",
            Servers = []
        }).ToArray();
        var audit = new AuditEvent { Actor = actor.UserId.ToString("D"), Action = "InUseRefresh", CorrelationId = "inuse-pages", Details = new { Count = sources.Length } };
        InUseRefreshState state = await repository.StateAsync(_token);
        (await repository.RefreshAsync(state.Version, new(sources, true), null, audit, _token)).Should().BeTrue();
        InUsePage first = await repository.QueryAsync(new(Search: prefix, PageSize: 10), actor.UserId, _token);
        InUsePage second = await repository.QueryAsync(new(Search: prefix, Page: 2, PageSize: 10), actor.UserId, _token);
        first.Total.Should().Be(12);
        second.Total.Should().Be(12);
        first.Items.Should().HaveCount(10);
        second.Items.Should().HaveCount(2);
        first.Items.Select(r => r.Id).Intersect(second.Items.Select(r => r.Id)).Should().BeEmpty();
        (await repository.QueryAsync(new(Search: prefix, View: "unassigned", Status: "Unreviewed"), actor.UserId, _token)).Total.Should().Be(12);
    }

    [LocalResourceSqlFact]
    public async Task InUse_RoundTripConcurrencyStaleDraftAndAuditRollback()
    {
        IConfiguration configuration = Configuration();
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        SecureOps.Infrastructure.Resources.ResourceActor actor = await CreateActorAsync(connection);
        var repository = new SqlInUseRepository(configuration);
        string sourceId = Guid.NewGuid().ToString("N");
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0] with { Id = sourceId, Code = sourceId };
        AuditEvent Audit(string action) => new() { Actor = actor.UserId.ToString("D"), Action = action, CorrelationId = "inuse-sql", Details = new { SourceId = sourceId } };
        InUseRefreshState state = await repository.StateAsync(_token);
        (await repository.RefreshAsync(state.Version, new([source], true), null, Audit("InUseRefresh"), _token)).Should().BeTrue();
        InUseRecord record = (await repository.QueryAsync(new(Search: sourceId), actor.UserId, _token)).Items.Single();
        record.Source.Servers.Should().HaveCount(2);
        InUseRecord next = record with { Version = record.Version + 1, AssigneeId = actor.UserId, AssigneeLabel = "synthetic:reviewer" };
        bool[] assignments = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.SaveAsync(next, record.Version, Audit("InUseAssigned"), _token)));
        assignments.Count(v => v).Should().Be(1);
        record = (await repository.GetAsync(record.Id, _token))!;
        next = record with { Version = record.Version + 1, Draft = new(record.SourceVersion, [], "Unknown checks", actor.UserId, DateTimeOffset.UtcNow) };
        bool[] saves = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.SaveAsync(next, record.Version, Audit("InUseDraftSaved"), _token)));
        saves.Count(v => v).Should().Be(1);
        record = (await repository.GetAsync(record.Id, _token))!;
        (await repository.ExportAsync(record.Id, record.Version - 1, Audit("InUseReportPrepared"), _token)).Should().BeFalse();
        (await repository.ExportAsync(record.Id, record.Version, Audit("InUseReportPrepared"), _token)).Should().BeTrue();
        string trigger = "TR_InUseTest_" + Guid.NewGuid().ToString("N");
        await connection.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Actor = '{actor.UserId:D}') THROW 51090, 'Synthetic audit failure.', 1; END;");
        state = await repository.StateAsync(_token);
        try
        {
            Func<Task> save = () => repository.SaveAsync(record with { Version = record.Version + 1, AssigneeId = null }, record.Version, Audit("InUseAssigned"), _token);
            await save.Should().ThrowAsync<SqlException>();
            Func<Task> draft = () => repository.SaveAsync(record with { Version = record.Version + 1, Draft = null }, record.Version, Audit("InUseDraftSaved"), _token);
            await draft.Should().ThrowAsync<SqlException>();
            Func<Task> export = () => repository.ExportAsync(record.Id, record.Version, Audit("InUseReportPrepared"), _token);
            await export.Should().ThrowAsync<SqlException>();
            Func<Task> refresh = () => repository.RefreshAsync(state.Version, new([source with { Title = "Must roll back" }], true), null, Audit("InUseRefresh"), _token);
            await refresh.Should().ThrowAsync<SqlException>();
            (await repository.GetAsync(record.Id, _token)).Should().BeEquivalentTo(record);
            (await repository.StateAsync(_token)).Should().BeEquivalentTo(state);
        }
        finally { await connection.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];"); }
        await repository.RefreshAsync(state.Version, new([source with { Title = "Changed source" }], false, "Partial"), null, Audit("InUseRefresh"), _token);
        InUseRecord stale = (await new SqlInUseRepository(configuration).GetAsync(record.Id, _token))!;
        stale.Status.Should().Be("Stale");
        stale.SourceVersion.Should().Be(record.SourceVersion + 1);
        stale.Draft.Should().BeEquivalentTo(record.Draft);
        InUseRefreshState partial = await repository.StateAsync(_token);
        await repository.RefreshAsync(partial.Version, null, "SourceUnavailableOrMalformed", Audit("InUseRefresh"), _token);
        (await repository.GetAsync(record.Id, _token)).Should().BeEquivalentTo(stale);
        (await repository.StateAsync(_token)).LastSuccessfulAt.Should().Be(partial.LastSuccessfulAt);
        await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([], true), null, Audit("InUseRefresh"), _token);
        (await repository.QueryAsync(new(Search: sourceId, View: "mine", PageSize: 1), actor.UserId, _token)).Total.Should().Be(1);
    }
}
