using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task InUseCatalogue_OriginalMetadataPagingBoundariesRestartAndRevocation()
    {
        ExecutionFixture f = await ExecutionAsync();
        var store = new SqlWorkflowReportStore(Configuration());
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        long accessVersion = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = f.Intent.InitiatorId });
        var actor = new ApplicationUser(f.Intent.InitiatorId, "synthetic-catalogue", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, accessVersion, [], []);
        DateTimeOffset at = DateTimeOffset.UtcNow.Date.AddDays(-1);
        InUseReport report = InUseWorkbook.Create(f.Record, actor.Id, at) with
        { Archived = true, PreparedByLabel = "Original \u0130nceleyen", PreparedByAccount = "original-" + actor.Id.ToString("N") };
        await store.ArchiveAsync(report, true, _token);
        await new SqlWorkflowReportStore(Configuration()).ArchiveAsync(report, true, _token);
        await sql.ExecuteAsync("UPDATE security.Users SET DisplayName='Renamed downloader' WHERE UserId=@id", new { id = actor.Id });
        var query = new InUseReportQuery(Search: report.SourceCode, From: at, To: at.AddDays(1), PageSize: 1);
        InUseReportPage page = (await store.CatalogueAsync(actor, query, "synthetic-catalogue", _token))!;
        page.Total.Should().Be(1);
        InUseReportEntry entry = page.Items.Single();
        entry.PreparedByLabel.Should().Be(report.PreparedByLabel);
        entry.PreparedByAccount.Should().Be(report.PreparedByAccount);
        entry.FileName.Should().Be(InUseReportNames.Download(report));
        entry.AttachmentStatus.Should().Be("NotVerified");
        entry.Sha256.Should().Be(report.Sha256);
        (await store.CatalogueAsync(actor, query with { To = at }, "boundary", _token))!.Total.Should().Be(0);
        (await store.CatalogueAsync(actor, query with { Page = 2 }, "page", _token))!.Items.Should().BeEmpty();
        (await store.CatalogueAsync(actor, query with { Search = report.PreparedByAccount }, "account", _token))!.Items.Should().Contain(x => x.RecordId == f.Record.Id);
        foreach (string host in entry.Hostnames)
        { (await store.CatalogueAsync(actor, query with { Search = host }, "host", _token))!.Total.Should().BeGreaterThan(0); }
        Func<Task> conflict = () => store.ArchiveAsync(report with { SourceCode = "OTHER-OR" }, true, _token);
        await conflict.Should().ThrowAsync<SqlException>();
        Func<Task> actorConflict = () => store.ArchiveAsync(report with { PreparedBy = Guid.NewGuid() }, true, _token);
        await actorConflict.Should().ThrowAsync<SqlException>();
        Func<Task> rewrite = () => sql.ExecuteAsync("UPDATE reporting.InUseReportCatalogue SET SourceCode='OTHER' WHERE RecordId=@id", new { id = f.Record.Id });
        await rewrite.Should().ThrowAsync<SqlException>();
        await sql.ExecuteAsync("UPDATE ops.InUseRecords SET RecordJson=JSON_MODIFY(RecordJson,'$.Discarded',CAST(1 AS bit)) WHERE Id=@id", new { id = f.Record.Id });
        (await store.CatalogueAsync(actor, query with { Status = "Discarded" }, "discard", _token))!.Items.Single().Status.Should().Be("Discarded");
        (await store.CatalogueAsync(actor, query with { Status = "Current" }, "active", _token))!.Total.Should().Be(0);
        await sql.ExecuteAsync("UPDATE security.RoleAssignments SET RevokedAt=SYSDATETIMEOFFSET() WHERE UserId=@id", new { id = actor.Id });
        (await store.CatalogueAsync(actor, query, "revoked", _token)).Should().BeNull();
    }

    [LocalResourceSqlFact]
    public async Task InUseCatalogue_MissingHistoricMetadataIsNotFilledFromCurrentRecord()
    {
        ExecutionFixture f = await ExecutionAsync();
        var store = new SqlWorkflowReportStore(Configuration());
        InUseReport report = InUseWorkbook.Create(f.Record, f.Intent.InitiatorId, DateTimeOffset.UtcNow) with
        { Archived = true, SourceCode = null, EvidenceSheets = [], Sheets = [], PreparedByLabel = null };
        await store.ArchiveAsync(report, true, _token);
        await using var sql = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        string? code = await sql.QuerySingleAsync<string?>("SELECT SourceCode FROM reporting.InUseReportCatalogue WHERE RecordId=@id", new { id = f.Record.Id });
        code.Should().BeNull();
        string hosts = await sql.QuerySingleAsync<string>("SELECT HostsJson FROM reporting.InUseReportCatalogue WHERE RecordId=@id", new { id = f.Record.Id });
        JsonSerializer.Deserialize<string[]>(hosts).Should().BeEmpty();
    }
}
