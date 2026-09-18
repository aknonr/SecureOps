using System.Text.Json;
using Dapper;
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
    [LocalResourceSqlFact]
    public async Task InUse_OverviewAndIntent_PersistWithoutInventingSourceDates()
    {
        await using SqlConnection connection = new(Configuration().GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        var repository = new SqlInUseRepository(Configuration());
        var audit = new AuditEvent { Actor = actor.UserId.ToString("D"), Action = "InUseLocalProgress", CorrelationId = "synthetic-progress" };
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0] with { Id = Guid.NewGuid().ToString(), Code = "OR-PROGRESS-" + Guid.NewGuid().ToString("N") };
        await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([source], false), null, audit, _token);
        InUseRecord r = (await repository.QueryAsync(new(Search: source.Code), actor.UserId, _token)).Items.Single();
        var intent = new InUseCompletion(Guid.NewGuid(), actor.UserId, r.Version, r.SourceVersion, new string('A', 64), DateTimeOffset.UtcNow);
        (await repository.SaveAsync(r with { Version = r.Version + 1, Completion = intent }, r.Version, audit, _token)).Should().BeTrue();
        InUseRecord stored = (await new SqlInUseRepository(Configuration()).GetAsync(r.Id, _token))!;
        stored.FirstSeenAt.Should().Be(r.FirstSeenAt);
        stored.Source.Creation.Should().BeNull();
        stored.Completion.Should().BeEquivalentTo(intent);
        (await repository.SaveAsync(stored with { Version = stored.Version + 1 }, r.Version, audit, _token)).Should().BeFalse();
        (await repository.OverviewAsync(audit, _token)).Unknown.Should().BeGreaterThan(0);
    }
    [LocalResourceSqlFact]
    public async Task InUse_RefreshLock_SerializesIndependentRepositories_WithoutBlockingStoredReads()
    {
        IConfiguration configuration = Configuration();
        var first = new SqlInUseRepository(configuration);
        var second = new SqlInUseRepository(configuration);
        await using (IAsyncDisposable? lease = await first.TryAcquireRefreshAsync(_token))
        {
            lease.Should().NotBeNull();
            (await second.TryAcquireRefreshAsync(_token)).Should().BeNull();
            (await second.QueryAsync(new(), Guid.NewGuid(), _token)).Should().NotBeNull();
            // PUBLIC is sufficient for this fixed database application-lock resource; no runtime DDL grant.
            await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
            await connection.OpenAsync(_token);
            string user = "InUseLockTest_" + Guid.NewGuid().ToString("N");
            await connection.ExecuteAsync($"CREATE USER [{user}] WITHOUT LOGIN;");
            try
            {
                int result = await connection.QuerySingleAsync<int>($"""
                    EXECUTE AS USER = '{user}';
                    BEGIN TRANSACTION;
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource=N'SecureOps:InUseRefresh:4241:68',
                        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0;
                    ROLLBACK;
                    REVERT;
                    SELECT @result;
                    """);
                result.Should().Be(-1);
            }
            finally { await connection.ExecuteAsync($"REVERT; DROP USER [{user}];"); }
        }
        await using IAsyncDisposable? reacquired = await second.TryAcquireRefreshAsync(_token);
        reacquired.Should().NotBeNull();
    }

    [LocalResourceSqlFact]
    public async Task InUse_SemanticProjection_PersistsForPublishedReview()
    {
        IConfiguration configuration = Configuration();
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        SecureOps.Infrastructure.Resources.ResourceActor actor = await CreateActorAsync(connection);
        var repository = new SqlInUseRepository(configuration);
        string prefix = "(LCSIMS_ServiceInstance)m_rid.";
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                Items = Enumerable.Range(0, 4).Select(i => new[]
        {
            new { Key = "SET." + prefix + "id", Value = (5000 + i).ToString() },
            new { Key = "SET." + prefix + "p_name", Value = "mapped-synthetic-" + i },
            new { Key = "KEY." + prefix + "p_SI_def_environment", Value = i == 0 ? "TEST" : "PROD" },
            new { Key = "SET." + prefix + "p_SI_def_environment", Value = (10 + i).ToString() },
            new { Key = "KEY." + prefix + "c_new_SI_major_project", Value = "Synthetic service " + i },
            new { Key = "SET." + prefix + "c_new_SI_major_project", Value = (6000 + i).ToString() },
            new { Key = "SET." + prefix + "c_new_SI_major_project.id", Value = (6000 + i).ToString() },
            new { Key = "SET." + prefix + "p_SI_ip_SI_address_1", Value = "192.0.2." + (i + 1) }
        })
            }
        }));
        string code = "OR-MAPPED-" + Guid.NewGuid().ToString("N");
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0] with
        {
            Id = code,
            Code = code,
            Servers = InUseServiceItemParser.Parse(response.RootElement),
            ServiceItemsState = "Observed",
            RelationshipEvidence = "Synthetic semantic projection; completeness unverified",
            Creator = null,
            AffectedAssetsState = "NotQueried",
            AffectedAssetCount = null,
            ProvisioningTeam = new(null, "Not queried")
        };
        var audit = new AuditEvent { Actor = actor.UserId.ToString("D"), Action = "InUseRefresh", CorrelationId = "mapped-local", Details = new { Count = 4 } };
        (await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([source], false), null, audit, _token)).Should().BeTrue();
        InUseRecord record = (await new SqlInUseRepository(configuration).QueryAsync(new(Search: code), actor.UserId, _token)).Items.Single();
        record.Source.Should().BeEquivalentTo(source);
        InUseRecord draft = record with { Version = record.Version + 1, Draft = new(record.SourceVersion, [], "", actor.UserId, DateTimeOffset.UtcNow) };
        (await repository.SaveAsync(draft, record.Version, audit, _token)).Should().BeTrue();
        var changed = source.Servers[0].Fields.ToDictionary(p => p.Key, p => p.Value);
        changed["SI_ENVIRONMENT"] = new("STAGING", "Synthetic changed source evidence");
        InUseSource next = source with { Servers = source.Servers.Select((s, i) => i == 0 ? s with { Fields = changed } : s).ToArray() };
        await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([next], false), null, audit, _token);
        InUseRecord stale = (await repository.GetAsync(record.Id, _token))!;
        stale.Status.Should().Be("Stale");
        stale.Draft.Should().BeEquivalentTo(draft.Draft);
        stale.Source.Servers[0].Fields["SI_ENVIRONMENT"].Value.Should().Be("STAGING");
    }

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
