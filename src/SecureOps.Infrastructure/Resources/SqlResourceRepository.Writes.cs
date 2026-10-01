using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Infrastructure.Resources;

public sealed partial class SqlResourceRepository
{
    /// <inheritdoc />
    public async Task<ResourceResult<ResourceCategory>> SaveCategoryAsync(Guid id, SaveResourceCategoryRequest request, ResourceActor actor, CancellationToken cancellationToken)
    {
        ResourceCategory next = new(id, request.Name.Trim(), request.DisplayOrder, request.ManagersOnly, request.Archived,
            checked(request.ExpectedVersion + 1), DateTimeOffset.UtcNow);
        const string sql = """
            IF @ExpectedVersion = 0
                INSERT INTO resources.Categories(Id, Name, DisplayOrder, ManagersOnly, Archived, Version, UpdatedAt)
                VALUES(@Id, @Name, @DisplayOrder, @ManagersOnly, @Archived, @Version, @UpdatedAt);
            ELSE
                UPDATE resources.Categories SET Name = @Name, DisplayOrder = @DisplayOrder, ManagersOnly = @ManagersOnly,
                    Archived = @Archived, Version = @Version, UpdatedAt = @UpdatedAt WHERE Id = @Id AND Version = @ExpectedVersion;
            """;
        string? error = await WriteAsync("Category", id, request.ExpectedVersion, sql, next, actor, next.UpdatedAt, cancellationToken);
        return error is null ? new(next) : ResourceResult<ResourceCategory>.Fail(error);
    }

    /// <inheritdoc />
    public async Task<ResourceResult<ResourceLink>> SaveLinkAsync(Guid id, SaveResourceLinkRequest request, ResourceActor actor, CancellationToken cancellationToken)
    {
        ResourceLink next = new(id, request.CategoryId, request.Name.Trim(), request.Url, request.Purpose.Trim(), request.Notes?.Trim(),
            request.Environment?.Trim(), request.Location?.Trim(), [.. (request.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Order(StringComparer.Ordinal)],
            request.DisplayOrder, request.Active, request.Archived, checked(request.ExpectedVersion + 1), DateTimeOffset.UtcNow);
        const string sql = """
            IF @ExpectedVersion = 0
                INSERT INTO resources.Links(Id, CategoryId, Name, Url, Purpose, Notes, Environment, Location, TagsJson,
                    DisplayOrder, Active, Archived, Version, UpdatedAt)
                VALUES(@Id, @CategoryId, @Name, @Url, @Purpose, @Notes, @Environment, @Location, @TagsJson,
                    @DisplayOrder, @Active, @Archived, @Version, @UpdatedAt);
            ELSE
                UPDATE resources.Links SET CategoryId = @CategoryId, Name = @Name, Url = @Url, Purpose = @Purpose, Notes = @Notes,
                    Environment = @Environment, Location = @Location, TagsJson = @TagsJson, DisplayOrder = @DisplayOrder,
                    Active = @Active, Archived = @Archived, Version = @Version, UpdatedAt = @UpdatedAt
                WHERE Id = @Id AND Version = @ExpectedVersion;
            """;
        DynamicParameters parameters = new(new
        {
            next.Id,
            next.CategoryId,
            next.Name,
            next.Url,
            next.Purpose,
            next.Notes,
            next.Environment,
            next.Location,
            TagsJson = JsonSerializer.Serialize(next.Tags),
            next.DisplayOrder,
            next.Active,
            next.Archived,
            next.Version,
            next.UpdatedAt
        });
        string? error = await WriteAsync("Link", id, request.ExpectedVersion, sql, parameters, actor, next.UpdatedAt, cancellationToken);
        return error is null ? new(next) : ResourceResult<ResourceLink>.Fail(error);
    }

    /// <inheritdoc />
    public async Task<ResourceResult<ResourcePreferences>> SavePreferencesAsync(ResourcePreferences preferences, long expectedVersion, ResourceActor actor, CancellationToken cancellationToken)
    {
        ResourcePreferences next = preferences with { Version = checked(expectedVersion + 1) };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        const string sql = """
            IF @ExpectedVersion = 0
                INSERT INTO resources.PersonalPreferences(UserId, Version, PreferencesJson, UpdatedAt)
                VALUES(@Id, @Version, @PreferencesJson, @UpdatedAt);
            ELSE
                UPDATE resources.PersonalPreferences SET Version = @Version, PreferencesJson = @PreferencesJson, UpdatedAt = @UpdatedAt
                WHERE UserId = @Id AND Version = @ExpectedVersion;
            """;
        string? error = await WriteAsync("Preferences", actor.UserId, expectedVersion, sql,
            new { next.Version, PreferencesJson = JsonSerializer.Serialize(next), UpdatedAt = now }, actor, now, cancellationToken);
        return error is null ? new(next) : ResourceResult<ResourcePreferences>.Fail(error);
    }

    private async Task<string?> WriteAsync(string kind, Guid id, long expectedVersion, string sql, object values,
        ResourceActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        (string table, string key) = kind switch
        {
            "Category" => ("resources.Categories", "Id"),
            "Link" => ("resources.Links", "Id"),
            "Preferences" => ("resources.PersonalPreferences", "UserId"),
            _ => throw new InvalidOperationException("Unsupported resource mutation.")
        };
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        DynamicParameters parameters = new(values);
        parameters.Add("Id", id);
        parameters.Add("ExpectedVersion", expectedVersion);
        if (kind == "Category")
        {
            int count = await connection.ExecuteScalarAsync<int>(Command("SELECT COUNT(*) FROM resources.Categories WITH (UPDLOCK, HOLDLOCK);", null, cancellationToken, transaction));
            if (expectedVersion == 0 && count >= 200)
            {
                return ResourceErrors.Limit;
            }
        }
        if (kind == "Link")
        {
            bool? archived = await connection.QuerySingleOrDefaultAsync<bool?>(Command(
                "SELECT Archived FROM resources.Categories WITH (UPDLOCK, HOLDLOCK) WHERE Id = @CategoryId;", parameters, cancellationToken, transaction));
            if (archived is null or true)
            {
                return ResourceErrors.NotFound;
            }
        }
        // Table and key come only from the closed internal switch above, never request input.
        long? current = await connection.QuerySingleOrDefaultAsync<long?>(Command(
            $"SELECT Version FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE {key} = @Id;", parameters, cancellationToken, transaction));
        if ((current ?? 0) != expectedVersion)
        {
            return ResourceErrors.Conflict;
        }

        await connection.ExecuteAsync(Command(sql, parameters, cancellationToken, transaction));

        // A queued audit writer cannot join this transaction. Use the existing audit table atomically.
        AuditEvent evidence = ResourceAudit.Change(kind, id, expectedVersion + 1, actor, now,
            kind == "Preferences" ? null : parameters.Get<bool>("Archived"),
            kind == "Link" ? parameters.Get<bool>("Active") : null);
        await connection.ExecuteAsync(Command("""
            INSERT INTO audit.AuditLog(OccurredAt, Actor, Action, CorrelationId, DetailsJson)
            VALUES(@OccurredAt, @Actor, @Action, @CorrelationId, @DetailsJson);
            """, new
        {
            evidence.OccurredAt,
            evidence.Actor,
            evidence.Action,
            evidence.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(evidence.Details)
        }, cancellationToken, transaction));
        await transaction.CommitAsync(cancellationToken);
        return null;
    }
}
