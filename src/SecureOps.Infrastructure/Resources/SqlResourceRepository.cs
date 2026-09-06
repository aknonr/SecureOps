using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Infrastructure.Resources;

/// <summary>SQL catalogue persistence. Values are parameterized; no target HTTP client is used.</summary>
/// <remarks>Uses the existing runtime-owned integrated-security connection.</remarks>
public sealed partial class SqlResourceRepository(IConfiguration configuration) : IResourceRepository
{
    private readonly string _connectionString = configuration.GetConnectionString("SecureOpsDb")
        ?? throw new InvalidOperationException("Resource SQL persistence requires SecureOpsDb configuration.");
    private const string _columns = "l.Id, l.CategoryId, l.Name, l.Url, l.Purpose, l.Notes, l.Environment, l.Location, l.TagsJson, l.DisplayOrder, l.Active, l.Archived, l.Version, l.UpdatedAt";
    private const string _visibility = "(@Manager = 1 OR c.ManagersOnly = 0) AND (@IncludeArchived = 1 OR (c.Archived = 0 AND l.Archived = 0 AND l.Active = 1))";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceCategory>> CategoriesAsync(bool manager, bool includeArchived, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        return [.. (await connection.QueryAsync<ResourceCategory>(Command("""
            SELECT TOP (200) Id, Name, DisplayOrder, ManagersOnly, Archived, Version, UpdatedAt
            FROM resources.Categories WHERE (@Manager = 1 OR ManagersOnly = 0) AND (@IncludeArchived = 1 OR Archived = 0)
            ORDER BY DisplayOrder, CONVERT(char(36), Id) COLLATE Latin1_General_100_BIN2;
            """, new { Manager = manager, IncludeArchived = manager && includeArchived }, cancellationToken)))];
    }

    /// <inheritdoc />
    public async Task<ResourcePage> QueryAsync(ResourceQuery query, bool manager, CancellationToken cancellationToken)
    {
        string where = " FROM resources.Links l JOIN resources.Categories c ON c.Id = l.CategoryId WHERE " + _visibility + """
             AND (@CategoryId IS NULL OR l.CategoryId = @CategoryId)
             AND (@Environment IS NULL OR l.Environment COLLATE Latin1_General_100_CI_AS_SC = @Environment)
             AND (@Location IS NULL OR l.Location COLLATE Latin1_General_100_CI_AS_SC = @Location)
             AND (@Tag IS NULL OR EXISTS (SELECT 1 FROM OPENJSON(l.TagsJson) t WHERE t.value COLLATE Latin1_General_100_CI_AS_SC = @Tag))
             AND (@Search IS NULL OR CHARINDEX(@Search, l.Name COLLATE Latin1_General_100_CI_AS_SC) > 0
                  OR CHARINDEX(@Search, l.Purpose COLLATE Latin1_General_100_CI_AS_SC) > 0
                  OR EXISTS (SELECT 1 FROM OPENJSON(l.TagsJson) t WHERE CHARINDEX(@Search, t.value COLLATE Latin1_General_100_CI_AS_SC) > 0))
            """;
        string sql = "SELECT COUNT(*)" + where + "; SELECT " + _columns + where + """
             ORDER BY c.DisplayOrder, l.DisplayOrder, CONVERT(char(36), l.Id) COLLATE Latin1_General_100_BIN2
             OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        using SqlMapper.GridReader rows = await connection.QueryMultipleAsync(Command(sql, new
        {
            Manager = manager,
            IncludeArchived = manager && query.IncludeArchived,
            query.CategoryId,
            Environment = Filter(query.Environment),
            Location = Filter(query.Location),
            Tag = Filter(query.Tag),
            Search = Filter(query.Search),
            Offset = (query.Page - 1) * query.PageSize,
            query.PageSize
        }, cancellationToken, transaction));
        int count = await rows.ReadSingleAsync<int>();
        ResourceLink[] links = [.. (await rows.ReadAsync<LinkRow>()).Select(Map)];
        await transaction.CommitAsync(cancellationToken);
        return new(links, query.Page, query.PageSize, count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceLink>> ResolveAsync(IReadOnlyCollection<Guid> ids, bool manager, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using SqlConnection connection = new(_connectionString);
        string sql = "SELECT " + _columns + " FROM resources.Links l JOIN resources.Categories c ON c.Id = l.CategoryId WHERE "
            + _visibility + " AND l.Id IN (SELECT TRY_CONVERT(uniqueidentifier, value) FROM OPENJSON(@Ids));";
        return [.. (await connection.QueryAsync<LinkRow>(Command(sql, new { Ids = JsonSerializer.Serialize(ids), Manager = manager, IncludeArchived = false }, cancellationToken))).Select(Map)];
    }

    /// <inheritdoc />
    public async Task<ResourceLink?> GetAsync(Guid id, bool manager, bool includeArchived, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        string sql = "SELECT " + _columns + " FROM resources.Links l JOIN resources.Categories c ON c.Id = l.CategoryId WHERE l.Id = @Id AND " + _visibility;
        LinkRow? row = await connection.QuerySingleOrDefaultAsync<LinkRow>(Command(sql, new { Id = id, Manager = manager, IncludeArchived = manager && includeArchived }, cancellationToken));
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<ResourcePreferences> PreferencesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        string? json = await connection.QuerySingleOrDefaultAsync<string>(Command("SELECT PreferencesJson FROM resources.PersonalPreferences WHERE UserId = @UserId;", new { UserId = userId }, cancellationToken));
        return json is null ? ResourcePreferences.Empty : JsonSerializer.Deserialize<ResourcePreferences>(json)
            ?? throw new InvalidOperationException("Personal resource state is invalid.");
    }

    private static string? Filter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <inheritdoc />
    public async Task<ResourceEnvironmentOptions> EnvironmentsAsync(ResourceEnvironmentQuery query, bool manager, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        string sql = "SELECT DISTINCT TOP (101) l.Environment COLLATE Latin1_General_100_CI_AS_SC AS Value"
            + " FROM resources.Links l JOIN resources.Categories c ON c.Id = l.CategoryId WHERE " + _visibility + """
             AND l.Environment IS NOT NULL AND LEN(l.Environment) > 0
             AND (@CategoryId IS NULL OR l.CategoryId = @CategoryId)
             AND (@Search IS NULL OR CHARINDEX(@Search, l.Environment COLLATE Latin1_General_100_CI_AS_SC) > 0)
             ORDER BY Value;
            """;
        string[] values = [.. await connection.QueryAsync<string>(Command(sql, new
        {
            Manager = manager, IncludeArchived = manager && query.IncludeArchived,
            query.CategoryId, Search = Filter(query.Search)
        }, cancellationToken))];
        return new([.. values.Take(100)], values.Length > 100);
    }
    private static CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken, SqlTransaction? transaction = null) =>
        new(sql, parameters, transaction, commandTimeout: 15, cancellationToken: cancellationToken);
    private static ResourceLink Map(LinkRow row) => new(row.Id, row.CategoryId, row.Name, row.Url, row.Purpose, row.Notes, row.Environment,
        row.Location, JsonSerializer.Deserialize<string[]>(row.TagsJson)!, row.DisplayOrder, row.Active, row.Archived, row.Version, row.UpdatedAt);

    private sealed class LinkRow
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string? Notes { get; set; }
        public string? Environment { get; set; }
        public string? Location { get; set; }
        public string TagsJson { get; set; } = "[]";
        public int DisplayOrder { get; set; }
        public bool Active { get; set; }
        public bool Archived { get; set; }
        public long Version { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
