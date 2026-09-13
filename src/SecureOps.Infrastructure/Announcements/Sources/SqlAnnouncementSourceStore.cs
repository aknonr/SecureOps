using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Announcements;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Durable owner-scoped source job and override storage. Job outcomes are persisted in SQL rather than
/// held by the job host, so an API or Worker restart never erases a completed result. Every read is
/// filtered by owner; a job is never returned to a principal that did not submit it.
/// </summary>
public sealed class SqlAnnouncementSourceStore(IConfiguration configuration)
{
    private const string Columns = "JobId,OwnerId,DraftId,Profile,OcoReference,State,ErrorCode,SnapshotJson,SubmittedAt,UpdatedAt";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly string _connection = configuration.GetConnectionString("SecureOpsDb")
        ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for announcement source jobs.");

    /// <summary>Accepted job plus whether an identical submission key already existed.</summary>
    public sealed record SubmitResult(AnnouncementSourceJob Job, bool Duplicate);

    /// <summary>
    /// Records a Queued job, or returns the existing job for a repeated submission key. Two concurrent
    /// identical submissions resolve to one job because the unique submission index is authoritative.
    /// </summary>
    public async Task<SubmitResult> SubmitAsync(AnnouncementSourceJob job, string submissionKey, string correlation, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        JobRow? existing = await connection.QuerySingleOrDefaultAsync<JobRow>(Command($"""
            SELECT {Columns} FROM announcements.SourceJobs WITH (UPDLOCK, HOLDLOCK)
            WHERE OwnerId=@OwnerId AND DraftId=@DraftId AND SubmissionKey=@SubmissionKey;
            """, new { job.OwnerId, job.DraftId, SubmissionKey = submissionKey }, transaction, token));
        if (existing is not null)
        {
            await transaction.CommitAsync(token);
            return new(Map(existing), true);
        }
        await connection.ExecuteAsync(Command("""
            INSERT INTO announcements.SourceJobs(JobId,OwnerId,DraftId,Profile,OcoReference,SubmissionKey,State,SubmittedAt,UpdatedAt)
            VALUES (@JobId,@OwnerId,@DraftId,@Profile,@OcoReference,@SubmissionKey,@State,@SubmittedAt,@SubmittedAt);
            """, new { job.JobId, job.OwnerId, job.DraftId, job.Profile, job.OcoReference, SubmissionKey = submissionKey, job.State, job.SubmittedAt }, transaction, token));
        await AuditAsync(connection, transaction, job.OwnerId, "AnnouncementSourceJobSubmitted", correlation,
            new { job.JobId, job.DraftId, job.Profile, job.State }, token);
        await transaction.CommitAsync(token);
        return new(job, false);
    }

    /// <summary>Moves exactly one Queued job to Running; a redelivered or terminal job returns null.</summary>
    public async Task<AnnouncementSourceJob?> ClaimAsync(Guid jobId, DateTimeOffset now, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        JobRow? row = await connection.QuerySingleOrDefaultAsync<JobRow>(Command("""
            UPDATE announcements.SourceJobs SET State=N'Running', UpdatedAt=@now
            OUTPUT inserted.JobId,inserted.OwnerId,inserted.DraftId,inserted.Profile,inserted.OcoReference,
                   inserted.State,inserted.ErrorCode,inserted.SnapshotJson,inserted.SubmittedAt,inserted.UpdatedAt
            WHERE JobId=@jobId AND State=N'Queued';
            """, new { jobId, now }, null, token));
        return row is null ? null : Map(row);
    }

    /// <summary>Writes a terminal outcome exactly once; a late duplicate completion changes nothing.</summary>
    public async Task<bool> CompleteAsync(Guid jobId, string state, string? errorCode,
        AnnouncementSourceSnapshot? snapshot, string correlation, DateTimeOffset now, CancellationToken token)
    {
        string? snapshotJson = snapshot is null ? null : JsonSerializer.Serialize(snapshot, _json);
        if (snapshotJson is not null && System.Text.Encoding.Unicode.GetByteCount(snapshotJson) > 1_048_576)
        { (snapshotJson, state, errorCode) = (null, AnnouncementSourceJobStates.Failed, "AnnouncementSourceSnapshotTooLarge"); }
        await using SqlConnection connection = new(_connection);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        Guid? owner = await connection.QuerySingleOrDefaultAsync<Guid?>(Command("""
            UPDATE announcements.SourceJobs SET State=@state, ErrorCode=@errorCode, SnapshotJson=@snapshotJson, UpdatedAt=@now
            OUTPUT inserted.OwnerId WHERE JobId=@jobId AND State IN (N'Queued', N'Running');
            """, new { jobId, state, errorCode, snapshotJson, now }, transaction, token));
        if (owner is null)
        {
            await transaction.CommitAsync(token);
            return false;
        }
        await AuditAsync(connection, transaction, owner.Value, "AnnouncementSourceJobCompleted", correlation,
            new { JobId = jobId, State = state, ErrorCode = errorCode, DeviceCount = snapshot?.Completeness.DeviceCount }, token);
        await transaction.CommitAsync(token);
        return true;
    }

    /// <summary>Owner-scoped job read including any persisted snapshot.</summary>
    public async Task<AnnouncementSourceJob?> GetAsync(Guid jobId, Guid owner, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        JobRow? row = await connection.QuerySingleOrDefaultAsync<JobRow>(Command(
            $"SELECT {Columns} FROM announcements.SourceJobs WHERE JobId=@jobId AND OwnerId=@owner;",
            new { jobId, owner }, null, token));
        return row is null ? null : Map(row);
    }

    /// <summary>Most recently submitted owned job for one draft; used for status refresh only.</summary>
    public async Task<AnnouncementSourceJob?> LatestAsync(Guid draftId, Guid owner, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        JobRow? row = await connection.QuerySingleOrDefaultAsync<JobRow>(Command($"""
            SELECT TOP(1) {Columns} FROM announcements.SourceJobs WHERE OwnerId=@owner AND DraftId=@draftId
            ORDER BY SubmittedAt DESC, JobId;
            """, new { draftId, owner }, null, token));
        return row is null ? null : Map(row);
    }

    /// <summary>Operator overrides for one owned draft; an absent row is empty, never an error.</summary>
    public async Task<AnnouncementSourceOverrides> OverridesAsync(Guid draftId, Guid owner, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        OverrideRow? row = await connection.QuerySingleOrDefaultAsync<OverrideRow>(Command("""
            SELECT Version,OverridesJson,AppliedJobId,AppliedCapturedAt,UpdatedAt
            FROM announcements.SourceOverrides WHERE DraftId=@draftId AND OwnerId=@owner;
            """, new { draftId, owner }, null, token));
        if (row is null)
        { return AnnouncementSourceOverrides.Empty(draftId, owner); }
        OverrideDocument document = JsonSerializer.Deserialize<OverrideDocument>(row.OverridesJson, _json) ?? new(null, [], [], [], []);
        return new(draftId, owner, row.Version, document.Profile, document.ManualTo, document.ManualCc,
            document.RemovedTo, document.RemovedCc, row.AppliedJobId, row.AppliedCapturedAt, row.UpdatedAt);
    }

    /// <summary>
    /// Persists overrides under an optimistic version check. A stale caller receives false instead of
    /// overwriting a newer operator decision.
    /// </summary>
    public async Task<bool> SaveOverridesAsync(AnnouncementSourceOverrides overrides, long expectedVersion,
        DateTimeOffset now, CancellationToken token)
    {
        string json = JsonSerializer.Serialize(new OverrideDocument(overrides.Profile, overrides.ManualTo,
            overrides.ManualCc, overrides.RemovedTo, overrides.RemovedCc), _json);
        if (System.Text.Encoding.Unicode.GetByteCount(json) > 65536)
        { return false; }
        await using SqlConnection connection = new(_connection);
        int affected = await connection.ExecuteAsync(Command("""
            UPDATE announcements.SourceOverrides SET Version=@nextVersion, OverridesJson=@json,
                AppliedJobId=@AppliedJobId, AppliedCapturedAt=@AppliedCapturedAt, UpdatedAt=@now
            WHERE DraftId=@DraftId AND OwnerId=@OwnerId AND Version=@expectedVersion;
            -- Only a genuinely absent row is created; an existing newer row stays a conflict, not a throw.
            IF @@ROWCOUNT = 0 AND @expectedVersion = 0
                AND NOT EXISTS (SELECT 1 FROM announcements.SourceOverrides WHERE DraftId=@DraftId AND OwnerId=@OwnerId)
                INSERT INTO announcements.SourceOverrides(DraftId,OwnerId,Version,OverridesJson,AppliedJobId,AppliedCapturedAt,UpdatedAt)
                VALUES (@DraftId,@OwnerId,1,@json,@AppliedJobId,@AppliedCapturedAt,@now);
            """, new
        {
            overrides.DraftId,
            overrides.OwnerId,
            expectedVersion,
            nextVersion = expectedVersion + 1,
            json,
            overrides.AppliedJobId,
            overrides.AppliedCapturedAt,
            now
        }, null, token));
        return affected > 0;
    }

    /// <summary>Records a reviewed application against the owned draft; details carry no addresses.</summary>
    public async Task ApplyAuditAsync(Guid owner, Guid draftId, Guid jobId, long version, string[] fields,
        bool recipients, string correlation, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        await AuditAsync(connection, null, owner, "AnnouncementSourceApplied", correlation,
            new { DraftId = draftId, JobId = jobId, Version = version, FieldCount = fields.Length, Recipients = recipients }, token);
    }

    private static Task AuditAsync(SqlConnection connection, SqlTransaction? transaction, Guid owner,
        string action, string correlation, object details, CancellationToken token) =>
        connection.ExecuteAsync(Command("""
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(@now,@actor,@action,@correlation,@details);
            """, new
        {
            now = DateTimeOffset.UtcNow,
            actor = owner.ToString("D"),
            action,
            correlation,
            details = JsonSerializer.Serialize(details, _json)
        }, transaction, token));

    private static CommandDefinition Command(string sql, object parameters, SqlTransaction? transaction, CancellationToken token) =>
        new(sql, parameters, transaction, 15, cancellationToken: token);

    private static AnnouncementSourceJob Map(JobRow row) => new(row.JobId, row.OwnerId, row.DraftId, row.Profile,
        row.OcoReference, row.State, row.SubmittedAt, row.UpdatedAt, row.ErrorCode,
        row.SnapshotJson is null ? null : JsonSerializer.Deserialize<AnnouncementSourceSnapshot>(row.SnapshotJson, _json));

    private sealed record JobRow(Guid JobId, Guid OwnerId, Guid DraftId, string Profile, string OcoReference,
        string State, string? ErrorCode, string? SnapshotJson, DateTimeOffset SubmittedAt, DateTimeOffset UpdatedAt);
    private sealed record OverrideRow(long Version, string OverridesJson, Guid? AppliedJobId,
        DateTimeOffset? AppliedCapturedAt, DateTimeOffset UpdatedAt);
    private sealed record OverrideDocument(string? Profile, string[] ManualTo, string[] ManualCc,
        string[] RemovedTo, string[] RemovedCc);
}
