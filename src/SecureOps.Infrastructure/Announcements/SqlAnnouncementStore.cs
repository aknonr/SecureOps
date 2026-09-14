using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Announcements;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Independent append-only revisions; owner and version are checked inside the audit transaction.</summary>
public sealed partial class SqlAnnouncementStore(IConfiguration configuration)
{
    private readonly string _connection = configuration.GetConnectionString("SecureOpsDb") ?? "";
    /// <summary>Returns the caller's latest stored revision only.</summary>
    public async Task<AnnouncementDraft?> GetAsync(Guid id, Guid owner, CancellationToken token)
    {
        await using var connection = new SqlConnection(_connection);
        string? json = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT TOP(1) DocumentJson FROM announcements.DraftRevisions WHERE Id=@id AND OwnerId=@owner ORDER BY Version DESC;",
            new { id, owner }, commandTimeout: 15, cancellationToken: token));
        return json is null ? null : JsonSerializer.Deserialize<AnnouncementDraft>(json);
    }

    /// <summary>Appends a revision and safe audit metadata atomically; stale writes have no effect.</summary>
    public Task<string?> SaveAsync(AnnouncementDraft draft, string correlation, CancellationToken token) =>
        SaveAsync(draft, correlation, null, token);

    internal async Task<string?> SaveAsync(AnnouncementDraft draft, string correlation,
        Func<SqlConnection, SqlTransaction, CancellationToken, Task<string?>>? reviewedWrite, CancellationToken token)
    {
        draft = draft with { MissingFieldCount = AnnouncementValidation.Errors(draft.Content, true).Length };
        string json = JsonSerializer.Serialize(draft);
        if (System.Text.Encoding.Unicode.GetByteCount(json) > 262144)
        { return "AnnouncementInvalid"; }
        await using var connection = new SqlConnection(_connection);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await OwnerLockAsync(connection, transaction, draft.OwnerId, "Exclusive", token);
        (Guid Owner, long Version)? current = await connection.QuerySingleOrDefaultAsync<(Guid, long)?>(new CommandDefinition(
            "SELECT TOP(1) OwnerId, Version FROM announcements.DraftRevisions WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id ORDER BY Version DESC;",
            new { draft.Id }, transaction, commandTimeout: 15, cancellationToken: token));
        if (current.HasValue && current.Value.Owner != draft.OwnerId)
        { return "AnnouncementNotFound"; }
        if ((current?.Version ?? 0) != draft.Version - 1)
        { return "AnnouncementConflict"; }
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO announcements.DraftRevisions(Id,Version,OwnerId,DocumentJson) VALUES(@Id,@Version,@OwnerId,@json);",
            new { draft.Id, draft.Version, draft.OwnerId, json }, transaction, commandTimeout: 15, cancellationToken: token));
        await AuditAsync(connection, transaction, draft, "AnnouncementDraftSaved", correlation, token);
        if (reviewedWrite is not null)
        {
            string? error = await reviewedWrite(connection, transaction, token);
            if (error is not null)
            { return error; }
        }
        await transaction.CommitAsync(token);
        return null;
    }

    /// <summary>Records a privileged read or download preparation, never sending; fails closed.</summary>
    public async Task ReadAuditAsync(AnnouncementDraft draft, bool download, string correlation, CancellationToken token)
    {
        await using var connection = new SqlConnection(_connection);
        await AuditAsync(connection, null, draft, download ? "AnnouncementDownloadPrepared" : "AnnouncementDraftRead", correlation, token);
    }
    private static Task AuditAsync(SqlConnection connection, SqlTransaction? transaction, AnnouncementDraft draft, string action, string correlation, CancellationToken token) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(@now,@actor,@action,@correlation,@details);
            """, new
        {
            now = DateTimeOffset.UtcNow,
            actor = draft.OwnerId.ToString("D"),
            action,
            correlation,
            details = JsonSerializer.Serialize(new { draft.Id, draft.Version, draft.TemplateRevision, draft.BannerHash })
        },
            transaction, commandTimeout: 15, cancellationToken: token));
}
