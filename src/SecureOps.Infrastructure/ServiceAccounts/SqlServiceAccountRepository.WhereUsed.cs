using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>One attached scan reduced to this account's searched name.</summary>
public sealed record WhereUsedScanRow(Guid LinkId, string Tool, string Purpose, DateTimeOffset CombinedAt, DateTimeOffset? LastScannedAt, DateTimeOffset LinkedAt);

/// <summary>One planned server of an attached scan with the number of components that run as the searched account.</summary>
public sealed record WhereUsedScanServerRow(Guid LinkId, string ServerName, string Result, DateTimeOffset? ScannedAt, int Matches);

/// <summary>One matched component (former-account role) of an attached scan.</summary>
public sealed record WhereUsedScanItemRow(Guid LinkId, string ServerName, string ComponentType, string ComponentName);

/// <summary>An Excel technical finding that names a server.</summary>
public sealed record WhereUsedFindingRow(string Server, string? ComponentType, string? ComponentName, DateTimeOffset? At, string ScanResult, string MatchResult);

/// <summary>A manual usage record that names a server.</summary>
public sealed record WhereUsedUsageRow(string Server, string UsageKind, string? Component, DateTimeOffset At);

/// <summary>Everything the where-used view needs from SQL, read in one round trip.</summary>
public sealed record WhereUsedData(bool ScansInstalled, int ScansTotal, IReadOnlyList<WhereUsedScanRow> Scans, IReadOnlyList<WhereUsedScanServerRow> ScanServers,
    IReadOnlyList<WhereUsedScanItemRow> ScanItems, bool ItemsTruncated, IReadOnlyList<WhereUsedFindingRow> Findings, int FindingsWithoutServer,
    IReadOnlyList<WhereUsedUsageRow> Usages, int UsagesWithoutServer);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>
    /// Raw evidence of the three stored sources for one account (scans, Excel findings, manual usages): the newest
    /// <see cref="WhereUsedPaging.MaxScans"/> scans with every planned server, at most <see cref="WhereUsedPaging.MaxComponents"/>
    /// matched components (per-server match counts are exact), and the account's server-named findings and usages. Read only,
    /// no per-row queries; the caller has already decided the account is in scope. Before migration 030 the scan part is empty
    /// and <c>ScansInstalled</c> is false.
    /// </summary>
    public Task<WhereUsedData> WhereUsedDataAsync(Guid accountId, CancellationToken cancellationToken) =>
        RetryReadOnDeadlockAsync(() => WhereUsedDataOnceAsync(accountId, cancellationToken));

    private async Task<WhereUsedData> WhereUsedDataOnceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        bool installed = await connection.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NULL THEN 0 ELSE 1 END;", null, null,
            cancellationToken)) == 1;
        int total = 0;
        bool truncated = false;
        WhereUsedScanRow[] scans = [];
        WhereUsedScanServerRow[] servers = [];
        WhereUsedScanItemRow[] items = [];
        if (installed)
        {
            using SqlMapper.GridReader scanGrid = await connection.QueryMultipleAsync(Cmd("""
                DECLARE @links TABLE(LinkId uniqueidentifier NOT NULL PRIMARY KEY, ScanId uniqueidentifier NOT NULL, MatchedAccount nvarchar(100) NOT NULL, Ord int NOT NULL);
                INSERT INTO @links(LinkId, ScanId, MatchedAccount, Ord)
                SELECT TOP (@maxScans) l.Id, l.ScanId, l.MatchedAccount, ROW_NUMBER() OVER (ORDER BY l.LinkedAt DESC, l.Id)
                FROM svcacct.UsageScanLinks l WHERE l.AccountId = @accountId ORDER BY l.LinkedAt DESC, l.Id;
                SELECT (SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE AccountId = @accountId) AS Total,
                       CAST(CASE WHEN (SELECT COUNT(*) FROM @links k JOIN svcacct.UsageScanItems i ON i.ScanId = k.ScanId AND i.Role = 'Former'
                                       AND i.MatchedAccount = k.MatchedAccount) > @maxComponents THEN 1 ELSE 0 END AS bit) AS ItemsTruncated;
                SELECT k.LinkId, s.Tool, s.Purpose, s.CombinedAt, s.LastScannedAt, l.LinkedAt
                FROM @links k JOIN svcacct.UsageScanLinks l ON l.Id = k.LinkId JOIN svcacct.UsageScans s ON s.Id = k.ScanId ORDER BY k.Ord;
                SELECT k.LinkId, sv.ServerName, sv.Result, sv.ScannedAt,
                       (SELECT COUNT(*) FROM svcacct.UsageScanItems i WHERE i.ScanId = k.ScanId AND i.ServerName = sv.ServerName AND i.Role = 'Former'
                           AND i.MatchedAccount = k.MatchedAccount) AS Matches
                FROM @links k JOIN svcacct.UsageScanServers sv ON sv.ScanId = k.ScanId ORDER BY k.Ord, sv.ServerName;
                SELECT TOP (@maxComponents) k.LinkId, i.ServerName, i.ComponentType, i.ComponentName
                FROM @links k JOIN svcacct.UsageScanItems i ON i.ScanId = k.ScanId AND i.Role = 'Former' AND i.MatchedAccount = k.MatchedAccount
                ORDER BY k.Ord, i.ServerName, i.ComponentType, i.ComponentName, i.Id;
                """, new { accountId, maxScans = WhereUsedPaging.MaxScans, maxComponents = WhereUsedPaging.MaxComponents }, null, cancellationToken));
            (total, truncated) = await scanGrid.ReadSingleAsync<(int Total, bool ItemsTruncated)>();
            scans = [.. await scanGrid.ReadAsync<WhereUsedScanRow>()];
            servers = [.. await scanGrid.ReadAsync<WhereUsedScanServerRow>()];
            items = [.. await scanGrid.ReadAsync<WhereUsedScanItemRow>()];
        }

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT TOP (@maxRows) Server, ComponentType, ComponentName, COALESCE(ScanAt, CAST(ScanOn AS datetimeoffset(7))) AS [At], ScanResult, MatchResult
            FROM svcacct.Findings WHERE AccountId = @accountId AND Server IS NOT NULL AND LTRIM(RTRIM(Server)) <> N''
            ORDER BY COALESCE(ScanAt, CAST(ScanOn AS datetimeoffset(7))) DESC, Id;
            SELECT COUNT(*) FROM svcacct.Findings WHERE AccountId = @accountId AND (Server IS NULL OR LTRIM(RTRIM(Server)) = N'');
            SELECT TOP (@maxRows) Server, UsageKind, Component, CreatedAt AS [At]
            FROM svcacct.AccountUsages WHERE AccountId = @accountId AND RemovedAt IS NULL AND Server IS NOT NULL AND LTRIM(RTRIM(Server)) <> N''
            ORDER BY CreatedAt DESC, Id;
            SELECT COUNT(*) FROM svcacct.AccountUsages WHERE AccountId = @accountId AND RemovedAt IS NULL AND (Server IS NULL OR LTRIM(RTRIM(Server)) = N'');
            """, new { accountId, maxRows = 5000 }, null, cancellationToken));
        WhereUsedFindingRow[] findings = [.. await grid.ReadAsync<WhereUsedFindingRow>()];
        int findingsWithout = await grid.ReadSingleAsync<int>();
        WhereUsedUsageRow[] usages = [.. await grid.ReadAsync<WhereUsedUsageRow>()];
        int usagesWithout = await grid.ReadSingleAsync<int>();
        return new WhereUsedData(installed, total, scans, servers, items, truncated, findings, findingsWithout, usages, usagesWithout);
    }
}
