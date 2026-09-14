using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements;

public sealed partial class SqlAnnouncementStore
{
    /// <summary>Atomic preparation/audit with the existing owner lock and revision fence.</summary>
    public async Task<PreparationOutcome> PrepareAsync(PreparedAnnouncement snapshot, string correlation, CancellationToken token)
    {
        await using var sql = new SqlConnection(_connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await OwnerLockAsync(sql, tx, snapshot.Draft.OwnerId, "Exclusive", token);
        string? old = await sql.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT DocumentJson FROM announcements.Preparations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;", new { snapshot.Id }, tx, commandTimeout: 15, cancellationToken: token));
        if (old is not null)
        {
            PreparedAnnouncement prior = JsonSerializer.Deserialize<PreparedAnnouncement>(old)!;
            if (prior.Draft.OwnerId != snapshot.Draft.OwnerId)
            { return new(Error: "AnnouncementNotFound"); }
            if (JsonSerializer.Serialize(prior.Draft) != JsonSerializer.Serialize(snapshot.Draft) || prior.Html != snapshot.Html
                || JsonSerializer.Serialize(prior.AssetRevisions) != JsonSerializer.Serialize(snapshot.AssetRevisions))
            { return new(Error: "AnnouncementPreparationConflict"); }
            return new(prior);
        }
        long latest = await sql.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT MAX(Version) FROM announcements.DraftRevisions WHERE Id=@Id AND OwnerId=@OwnerId;", new { snapshot.Draft.Id, snapshot.Draft.OwnerId }, tx, commandTimeout: 15, cancellationToken: token));
        if (latest != snapshot.Draft.Version)
        { return new(Error: "AnnouncementConflict"); }
        AnnouncementSourceOverrides review = await Sources.SqlAnnouncementSourceStore.ReadOverridesAsync(
            sql, tx, snapshot.Draft.Id, snapshot.Draft.OwnerId, token);
        if (review.AppliedJobId is not null)
        {
            snapshot = snapshot with { SourceReview = review };
            snapshot = snapshot with { Fingerprint = AnnouncementService.PreparationFingerprint(snapshot) };
        }
        string json = JsonSerializer.Serialize(snapshot);
        if (System.Text.Encoding.Unicode.GetByteCount(json) > 24000000 || snapshot.Email.Length > 3000000)
        { return new(Error: "AnnouncementInvalid"); }
        await sql.ExecuteAsync(new CommandDefinition("""
            INSERT INTO announcements.Preparations(Id,OwnerId,DraftId,DraftVersion,PreparedAt,PreparedBy,Subject,DocumentJson)
            VALUES(@Id,@OwnerId,@DraftId,@Version,@PreparedAt,@PreparedBy,@Subject,@json);
            """, new { snapshot.Id, snapshot.Draft.OwnerId, DraftId = snapshot.Draft.Id, snapshot.Draft.Version, snapshot.PreparedAt, snapshot.PreparedBy, snapshot.Draft.Content.Subject, json }, tx, commandTimeout: 15, cancellationToken: token));
        await PreparationAuditAsync(sql, tx, snapshot.Draft.OwnerId, snapshot.Id, "AnnouncementPrepared", correlation, token);
        await tx.CommitAsync(token);
        return new(snapshot);
    }
    /// <summary>Loads one authorized stored artifact; never resolves mutable files or latest draft.</summary>
    public async Task<PreparedAnnouncement?> PreparedAsync(Guid id, Guid owner, CancellationToken token)
    {
        await using var sql = new SqlConnection(_connection);
        string? json = await sql.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT DocumentJson FROM announcements.Preparations WHERE Id=@id AND OwnerId=@owner;", new { id, owner }, commandTimeout: 15, cancellationToken: token));
        return json is null ? null : JsonSerializer.Deserialize<PreparedAnnouncement>(json);
    }
    /// <summary>SQL ownership, count/order/page; covering index avoids reading large snapshot JSON.</summary>
    public async Task<PreparationPage> PreparationPageAsync(Guid owner, int page, int pageSize, string correlation, CancellationToken token)
    {
        await using var sql = new SqlConnection(_connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await OwnerLockAsync(sql, tx, owner, "Shared", token);
        using SqlMapper.GridReader rows = await sql.QueryMultipleAsync(new CommandDefinition("""
            SELECT COUNT(*) FROM announcements.Preparations WHERE OwnerId=@owner;
            SELECT Id,Subject,DraftVersion AS Version,PreparedAt,PreparedBy FROM announcements.Preparations
            WHERE OwnerId=@owner ORDER BY PreparedAt DESC,Id OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """, new { owner, offset = (page - 1) * pageSize, pageSize }, tx, commandTimeout: 15, cancellationToken: token));
        int count = await rows.ReadSingleAsync<int>();
        PreparationSummary[] items = [.. await rows.ReadAsync<PreparationSummary>()];
        await PreparationAuditAsync(sql, tx, owner, Guid.Empty, "AnnouncementPreparationsListed", correlation, token);
        await tx.CommitAsync(token);
        return new(items, page, pageSize, count);
    }
    /// <summary>Fail-closed owned history read audit, no content or addresses in audit details.</summary>
    public async Task PreparationReadAuditAsync(PreparedAnnouncement snapshot, string correlation, CancellationToken token)
    { await using var sql = new SqlConnection(_connection); await PreparationAuditAsync(sql, null, snapshot.Draft.OwnerId, snapshot.Id, "AnnouncementPreparationRead", correlation, token); }
    private static Task PreparationAuditAsync(SqlConnection sql, SqlTransaction? tx, Guid owner, Guid id, string action, string correlation, CancellationToken token) =>
        sql.ExecuteAsync(new CommandDefinition("INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson) VALUES(@now,@actor,@action,@correlation,@details);",
            new { now = DateTimeOffset.UtcNow, actor = owner.ToString("D"), action, correlation, details = JsonSerializer.Serialize(new { PreparationId = id }) }, tx, commandTimeout: 15, cancellationToken: token));
}
