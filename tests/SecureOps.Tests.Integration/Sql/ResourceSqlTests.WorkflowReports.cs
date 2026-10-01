using System.IO.Compression;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.InUse.Execution;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task WorkflowReport_DiscardedDraftIsNotActiveWork_ArchiveRemainsHistorical()
    {
        ExecutionFixture f = await ExecutionAsync();
        var store = new SqlWorkflowReportStore(Configuration());
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        long version = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = f.Intent.InitiatorId });
        var actor = new ApplicationUser(f.Intent.InitiatorId, "synthetic-report", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, version, [], []);
        await store.ArchiveAsync(InUseWorkbook.Create(f.Record, actor.Id, DateTimeOffset.UtcNow) with { Archived = true }, true, _token);
        var repository = new SqlInUseRepository(Configuration());
        (await repository.SaveAsync(f.Record with { Version = f.Record.Version + 1, Draft = null, Discarded = true }, f.Record.Version, LifecycleAudit(f), _token)).Should().BeTrue();
        Guid cut = await store.CaptureAsync(actor, "discard", new(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, IncludeSynthetic: true), true, false, false, "synthetic-discard", _token);
        WorkflowReport all = (await store.ReadAsync(cut, actor, "discard", new(), "synthetic-discard", true, _token))!;
        WorkflowFact[] facts = all.Items.Where(i => i.RecordId == f.Record.Id).ToArray();
        facts.Select(i => i.Metric).Should().BeEquivalentTo(new[] { "InUse.Discarded", "InUse.Archived" });
        facts.Should().OnlyContain(i => i.Status == "Discarded");
        WorkflowReport filtered = (await store.ReadAsync(cut, actor, "discard", new(Status: "Discarded"), "synthetic-discard", true, _token))!;
        filtered.Total.Should().Be(filtered.Items.Count);
        WorkflowReportWorkbook.Create(filtered).Should().NotBeEmpty();
    }

    [LocalResourceSqlFact]
    public async Task WorkflowReport_OcoOwnerBoundariesHalfOpenDatesAndLogicalSends()
    {
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        Infrastructure.Resources.ResourceActor saved = await CreateActorAsync(sql);
        Infrastructure.Resources.ResourceActor other = await CreateActorAsync(sql);
        long version = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = saved.UserId });
        var actor = new ApplicationUser(saved.UserId, "synthetic-report", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, version, [], []);
        DateTimeOffset from = DateTimeOffset.UtcNow.Date.AddDays(-2).AddHours(-3);
        DateTimeOffset to = from.AddDays(1);
        var drafts = new SqlAnnouncementStore(Configuration());
        async Task<PreparedAnnouncement> Prepare(Guid owner, DateTimeOffset at)
        {
            var content = new AnnouncementContent("OCO-000123", "Synthetic", "Synthetic report fixture", "2026-09-15",
                "2026-09-15T10:00:00Z", "2026-09-15T11:00:00Z", "Synthetic", "Synthetic", "Synthetic", "", [], [], "synthetic");
            var draft = new AnnouncementDraft(Guid.NewGuid(), owner, 1, at, content, "operator@example.invalid", "synthetic") { Synthetic = true };
            (await drafts.SaveAsync(draft, "synthetic-report", _token)).Should().BeNull();
            var preparation = new PreparedAnnouncement(Guid.NewGuid(), draft, at, "Synthetic preparer", "synthetic", "<p>Synthetic</p>", [1], new Dictionary<string, string>());
            (await drafts.PrepareAsync(preparation, "synthetic-report", _token)).Error.Should().BeNull();
            return preparation;
        }
        PreparedAnnouncement included = await Prepare(actor.Id, from);
        PreparedAnnouncement excludedEnd = await Prepare(actor.Id, to);
        PreparedAnnouncement excludedOther = await Prepare(other.UserId, from);
        foreach (string kind in new[] { "SelfTest", "Send" })
        {
            await sql.ExecuteAsync("""
                INSERT announcements.MailCommands(CommandId,PreparationId,DraftId,OwnerId,Kind,Version,State,CreatedAt,UpdatedAt,IntentJson,MessageBytes)
                VALUES(NEWID(),@preparation,@draft,@owner,@kind,3,@state,@from,@updated,'{}',0x01);
                """, new { preparation = included.Id, draft = included.Draft.Id, owner = actor.Id, kind, state = kind == "SelfTest" ? "Accepted" : "Unknown", from, updated = to.AddMinutes(1) });
        }
        var store = new SqlWorkflowReportStore(Configuration());
        Guid id = await store.CaptureAsync(actor, "scope", new(from, to, IncludeSynthetic: true), false, false, true, "synthetic-report", _token);
        WorkflowReport report = (await store.ReadAsync(id, actor, "scope", new(), "synthetic-report", true, _token))!;
        report.Items.Should().HaveCount(3);
        report.Metrics.Single(x => x.Key == "Oco.Prepared").Count.Should().Be(1);
        report.Metrics.Single(x => x.Key == "Oco.SelfTest.Accepted").Count.Should().Be(1);
        report.Metrics.Single(x => x.Key == "Oco.Send.Unknown").Count.Should().Be(1);
        report.Items.Should().NotContain(x => x.RecordId == excludedEnd.Draft.Id || x.RecordId == excludedOther.Draft.Id);
        report.Items.Should().OnlyContain(x => x.OccurredAt == from, "logical request time, not retry/update time defines the cohort");
        report.Readiness.Single(x => x.Module == "Oco").State.Should().Be("Unknown");
        Guid noScope = await store.CaptureAsync(actor, "none", new(from, to, IncludeSynthetic: true), false, false, false, "synthetic-report", _token);
        (await store.ReadAsync(noScope, actor, "none", new(), "synthetic-report", true, _token))!.Total.Should().Be(0);
    }

    [LocalResourceSqlFact]
    public async Task WorkflowReport_ThousandRecordSqlAggregationAndPaging()
    {
        ExecutionFixture f = await ExecutionAsync();
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        var records = Enumerable.Range(0, 1000).Select(i =>
        {
            var id = Guid.NewGuid();
            InUseRecord r = f.Record with { Id = id, Source = f.Record.Source with { Id = id.ToString("N"), Code = "SYN-REPORT-" + i } };
            return new { r.Id, SourceId = r.Source.Id, r.Source.Code, r.Source.Title, r.Version, RecordJson = JsonSerializer.Serialize(r) };
        }).ToArray();
        await sql.ExecuteAsync("""
            INSERT ops.InUseRecords(Id,SourceId,Code,Title,ReviewStatus,Version,RecordJson)
            SELECT Id,SourceId,Code,Title,'Draft',Version,RecordJson FROM OPENJSON(@json)
            WITH(Id uniqueidentifier,SourceId nvarchar(100),Code nvarchar(100),Title nvarchar(1000),Version bigint,RecordJson nvarchar(max));
            """, new { json = JsonSerializer.Serialize(records) });
        long version = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = f.Intent.InitiatorId });
        var actor = new ApplicationUser(f.Intent.InitiatorId, "synthetic-report", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, version, [], []);
        var store = new SqlWorkflowReportStore(Configuration());
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Guid cut = await store.CaptureAsync(actor, "volume", new(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, IncludeSynthetic: true), true, false, false, "synthetic-volume", _token);
        long captureMs = timer.ElapsedMilliseconds;
        WorkflowReport page = (await store.ReadAsync(cut, actor, "volume", new(Metric: "InUse.Unassigned", Page: 10, PageSize: 25), "synthetic-volume", false, _token))!;
        page.Items.Should().HaveCount(25);
        page.Total.Should().BeGreaterThanOrEqualTo(1000);
        WorkflowReport exported = (await store.ReadAsync(cut, actor, "volume", page.Filter, "synthetic-volume", true, _token))!;
        exported.Items.Count.Should().Be((int)page.Total);
        exported.Metrics.Single().Count.Should().Be(page.Total);
        WorkflowReportWorkbook.Create(exported).Should().NotBeEmpty();
        Console.WriteLine($"Synthetic report: 1000 new ORs; capture={captureMs}ms; capture+page+export={timer.ElapsedMilliseconds}ms; facts={exported.Total}. Not an SLA.");
    }

    [LocalResourceSqlFact]
    public async Task WorkflowReport_FrozenStagesReceiptDedupPagingExportAndRevocation()
    {
        ExecutionFixture f = await ExecutionAsync();
        var store = new SqlWorkflowReportStore(Configuration());
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        long version = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = f.Intent.InitiatorId });
        var actor = new ApplicationUser(f.Intent.InitiatorId, "synthetic-report", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, version, [], [Capabilities.ManagementReportingView, Capabilities.InUseView]);
        InUseReport report = InUseWorkbook.Create(f.Record, actor.Id, DateTimeOffset.UtcNow) with { Archived = true };
        await store.ArchiveAsync(report, true, _token);
        await store.ArchiveAsync(report, true, _token);
        await f.Store.CreateAsync(f.Intent, f.Bytes, _token);
        await new InUseExecutionWorker(f.Store, new FaultTransport(new FixtureInUseCompletionTransport(f.Options, f.Policy), "Bpm"), f.Policy, f.Options).RunAsync(f.Intent.OperationId, _token);
        var request = new WorkflowReportRequest(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, IncludeSynthetic: true);
        Guid id = await store.CaptureAsync(actor, "scope", request, true, false, false, "synthetic-report", _token);
        WorkflowReport cut = (await store.ReadAsync(id, actor, "scope", new(Metric: "InUse.Attachment"), "synthetic-report", false, _token))!;
        cut.Items.Should().ContainSingle(x => x.RecordId == f.Record.Id);
        cut.Items.Should().OnlyContain(x => x.Metric == "InUse.Attachment");
        WorkflowReport closed = (await store.ReadAsync(id, actor, "scope", new(Metric: "InUse.Closed"), "synthetic-report", false, _token))!;
        closed.Items.Should().NotContain(x => x.RecordId == f.Record.Id);
        WorkflowReport partial = (await store.ReadAsync(id, actor, "scope", new(Metric: "InUse.Partial"), "synthetic-report", false, _token))!;
        partial.Items.Should().ContainSingle(x => x.RecordId == f.Record.Id);
        WorkflowReport archived = (await store.ReadAsync(id, actor, "scope", new(Metric: "InUse.Archived"), "synthetic-report", true, _token))!;
        archived.Items.Should().ContainSingle(x => x.RecordId == f.Record.Id);
        archived.Metrics.Single().Count.Should().Be(archived.Items.Count);
        byte[] bytes = WorkflowReportWorkbook.Create(archived);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet3.xml")!.Open());
        string xml = await reader.ReadToEndAsync();
        xml.Should().Contain(f.Record.Id.ToString("D")).And.NotContain("<f>");
        await sql.ExecuteAsync("UPDATE ops.InUseRecords SET ReviewStatus='Stale' WHERE Id=@id", new { id = f.Record.Id });
        (await store.ReadAsync(id, actor, "scope", new(Metric: "InUse.Attachment"), "synthetic-report", false, _token))!.Should().BeEquivalentTo(cut);
        (await store.ReadAsync(id, actor with { Id = Guid.NewGuid() }, "scope", new(), "synthetic-report", false, _token)).Should().BeNull();
        (await store.ReadAsync(id, actor, "changed-scope", new(), "synthetic-report", false, _token)).Should().BeNull();
        Guid excluded = await store.CaptureAsync(actor, "scope", request with { IncludeSynthetic = false }, true, false, false, "synthetic-report", _token);
        (await store.ReadAsync(excluded, actor, "scope", new(), "synthetic-report", true, _token))!.Items.Should().NotContain(x => x.RecordId == f.Record.Id);
        await sql.ExecuteAsync("UPDATE security.Users SET AccessVersion=AccessVersion+1 WHERE UserId=@id", new { id = actor.Id });
        (await store.ReadAsync(id, actor, "scope", new(), "synthetic-report", false, _token)).Should().BeNull();
    }

    [LocalResourceSqlFact]
    public async Task WorkflowReport_AllModuleSqlProjectionsAndOwnerBoundary()
    {
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        Infrastructure.Resources.ResourceActor saved = await CreateActorAsync(sql);
        long version = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = saved.UserId });
        var actor = new ApplicationUser(saved.UserId, "synthetic-report", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, version, [], []);
        var store = new SqlWorkflowReportStore(Configuration());
        var request = new WorkflowReportRequest(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, IncludeSynthetic: true);
        Guid id = await store.CaptureAsync(actor, "scope", request, true, true, true, "synthetic-report-all", _token);
        WorkflowReport all = (await store.ReadAsync(id, actor, "scope", new(), "synthetic-report-all", true, _token))!;
        all.Items.Where(x => x.Module == "Oco").Should().BeEmpty("this actor owns no preparations/source jobs/mail");
        all.Metrics.Sum(x => x.Count).Should().Be(all.Total, "fact rows reconcile, not a business-completion total");
        WorkflowReport first = (await store.ReadAsync(id, actor, "scope", new(PageSize: 1), "synthetic-report-all", false, _token))!;
        first.Total.Should().Be(all.Total);
        first.Items.Should().HaveCount((int)Math.Min(1, all.Total));
        Func<Task> change = () => sql.ExecuteAsync("UPDATE reporting.WorkflowSnapshots SET ScopeHash='changed' WHERE Id=@id", new { id });
        await change.Should().ThrowAsync<SqlException>();
    }
}
