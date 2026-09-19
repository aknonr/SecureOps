using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.Reporting;

public sealed partial class SqlWorkflowReportStore
{
    /// <summary>Authorizes before count/filter/page; current record scope is global In Use view/review.</summary>
    public async Task<InUseReportPage?> CatalogueAsync(ApplicationUser user, InUseReportQuery query,
        string correlation, CancellationToken token)
    {
        await using var sql = new SqlConnection(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await SqlAccessRepository.LockAdministrationAsync(sql, tx, token);
        int authority = await sql.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM security.Users u WHERE u.UserId=@Id AND u.AccessStatus='Approved' AND u.AccessVersion=@Version
              AND EXISTS(SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value='InUse.View')
              AND EXISTS(SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value='InUse.Review');
            """, new { user.Id, user.Version }, tx, 15, cancellationToken: token));
        if (authority != 1)
        { return null; }
        const string from = """
            FROM reporting.InUseReportCatalogue c JOIN reporting.InUseArchiveReceipts a ON a.RecordId=c.RecordId AND a.ReportVersion=c.ReportVersion
            JOIN ops.InUseRecords r ON r.Id=c.RecordId
            CROSS APPLY(SELECT CASE WHEN JSON_VALUE(r.RecordJson,'$.Discarded')='true' THEN 'Discarded'
                WHEN c.ReportVersion<=COALESCE(TRY_CONVERT(bigint,JSON_VALUE(r.RecordJson,'$.InvalidatedReviewsThrough')),0) THEN 'Superseded'
                WHEN c.ReportVersion<r.Version THEN 'Superseded' ELSE 'Current' END AS Status) state
            WHERE (@Search IS NULL OR CHARINDEX(@Search,c.SourceCode)>0 OR CHARINDEX(@Search,a.PreparedByLabel)>0
                OR CHARINDEX(@Search,c.PreparedByAccount)>0 OR EXISTS(SELECT 1 FROM OPENJSON(c.HostsJson) h WHERE CHARINDEX(@Search,h.value)>0))
              AND (@From IS NULL OR a.PreparedAt>=@From) AND (@To IS NULL OR a.PreparedAt<@To)
              AND (@Version IS NULL OR c.ReportVersion=@Version) AND (@Status IS NULL OR state.Status=@Status)
            """;
        using SqlMapper.GridReader results = await sql.QueryMultipleAsync(new CommandDefinition($"""
            SELECT COUNT_BIG(*) {from};
            SELECT c.RecordId,c.ReportVersion AS Version,r.Version AS CurrentRecordVersion,c.SourceCode,c.HostsJson,
                a.PreparedBy,a.PreparedByLabel,c.PreparedByAccount,NULLIF(a.PreparedAt,CONVERT(datetimeoffset,'0001-01-01')) AS PreparedAt,
                c.DownloadName AS FileName,a.Sha256,state.Status,
                CASE WHEN EXISTS(SELECT 1 FROM ops.InUseExecutions e JOIN ops.InUseExecutionEvents v ON v.OperationId=e.OperationId
                    WHERE e.RecordId=c.RecordId AND e.ReportVersion=c.ReportVersion AND JSON_VALUE(e.IntentJson,'$.ReportSha256')=a.Sha256
                      AND JSON_VALUE(v.EvidenceJson,'$.Step')='Attachment' AND JSON_VALUE(v.EvidenceJson,'$.Outcome')='Verified')
                    THEN 'Verified'
                    WHEN EXISTS(SELECT 1 FROM ops.InUseExecutions e WHERE e.RecordId=c.RecordId AND e.ReportVersion=c.ReportVersion
                        AND e.State='Unknown') THEN 'Unknown' ELSE 'NotVerified' END AS AttachmentStatus
            {from} ORDER BY a.PreparedAt DESC,c.RecordId,c.ReportVersion DESC OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;
            SELECT SYSDATETIMEOFFSET();
            """, new
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.From,
            query.To,
            query.Version,
            query.Status,
            offset = (query.Page - 1) * query.PageSize,
            size = query.PageSize
        }, tx, 30, cancellationToken: token));
        long total = await results.ReadSingleAsync<long>();
        CatalogueRow[] rows = (await results.ReadAsync<CatalogueRow>()).ToArray();
        DateTimeOffset asOf = await results.ReadSingleAsync<DateTimeOffset>();
        await results.DisposeAsync();
        await AuditAsync(sql, tx, user.Id, correlation, Guid.Empty, "InUseCatalogueRead", token);
        await tx.CommitAsync(token);
        return new(rows.Select(r => new InUseReportEntry(r.RecordId, r.Version, r.CurrentRecordVersion, r.SourceCode,
            JsonSerializer.Deserialize<string[]>(r.HostsJson)!, r.PreparedBy, r.PreparedByLabel, r.PreparedByAccount,
            r.PreparedAt, r.FileName, r.Sha256, r.Status, r.AttachmentStatus)).ToArray(), total, query.Page, query.PageSize,
            asOf, "VerifiedIndexedArchivesOnly;MissingHistoricalMetadataNotInferred");
    }
    private sealed record CatalogueRow(Guid RecordId, long Version, long CurrentRecordVersion, string? SourceCode,
        string HostsJson, Guid PreparedBy, string? PreparedByLabel, string? PreparedByAccount, DateTimeOffset? PreparedAt,
        string FileName, string Sha256, string Status, string AttachmentStatus);
}
