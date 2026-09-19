using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Bounded immutable SQL report cuts; every access is owner/version/scope fenced.</summary>
public sealed partial class SqlWorkflowReportStore(IConfiguration configuration)
{
    private string Connection => configuration.GetConnectionString("SecureOpsDb") ?? "";
    /// <summary>Presence only; never claims connectivity or emits the connection string.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Connection);
    /// <summary>Maximum materialized facts, including separate stages. Never silently truncates.</summary>
    public const int MaximumFacts = 50000;

    /// <summary>Records only a previously integrity-verified committed archive; retries preserve identity.</summary>
    public async Task ArchiveAsync(InUseReport report, bool synthetic, CancellationToken token)
    {
        if (!report.Archived || report.PreparedBy == Guid.Empty || report.Sha256.Length != 64)
        { throw new InvalidDataException("A verified immutable archive is required."); }
        var metadata = InUseReportMetadata.From(report);
        await using var sql = new SqlConnection(Connection);
        await sql.ExecuteAsync(new CommandDefinition("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF EXISTS(SELECT 1 FROM reporting.InUseArchiveReceipts WITH(UPDLOCK,HOLDLOCK)
                WHERE RecordId=@RecordId AND ReportVersion=@Version AND (Sha256<>@Sha256 OR SourceVersion<>@SourceVersion
                    OR PreparedBy<>@PreparedBy OR PreparedAt<>@PreparedAt
                    OR ISNULL(PreparedByLabel,N'')<>ISNULL(@PreparedByLabel,N'')))
                THROW 51232,'Archive receipt identity conflict.',1;
            IF NOT EXISTS(SELECT 1 FROM reporting.InUseArchiveReceipts WITH(UPDLOCK,HOLDLOCK) WHERE RecordId=@RecordId AND ReportVersion=@Version)
                INSERT reporting.InUseArchiveReceipts VALUES(@RecordId,@Version,@SourceVersion,@Sha256,@PreparedBy,@PreparedByLabel,@PreparedAt,SYSDATETIMEOFFSET(),@synthetic);
            IF EXISTS(SELECT 1 FROM reporting.InUseReportCatalogue WITH(UPDLOCK,HOLDLOCK)
                WHERE RecordId=@RecordId AND ReportVersion=@Version AND MetadataHash<>@MetadataHash)
                THROW 51242,'Archive catalogue identity conflict.',1;
            IF NOT EXISTS(SELECT 1 FROM reporting.InUseReportCatalogue WITH(UPDLOCK,HOLDLOCK) WHERE RecordId=@RecordId AND ReportVersion=@Version)
                INSERT reporting.InUseReportCatalogue VALUES(@RecordId,@Version,@SourceCode,@HostsJson,@PreparedByAccount,@DownloadName,@MetadataHash,SYSDATETIMEOFFSET());
            COMMIT;
            """, new
        {
            report.RecordId,
            report.Version,
            report.SourceVersion,
            report.Sha256,
            report.PreparedBy,
            report.PreparedByLabel,
            report.PreparedAt,
            metadata.SourceCode,
            metadata.HostsJson,
            metadata.PreparedByAccount,
            metadata.DownloadName,
            metadata.MetadataHash,
            synthetic
        }, commandTimeout: 15, cancellationToken: token));
    }

    /// <summary>Captures permitted facts in one serializable cut before any browser paging.</summary>
    public async Task<Guid> CaptureAsync(ApplicationUser user, string scope, WorkflowReportRequest request,
        bool inUse, bool sdm, bool oco, string correlation, CancellationToken token, WorkflowReadiness? queue = null)
    {
        await using var sql = new SqlConnection(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var id = Guid.NewGuid();
        WorkflowReadiness[] readiness = (await sql.QueryAsync<WorkflowReadiness>(new CommandDefinition("""
            SELECT 'InUse' AS Module,'Observed' AS State,
                MAX(TRY_CONVERT(datetimeoffset,JSON_VALUE(RecordJson,'$.LastSeenAt'))) AS LastSuccess,
                N'Son izinli kaynak gözlemi; tamlık ve güncel dış sistem durumu garantisi değil' AS Reason
            FROM ops.InUseRecords WHERE @inUse=1 AND (@demo=1 OR JSON_VALUE(RecordJson,'$.Source.Synthetic')='false') HAVING @inUse=1
            UNION ALL
            SELECT 'Sdm','Observed',MAX(LastSourceValidationAt),N'Son kaynak gözlemi; tip eşlemesi ve kapanış ayrı değerlendirilir'
            FROM ops.OperationalRecords WHERE @sdm=1 AND (@demo=1 OR SourceSynthetic=0) HAVING @sdm=1
            UNION ALL
            SELECT 'Oco','Observed',MAX(j.UpdatedAt),N'Yalnız sahibin başarılı kaynak işi; gönderim veya teslim kanıtı değildir'
            FROM announcements.SourceJobs j CROSS APPLY(SELECT TOP(1) DocumentJson FROM announcements.DraftRevisions d
                WHERE d.Id=j.DraftId AND d.OwnerId=@owner ORDER BY d.Version) d
            WHERE @oco=1 AND j.OwnerId=@owner AND j.State='Succeeded' AND (@demo=1 OR JSON_VALUE(d.DocumentJson,'$.Synthetic')='false') HAVING @oco=1;
            """, new { inUse, sdm, oco, owner = user.Id, demo = request.IncludeSynthetic }, tx, 15, cancellationToken: token))).ToArray();
        readiness = readiness.Select(x => x.LastSuccess is null ? x with { State = "Unknown" } : x).ToArray();
        if (queue is not null)
        { readiness = [.. readiness, queue]; }
        string[] limitations = ["CurrentStateNotHistoricalBacklog", "ArchiveReceiptsMayNotCoverOldEnvelopes",
            "UnknownOriginExcludedUnlessDemo", "OcoOwnerScopeOnly", "NoInboxDeliveryEvidence", "NoHistoricalCompletenessClaim"];
        // Source range locks remain held until commit. Label the retained cut after reading its facts.
        await sql.ExecuteAsync(new CommandDefinition(_captureSql + """

            DECLARE @now datetimeoffset=SYSDATETIMEOFFSET();
            IF NOT EXISTS(SELECT 1 FROM security.Users WHERE UserId=@owner AND AccessStatus='Approved' AND AccessVersion=@version)
                THROW 51234,'Report access changed.',1;
            INSERT reporting.WorkflowSnapshots VALUES(@id,@owner,@version,@scope,@now,DATEADD(hour,1,@now),@request,@limitations,@readiness);
            INSERT reporting.WorkflowFacts SELECT @id,Metric,LogicalId,Module,RecordId,Reference,RecordType,Status,Actor,Assignee,OccurredAt,Detail FROM #facts;
            """, new
        {
            id,
            owner = user.Id,
            version = user.Version,
            scope,
            request.From,
            request.To,
            request.IncludeSynthetic,
            inUse,
            sdm,
            oco,
            maximum = MaximumFacts,
            request = JsonSerializer.Serialize(request),
            limitations = JsonSerializer.Serialize(limitations),
            readiness = JsonSerializer.Serialize(readiness)
        }, tx, 30, cancellationToken: token));
        await AuditAsync(sql, tx, user.Id, correlation, id, "Captured", token);
        await tx.CommitAsync(token);
        return id;
    }

    /// <summary>Counts and pages the same retained facts; filters never run over a browser page.</summary>
    public async Task<WorkflowReport?> ReadAsync(Guid id, ApplicationUser user, string scope, WorkflowReportFilter filter,
        string correlation, bool export, CancellationToken token)
    {
        await using var sql = new SqlConnection(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        Snapshot? cut = await sql.QuerySingleOrDefaultAsync<Snapshot>(new CommandDefinition("""
            SELECT Id,AsOf,ExpiresAt,RequestJson,LimitationsJson,ReadinessJson FROM reporting.WorkflowSnapshots
            WHERE Id=@id AND OwnerId=@owner AND AccessVersion=@version AND ScopeHash=@scope AND ExpiresAt>SYSDATETIMEOFFSET()
                AND EXISTS(SELECT 1 FROM security.Users WHERE UserId=@owner AND AccessStatus='Approved' AND AccessVersion=@version);
            """, new { id, owner = user.Id, version = user.Version, scope }, tx, 15, cancellationToken: token));
        if (cut is null)
        { return null; }
        const string where = "SnapshotId=@id AND (@Module IS NULL OR Module=@Module) AND (@Status IS NULL OR Status=@Status) AND (@RecordType IS NULL OR RecordType=@RecordType) AND (@Metric IS NULL OR Metric=@Metric)";
        using SqlMapper.GridReader result = await sql.QueryMultipleAsync(new CommandDefinition($"""
            SELECT Metric,COUNT_BIG(*) AS [Count] FROM reporting.WorkflowFacts WHERE {where} GROUP BY Metric;
            SELECT COUNT_BIG(*) FROM reporting.WorkflowFacts WHERE {where};
            SELECT Metric,LogicalId,Module,RecordId,Reference,RecordType,Status,Actor,Assignee,OccurredAt,Detail
            FROM reporting.WorkflowFacts WHERE {where} ORDER BY Metric,LogicalId
            OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;
            """, new
        {
            id,
            filter.Module,
            filter.Status,
            filter.RecordType,
            filter.Metric,
            offset = export ? 0 : (filter.Page - 1) * filter.PageSize,
            size = export ? MaximumFacts : filter.PageSize
        }, tx, 30, cancellationToken: token));
        var counts = (await result.ReadAsync<CountRow>()).ToDictionary(x => x.Metric, x => x.Count, StringComparer.Ordinal);
        long total = await result.ReadSingleAsync<long>();
        WorkflowFact[] rows = (await result.ReadAsync<WorkflowFact>()).ToArray();
        await result.DisposeAsync();
        await AuditAsync(sql, tx, user.Id, correlation, id, export ? "Exported" : "Read", token);
        await tx.CommitAsync(token);
        WorkflowMetric[] metrics = WorkflowMetricCatalog.Definitions.Where(d => counts.ContainsKey(d.Key))
            .Select(d => d with { Count = counts[d.Key] }).ToArray();
        return new(cut.Id, cut.AsOf, cut.ExpiresAt, JsonSerializer.Deserialize<WorkflowReportRequest>(cut.RequestJson)!,
            metrics, rows, total, filter.Page, export ? MaximumFacts : filter.PageSize,
            JsonSerializer.Deserialize<string[]>(cut.LimitationsJson)!)
        { Filter = filter, Readiness = JsonSerializer.Deserialize<WorkflowReadiness[]>(cut.ReadinessJson)! };
    }

    private static Task AuditAsync(SqlConnection sql, SqlTransaction tx, Guid actor, string correlation, Guid id, string outcome, CancellationToken token) =>
        sql.ExecuteAsync(new CommandDefinition("INSERT audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson) VALUES(SYSDATETIMEOFFSET(),@actor,'WorkflowReportRead',@correlation,@details);",
            new { actor = actor.ToString("D"), correlation, details = JsonSerializer.Serialize(new { id, outcome }) }, tx, 15, cancellationToken: token));
    private sealed record Snapshot(Guid Id, DateTimeOffset AsOf, DateTimeOffset ExpiresAt, string RequestJson, string LimitationsJson, string ReadinessJson);
    private sealed record CountRow(string Metric, long Count);
}
