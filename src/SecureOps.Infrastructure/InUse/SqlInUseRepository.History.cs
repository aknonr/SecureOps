using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public sealed partial class SqlInUseRepository
{
    /// <inheritdoc />
    public async Task<(IReadOnlyList<InUseServerReview> Items, int Total)> HistoryAsync(string identityKey, string? search,
        int page, int pageSize, CancellationToken token)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        const string where = " FROM ops.InUseServerReviews h JOIN ops.InUseRecords r ON r.Id=h.RecordId WHERE IdentityKey=@identityKey AND (@search IS NULL OR CHARINDEX(@search,SearchText COLLATE Turkish_100_CI_AS)>0)";
        using SqlMapper.GridReader rows = await connection.QueryMultipleAsync(Command("SELECT COUNT(*)" + where + "; SELECT " + _historyProjection + where
            + " ORDER BY ReviewedAt DESC,ReviewId OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;",
            new { identityKey, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), offset = (page - 1) * pageSize, pageSize }, token, transaction));
        int total = await rows.ReadSingleAsync<int>();
        InUseServerReview[] items = (await rows.ReadAsync<HistoryRow>()).Select(MapHistory).ToArray();
        await transaction.CommitAsync(token);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<InUseServerReview?> ReviewAsync(Guid reviewId, CancellationToken token)
    {
        await using SqlConnection connection = new(_connectionString);
        HistoryRow? row = await connection.QuerySingleOrDefaultAsync<HistoryRow>(Command(
            "SELECT " + _historyProjection + " FROM ops.InUseServerReviews h JOIN ops.InUseRecords r ON r.Id=h.RecordId WHERE ReviewId=@reviewId;", new { reviewId }, token));
        return row is null ? null : MapHistory(row);
    }

    private const string _historyProjection = "SnapshotJson,CAST(CASE WHEN JSON_VALUE(r.RecordJson,'$.Discarded')='true' OR h.RecordVersion<=COALESCE(TRY_CONVERT(bigint,JSON_VALUE(r.RecordJson,'$.InvalidatedReviewsThrough')),0) THEN 1 ELSE 0 END AS bit) AS Invalidated";
    private sealed record HistoryRow(string SnapshotJson, bool Invalidated);
    private static InUseServerReview MapHistory(HistoryRow row) => Read<InUseServerReview>(row.SnapshotJson) with { Invalidated = row.Invalidated };
}
