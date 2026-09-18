using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>Atomic durable intent/outbox and fenced step journal on the existing application SQL store.</summary>
public sealed class SqlInUseExecutionStore(IConfiguration configuration) : IInUseExecutionStore
{
    /// <summary>False for memory-only test compositions; execution requires SQL.</summary>
    public bool Configured => !string.IsNullOrWhiteSpace(configuration.GetConnectionString("SecureOpsDb"));
    private string Connection => configuration.GetConnectionString("SecureOpsDb") ?? throw new InvalidOperationException("SQL In Use execution storage required.");
    /// <summary>Versioned draft fingerprint; assignment/progress versions cannot change the reviewed artifact.</summary>
    public static string ReviewHash(InUseDraft? draft) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(draft)));

    /// <summary>Creates one logical operation and its artifact/audit in the same transaction, before enqueue.</summary>
    public async Task<InUseResult<InUseExecution>> CreateAsync(InUseExecutionIntent intent, byte[] artifact, CancellationToken token)
    {
        if (intent.OperationId == Guid.Empty || artifact.Length is < 1 or > 10_000_000
            || Convert.ToHexString(SHA256.HashData(artifact)) != intent.ReportSha256)
        { return InUseResult<InUseExecution>.Fail("InUseInvalid"); }
        await using SqlConnection sql = new(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await SqlAccessRepository.LockAdministrationAsync(sql, tx, token);
        if (!await AuthorizedAsync(sql, tx, intent.InitiatorId, token))
        { return InUseResult<InUseExecution>.Fail("AccessDenied"); }
        Row? existing = await sql.QuerySingleOrDefaultAsync<Row>(Command("""
            SELECT TOP(1) * FROM ops.InUseExecutions WITH(UPDLOCK,HOLDLOCK) WHERE OperationId=@OperationId
                OR (RecordId=@RecordId AND (ReportVersion=@ReportVersion OR Active=1))
            ORDER BY CASE WHEN OperationId=@OperationId THEN 0 ELSE 1 END;
            """, intent, tx, token));
        if (existing is not null)
        {
            InUseExecutionIntent old = Read<InUseExecutionIntent>(existing.IntentJson);
            return old.RecordId == intent.RecordId && old.ReportVersion == intent.ReportVersion && old.ReportSha256 == intent.ReportSha256
                && old.InitiatorId == intent.InitiatorId ? new(await MapAsync(sql, tx, existing, token))
                : new(null, "InUseConflict", "Bu kayıt için önceki işlem var. Yeni ek yüklemeden kayıtlı sonucu inceleyin.");
        }
        InUseRecord? record = await RecordAsync(sql, tx, intent.RecordId, token);
        if (record?.Version != intent.ReportVersion || !Current(record, intent))
        { return InUseResult<InUseExecution>.Fail("InUseConflict"); }
        await sql.ExecuteAsync(Command("""
            INSERT INTO ops.InUseExecutions(OperationId,RecordId,ReportVersion,InitiatorId,State,Step,Revision,Active,CreatedAt,UpdatedAt,IntentJson,Artifact)
            VALUES(@OperationId,@RecordId,@ReportVersion,@InitiatorId,'Queued',0,1,1,@RequestedAt,@RequestedAt,@json,@artifact);
            """, new
        {
            intent.OperationId,
            intent.RecordId,
            intent.ReportVersion,
            intent.InitiatorId,
            intent.RequestedAt,
            json = JsonSerializer.Serialize(intent),
            artifact
        }, tx, token));
        await EventAsync(sql, tx, intent, 1, new("Intent", "Queued", null, "ReviewedArtifactPersisted", DateTimeOffset.UtcNow, "SecureOps.Api"), token);
        await tx.CommitAsync(token);
        return new(new(intent.OperationId, intent.RecordId, intent.ReportVersion, intent.ReportSha256, "Queued", 0, 1,
            intent.InitiatorId, intent.InitiatorLabel, intent.RequestedAt, []));
    }

    /// <summary>Reads the latest operation for a record; the application service authorizes access first.</summary>
    public async Task<InUseExecution?> LatestAsync(Guid recordId, CancellationToken token)
    {
        await using SqlConnection sql = new(Connection);
        await sql.OpenAsync(token);
        Row? row = await sql.QuerySingleOrDefaultAsync<Row>(Command("SELECT TOP(1) * FROM ops.InUseExecutions WHERE RecordId=@recordId ORDER BY CreatedAt DESC,OperationId;", new { recordId }, null, token));
        return row is null ? null : await MapAsync(sql, null, row, token);
    }

    /// <inheritdoc />
    public async Task<InUseExecutionLease?> ClaimAsync(Guid id, string fingerprint, string executor, CancellationToken token)
    {
        await using SqlConnection sql = new(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await SqlAccessRepository.LockAdministrationAsync(sql, tx, token);
        Row? row = await sql.QuerySingleOrDefaultAsync<Row>(Command("SELECT * FROM ops.InUseExecutions WITH(UPDLOCK,HOLDLOCK) WHERE OperationId=@id;", new { id }, tx, token));
        if (row is null || row.State != "Queued")
        { return null; }
        InUseExecutionIntent intent = Read<InUseExecutionIntent>(row.IntentJson);
        InUseRecord? record = await RecordAsync(sql, tx, intent.RecordId, token);
        string? blocked = fingerprint != intent.ConfigurationFingerprint ? "ConfigurationChanged"
            : !await AuthorizedAsync(sql, tx, intent.InitiatorId, token) ? "AccessRevoked"
            : record is null || !Current(record, intent) ? "SourceOrReviewChanged" : null;
        if (blocked is not null)
        {
            await UpdateAsync(sql, tx, row, intent, "Blocked", row.Step,
                new(InUseExecutionWorker.Steps[row.Step], "Blocked", null, blocked, DateTimeOffset.UtcNow, executor), token);
            await tx.CommitAsync(token);
            return null;
        }
        var lease = Guid.NewGuid();
        long revision = row.Revision + 1;
        await sql.ExecuteAsync(Command("""
            UPDATE ops.InUseExecutions SET State='Running',Revision=@revision,LeaseToken=@lease,
                LeaseUntil=DATEADD(second,180,SYSDATETIMEOFFSET()),UpdatedAt=SYSDATETIMEOFFSET() WHERE OperationId=@id;
            """, new { revision, lease, id }, tx, token));
        IReadOnlyList<InUseStepEvidence> evidence = await EvidenceAsync(sql, tx, id, token);
        await EventAsync(sql, tx, intent, revision, new(InUseExecutionWorker.Steps[row.Step], "Started", null,
            "LeaseOwned", DateTimeOffset.UtcNow, executor), token);
        await tx.CommitAsync(token);
        return new(intent, row.Artifact, lease, revision, row.Step, evidence);
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(InUseExecutionLease lease, InUseRemoteResult result, string executor, CancellationToken token)
    {
        if (result.Outcome is not ("Verified" or "Acknowledged" or "Rejected" or "Unknown" or "Unconfirmed")
            || result.Code.Length > 100 || result.RemoteId?.Length > 100)
        { return false; }
        await using SqlConnection sql = new(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        Row? row = await sql.QuerySingleOrDefaultAsync<Row>(Command("SELECT * FROM ops.InUseExecutions WITH(UPDLOCK,HOLDLOCK) WHERE OperationId=@id;", new { id = lease.Intent.OperationId }, tx, token));
        if (row is null || row.State != "Running" || row.LeaseToken != lease.Token || row.Revision != lease.Revision
            || row.Step != lease.Step || row.LeaseUntil <= DateTimeOffset.UtcNow)
        { return false; }
        bool verifiedStep = row.Step is 0 or 4 or 6;
        string state = result.Outcome == "Unknown" ? "Unknown" : result.Outcome == "Rejected" ? "Failed"
            : result.Outcome == "Unconfirmed" || verifiedStep && result.Outcome != "Verified" ? "Unconfirmed"
            : row.Step == 6 ? "Completed" : "Queued";
        await UpdateAsync(sql, tx, row, lease.Intent, state, state is "Queued" or "Completed" ? row.Step + 1 : row.Step,
            new(InUseExecutionWorker.Steps[row.Step], result.Outcome, result.RemoteId, result.Code, DateTimeOffset.UtcNow, executor), token);
        await tx.CommitAsync(token);
        return true;
    }

    /// <summary>Queued rows are the transactional outbox. Expired mutations become terminal Unknown, never replayed.</summary>
    public async Task<IReadOnlyList<Guid>> RecoverAsync(CancellationToken token)
    {
        await using SqlConnection sql = new(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        Row[] expired = (await sql.QueryAsync<Row>(Command("SELECT TOP(100) * FROM ops.InUseExecutions WITH(UPDLOCK,HOLDLOCK) WHERE State='Running' AND LeaseUntil<SYSDATETIMEOFFSET() ORDER BY CreatedAt;", null, tx, token))).ToArray();
        foreach (Row row in expired)
        {
            bool safeRead = row.Step is 0 or 4 or 6;
            await UpdateAsync(sql, tx, row, Read<InUseExecutionIntent>(row.IntentJson), safeRead ? "Queued" : "Unknown", row.Step,
                new(InUseExecutionWorker.Steps[row.Step], safeRead ? "ReadRetry" : "Unknown", null,
                    "WorkerLeaseExpired", DateTimeOffset.UtcNow, "SecureOps.Worker:Recovery"), token);
        }
        Guid[] queued = (await sql.QueryAsync<Guid>(Command("SELECT TOP(100) OperationId FROM ops.InUseExecutions WHERE State='Queued' ORDER BY CreatedAt;", null, tx, token))).ToArray();
        await tx.CommitAsync(token);
        return queued;
    }

    private static bool Current(InUseRecord record, InUseExecutionIntent intent) => record.SourceVersion == intent.SourceVersion
        && record.SourceHash == intent.SourceHash && ReviewHash(record.Draft) == intent.ReviewHash && InUseProgress.Ready(record);

    private static Task<int> UpdateAsync(SqlConnection sql, SqlTransaction tx, Row row, InUseExecutionIntent intent,
        string state, int step, InUseStepEvidence evidence, CancellationToken token) => UpdateWithEventAsync(sql, tx, row, intent, state, step, evidence, token);

    private static async Task<int> UpdateWithEventAsync(SqlConnection sql, SqlTransaction tx, Row row, InUseExecutionIntent intent,
        string state, int step, InUseStepEvidence evidence, CancellationToken token)
    {
        int count = await sql.ExecuteAsync(Command("""
            UPDATE ops.InUseExecutions SET State=@state,Step=@step,Revision=@revision,Active=@active,
                LeaseToken=NULL,LeaseUntil=NULL,UpdatedAt=SYSDATETIMEOFFSET() WHERE OperationId=@id;
            """, new { state, step, revision = row.Revision + 1, active = state != "Completed" && (row.Step > 0 || state is "Queued" or "Unknown"), id = row.OperationId }, tx, token));
        await EventAsync(sql, tx, intent, row.Revision + 1, evidence, token);
        return count;
    }

    private static async Task<bool> AuthorizedAsync(SqlConnection sql, SqlTransaction tx, Guid actor, CancellationToken token) =>
        await sql.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(DISTINCT c.value) FROM security.Users u JOIN security.RoleAssignments a ON a.UserId=u.UserId
            JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
            WHERE u.UserId=@actor AND u.AccessStatus='Approved' AND a.RevokedAt IS NULL
                AND c.value IN ('InUse.View','InUse.Review','InUse.Complete');
            """, new { actor }, tx, token)) == 3;
    private static async Task<InUseRecord?> RecordAsync(SqlConnection sql, SqlTransaction tx, Guid id, CancellationToken token)
    {
        string? json = await sql.QuerySingleOrDefaultAsync<string>(Command("SELECT RecordJson FROM ops.InUseRecords WITH(HOLDLOCK) WHERE Id=@id;", new { id }, tx, token));
        return json is null ? null : Read<InUseRecord>(json);
    }
    private static async Task EventAsync(SqlConnection sql, SqlTransaction tx, InUseExecutionIntent intent, long revision, InUseStepEvidence evidence, CancellationToken token)
    {
        await sql.ExecuteAsync(Command("""
            INSERT INTO ops.InUseExecutionEvents(OperationId,Revision,OccurredAt,EvidenceJson) VALUES(@id,@revision,@at,@json);
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson) VALUES(@at,@actor,'InUseExecutionStep',@correlation,@audit);
            """, new
        {
            id = intent.OperationId,
            revision,
            at = evidence.At,
            json = JsonSerializer.Serialize(evidence),
            actor = intent.InitiatorId.ToString("D"),
            correlation = intent.OperationId.ToString("D"),
            audit = JsonSerializer.Serialize(new { intent.OperationId, intent.RecordId, intent.ReportVersion, intent.ReportSha256, evidence })
        }, tx, token));
    }
    private static async Task<IReadOnlyList<InUseStepEvidence>> EvidenceAsync(SqlConnection sql, SqlTransaction? tx, Guid id, CancellationToken token) =>
        (await sql.QueryAsync<string>(Command("SELECT EvidenceJson FROM ops.InUseExecutionEvents WHERE OperationId=@id ORDER BY Revision;", new { id }, tx, token))).Select(Read<InUseStepEvidence>).ToArray();
    private static async Task<InUseExecution> MapAsync(SqlConnection sql, SqlTransaction? tx, Row row, CancellationToken token)
    {
        InUseExecutionIntent i = Read<InUseExecutionIntent>(row.IntentJson);
        return new(i.OperationId, i.RecordId, i.ReportVersion, i.ReportSha256, row.State, row.Step, row.Revision,
            i.InitiatorId, i.InitiatorLabel, i.RequestedAt, await EvidenceAsync(sql, tx, i.OperationId, token));
    }
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Invalid In Use execution state.");
    private static CommandDefinition Command(string sql, object? values, SqlTransaction? tx, CancellationToken token) => new(sql, values, tx, commandTimeout: 15, cancellationToken: token);
    private sealed class Row
    {
        public Guid OperationId { get; set; }
        public string State { get; set; } = "";
        public int Step { get; set; }
        public long Revision { get; set; }
        public Guid? LeaseToken { get; set; }
        public DateTimeOffset? LeaseUntil { get; set; }
        public string IntentJson { get; set; } = "";
        public byte[] Artifact { get; set; } = [];
    }
}
