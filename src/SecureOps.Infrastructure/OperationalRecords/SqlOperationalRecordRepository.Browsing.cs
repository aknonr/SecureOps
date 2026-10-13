using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class SqlOperationalRecordRepository
{
    /// <inheritdoc />
    public async Task<OperationalRecordPage> BrowseAsync(OperationalRecordQuery query, bool excludeSynthetic, CancellationToken cancellationToken)
    {
        OperationalRecordBrowsing.Validate(query);
        string order = query.Sort switch
        {
            // An absent creation time is not an old one: unknown dates sort last.
            "oldest" => "CASE WHEN r.SourceCreatedAt IS NULL THEN 1 ELSE 0 END ASC, r.SourceCreatedAt ASC",
            "code" => "r.OrCode ASC",
            _ => "r.UpdatedAt DESC"
        };
        const string where = """
            WHERE (@State IS NULL OR r.WorkflowState = @State)
              AND (@Search = '' OR CHARINDEX(@Search, r.OrCode) > 0 OR CHARINDEX(@Search, r.Title) > 0)
              AND (@ExcludeSynthetic = 0 OR (
                r.SourceRecordId NOT LIKE 'synthetic-%' AND r.SourceRecordId NOT LIKE 'SYN-%'
                AND r.SourceRecordId NOT LIKE 'SIM-%' AND r.SourceRecordId NOT LIKE 'simulation-%' AND r.SourceRecordId NOT LIKE 'FAKE-%'
                AND r.OrCode NOT LIKE 'synthetic-%' AND r.OrCode NOT LIKE 'SYN-%'
                AND r.OrCode NOT LIKE 'SIM-%' AND r.OrCode NOT LIKE 'simulation-%' AND r.OrCode NOT LIKE 'FAKE-%'))
            """;
        var parameters = new
        {
            State = query.State?.ToString(),
            Search = query.Search?.Trim() ?? string.Empty,
            ExcludeSynthetic = excludeSynthetic,
            Offset = (query.Page - 1) * query.PageSize,
            query.PageSize
        };
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // Keep count and rows coherent without requiring a database snapshot-isolation setting.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        (int total, int unknownCreatedAt) = await connection.QuerySingleAsync<(int, int)>(Command(
            $"SELECT COUNT(*), COALESCE(SUM(CASE WHEN r.SourceCreatedAt IS NULL THEN 1 ELSE 0 END), 0) FROM ops.OperationalRecords r {where}",
            parameters, cancellationToken, transaction));
        IEnumerable<OperationalRecordRow> rows = await connection.QueryAsync<OperationalRecordRow>(Command(
            $"{_readSql} {where} ORDER BY {order}, r.SourceRecordId ASC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            parameters, cancellationToken, transaction));
        OperationalRecordPage page = new(rows.Select(Map).ToArray(), total, unknownCreatedAt);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }
}
