using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements;

public sealed partial class SqlAnnouncementStore
{
    /// <summary>Owner-filtered latest IDs and page are selected in SQL under one serializable read.</summary>
    public async Task<AnnouncementPage> ListAsync(Guid owner, int page, int pageSize, CancellationToken token)
    {
        if (owner == Guid.Empty || page is < 1 or > 10000 || pageSize is < 1 or > 100)
        { throw new ArgumentOutOfRangeException(nameof(page)); }
        await using var connection = new SqlConnection(_connection);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await OwnerLockAsync(connection, transaction, owner, "Shared", token);
        using SqlMapper.GridReader rows = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT Id, MAX(Version) AS Version INTO #LatestAnnouncements
            FROM announcements.DraftRevisions WHERE OwnerId=@owner GROUP BY Id;
            SELECT COUNT(*) FROM #LatestAnnouncements;
            SELECT d.Id,d.Version,JSON_VALUE(d.DocumentJson,'$.Content.OcoReference') AS OcoReference,
                JSON_VALUE(d.DocumentJson,'$.Content.Subject') AS Subject,
                JSON_VALUE(d.DocumentJson,'$.Content.WorkStart') AS WorkStart,
                JSON_VALUE(d.DocumentJson,'$.Content.WorkEnd') AS WorkEnd,
                CONVERT(datetimeoffset,JSON_VALUE(d.DocumentJson,'$.SavedAt')) AS UpdatedAt,
                CONVERT(int,JSON_VALUE(d.DocumentJson,'$.MissingFieldCount')) AS MissingFieldCount
            FROM #LatestAnnouncements l JOIN announcements.DraftRevisions d ON d.Id=l.Id AND d.Version=l.Version
            WHERE d.OwnerId=@owner
            ORDER BY UpdatedAt DESC, CONVERT(char(36),d.Id) COLLATE Latin1_General_100_BIN2
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            DROP TABLE #LatestAnnouncements;
            """, new { owner, offset = (page - 1) * pageSize, pageSize }, transaction, commandTimeout: 15, cancellationToken: token));
        int total = await rows.ReadSingleAsync<int>();
        AnnouncementSummary[] items = [.. await rows.ReadAsync<AnnouncementSummary>()];
        await transaction.CommitAsync(token);
        return new(items, page, pageSize, total);
    }

    // Acquire before touching either index: list's owner-index scan and save's PK lookup must not invert locks.
    private static async Task OwnerLockAsync(SqlConnection connection, SqlTransaction transaction, Guid owner, string mode, CancellationToken token)
    {
        int result = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode=@mode,
                @LockOwner='Transaction',@DbPrincipal='public',@LockTimeout=5000;
            SELECT @result;
            """, new { resource = "SecureOps:AnnouncementOwner:" + owner.ToString("D"), mode }, transaction, commandTimeout: 15, cancellationToken: token));
        if (result < 0)
        { throw new InvalidOperationException("Announcement owner lock unavailable."); }
    }

    /// <summary>Metadata read audit contains counts, not subjects, recipients or file names.</summary>
    public async Task DiscoveryAuditAsync(Guid owner, bool banners, int count, string correlation, CancellationToken token)
    {
        await using var connection = new SqlConnection(_connection);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(@now,@actor,@action,@correlation,@details);
            """, new
        {
            now = DateTimeOffset.UtcNow,
            actor = owner.ToString("D"),
            correlation,
            action = banners ? "AnnouncementBannersRead" : "AnnouncementDraftsListed",
            details = System.Text.Json.JsonSerializer.Serialize(new { Count = count })
        }, commandTimeout: 15, cancellationToken: token));
    }
}
