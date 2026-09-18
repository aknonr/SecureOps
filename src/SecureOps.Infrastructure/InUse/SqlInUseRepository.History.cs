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
        const string where = " FROM ops.InUseServerReviews WHERE IdentityKey=@identityKey AND (@search IS NULL OR CHARINDEX(@search,SearchText COLLATE Turkish_100_CI_AS)>0)";
        using SqlMapper.GridReader rows = await connection.QueryMultipleAsync(Command("SELECT COUNT(*)" + where + "; SELECT SnapshotJson" + where
            + " ORDER BY ReviewedAt DESC,ReviewId OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;",
            new { identityKey, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), offset = (page - 1) * pageSize, pageSize }, token, transaction));
        int total = await rows.ReadSingleAsync<int>();
        InUseServerReview[] items = (await rows.ReadAsync<string>()).Select(Read<InUseServerReview>).ToArray();
        await transaction.CommitAsync(token);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<InUseServerReview?> ReviewAsync(Guid reviewId, CancellationToken token)
    {
        await using SqlConnection connection = new(_connectionString);
        string? json = await connection.QuerySingleOrDefaultAsync<string>(Command(
            "SELECT SnapshotJson FROM ops.InUseServerReviews WHERE ReviewId=@reviewId;", new { reviewId }, token));
        return json is null ? null : Read<InUseServerReview>(json);
    }
}
