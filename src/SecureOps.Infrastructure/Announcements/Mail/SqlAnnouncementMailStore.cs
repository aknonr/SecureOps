using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Announcements;
using SecureOps.Domain.Commands;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Commands;

namespace SecureOps.Infrastructure.Announcements.Mail;

/// <summary>Mail-specific durable intent with fenced execution. A dispatched lease never becomes sendable again.</summary>
public sealed class SqlAnnouncementMailStore(IConfiguration configuration)
{
    private string Connection => configuration.GetConnectionString("SecureOpsDb") ?? throw new InvalidOperationException("SQL mail storage required.");

    /// <summary>Commits frozen bytes and required evidence before any enqueue or network submission.</summary>
    public async Task<(AnnouncementMailCommand? Command, string? Error)> CreateAsync(AnnouncementMailIntent intent, byte[] message, CancellationToken token)
    {
        if (message.Length is < 1 or > 3_000_000 || intent.Kind is not ("Send" or "SelfTest") || intent.Initiator.Kind != "Human"
            || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(message)) != intent.MessageHash)
        { return (null, "AnnouncementMailInvalid"); }
        await using var sql = new SqlConnection(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await SqlAccessRepository.LockAdministrationAsync(sql, tx, token);
        await MailLockAsync(sql, tx, intent.DraftId, token);
        Row? existing = await sql.QuerySingleOrDefaultAsync<Row>(Command("""
            SELECT TOP(1) * FROM announcements.MailCommands WHERE CommandId=@CommandId
                OR (DraftId=@DraftId AND ((Kind='Send' AND @Kind='Send') OR State IN ('Queued','Dispatching','Unknown','Partial')))
            ORDER BY CASE WHEN CommandId=@CommandId THEN 0 ELSE 1 END;
            """, intent, tx, token));
        if (existing is not null)
        {
            AnnouncementMailCommand prior = Map(existing);
            if (prior.Intent.Initiator.Id != intent.Initiator.Id)
            { return (null, "AnnouncementNotFound"); }
            return prior.Intent.CommandId == intent.CommandId && prior.Intent.PreviewToken == intent.PreviewToken
                ? (prior, null) : (prior, "AnnouncementMailAlreadyRequested");
        }
        if (!await AuthorizedAsync(sql, tx, intent, token))
        { return (null, "AccessDenied"); }
        long latest = await sql.ExecuteScalarAsync<long>(Command("SELECT MAX(Version) FROM announcements.DraftRevisions WITH(HOLDLOCK) WHERE Id=@DraftId AND OwnerId=@OwnerId",
            new { intent.DraftId, OwnerId = intent.Initiator.Id }, tx, token));
        if (latest != intent.DraftVersion)
        { return (null, "AnnouncementConflict"); }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await sql.ExecuteAsync(Command("""
            INSERT INTO announcements.MailCommands(CommandId,PreparationId,DraftId,OwnerId,Kind,Version,State,CreatedAt,UpdatedAt,IntentJson,MessageBytes)
            VALUES(@CommandId,@PreparationId,@DraftId,@OwnerId,@Kind,1,'Queued',@now,@now,@json,@message);
            """, new { intent.CommandId, intent.PreparationId, intent.DraftId, OwnerId = intent.Initiator.Id, intent.Kind, now, json = JsonSerializer.Serialize(intent), message }, tx, token));
        await EventAsync(sql, tx, intent, "Requested", null, token);
        await tx.CommitAsync(token);
        return (new(intent, 1, "Queued", now, now, [], [], null), null);
    }

    /// <summary>Rechecks the original initiating actor and current authority inside the claim transaction.</summary>
    public async Task<AnnouncementMailExecution?> ClaimAsync(Guid id, string configurationFingerprint, OperationExecutor executor, CancellationToken token)
    {
        await using var sql = new SqlConnection(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await SqlAccessRepository.LockAdministrationAsync(sql, tx, token);
        Row? row = await sql.QuerySingleOrDefaultAsync<Row>(Command("SELECT * FROM announcements.MailCommands WITH(UPDLOCK,HOLDLOCK) WHERE CommandId=@id", new { id }, tx, token));
        if (row is null || row.State != "Queued")
        { return null; }
        AnnouncementMailCommand command = Map(row);
        long latest = await sql.ExecuteScalarAsync<long>(Command("SELECT MAX(Version) FROM announcements.DraftRevisions WHERE Id=@DraftId AND OwnerId=@OwnerId",
            new { command.Intent.DraftId, OwnerId = command.Intent.Initiator.Id }, tx, token));
        bool permitted = await AuthorizedAsync(sql, tx, command.Intent, token) && latest == command.Intent.DraftVersion
            && configurationFingerprint == command.Intent.ConfigurationFingerprint;
        var execution = Guid.NewGuid();
        string state = permitted ? "Dispatching" : "Denied";
        await sql.ExecuteAsync(Command("""
            UPDATE announcements.MailCommands SET State=@state,Version=Version+1,ExecutionToken=@execution,
                UpdatedAt=SYSUTCDATETIME(),LeaseExpiresAt=DATEADD(MINUTE,5,SYSUTCDATETIME()) WHERE CommandId=@id;
            """, new { state, execution, id }, tx, token));
        await EventAsync(sql, tx, command.Intent, permitted ? "Dispatched" : "Denied", executor, token);
        await tx.CommitAsync(token);
        return permitted ? new(command with { State = state, Version = command.Version + 1 }, execution, row.MessageBytes) : null;
    }

    /// <summary>Completion token fences duplicate Worker invocation and stale completion after recovery.</summary>
    public async Task<bool> CompleteAsync(AnnouncementMailExecution execution, AnnouncementTransportOutcome outcome, OperationExecutor executor, CancellationToken token)
    {
        if (outcome.State is not ("Accepted" or "Partial" or "Unknown" or "Failed" or "Denied"))
        { throw new ArgumentException("Invalid mail outcome.", nameof(outcome)); }
        await using var sql = new SqlConnection(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        int updated = await sql.ExecuteAsync(Command("""
            UPDATE announcements.MailCommands SET State=@State,Version=Version+1,UpdatedAt=SYSUTCDATETIME(),OutcomeJson=@json
            WHERE CommandId=@id AND ExecutionToken=@ExecutionToken AND State='Dispatching';
            """, new { outcome.State, json = JsonSerializer.Serialize(outcome), id = execution.Command.Intent.CommandId, execution.ExecutionToken }, tx, token));
        if (updated != 1)
        { return false; }
        await EventAsync(sql, tx, execution.Command.Intent, outcome.State, executor, token);
        await tx.CommitAsync(token);
        return true;
    }

    /// <summary>Only queued commands may be re-enqueued; expired dispatched commands become uncertain.</summary>
    public async Task<IReadOnlyList<Guid>> RecoverAsync(CancellationToken token)
    {
        await using var sql = new SqlConnection(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        IEnumerable<Row> expired = await sql.QueryAsync<Row>(Command("SELECT TOP(50) * FROM announcements.MailCommands WITH(UPDLOCK,HOLDLOCK) WHERE State='Dispatching' AND LeaseExpiresAt<SYSUTCDATETIME() ORDER BY UpdatedAt,CommandId", null, tx, token));
        foreach (Row row in expired)
        {
            await sql.ExecuteAsync(Command("UPDATE announcements.MailCommands SET State='Unknown',Version=Version+1,UpdatedAt=SYSUTCDATETIME() WHERE CommandId=@CommandId", row, tx, token));
            await EventAsync(sql, tx, Map(row).Intent, "Unknown", new("SecureOps.Worker", "Recovery"), token);
        }
        Guid[] queued = [.. await sql.QueryAsync<Guid>(Command("SELECT TOP(50) CommandId FROM announcements.MailCommands WHERE State='Queued' ORDER BY CreatedAt,CommandId", null, tx, token))];
        await tx.CommitAsync(token);
        return queued;
    }

    /// <summary>Bounded owner-only command history, excluding message bytes.</summary>
    public async Task<IReadOnlyList<AnnouncementMailCommand>> ListAsync(Guid owner, Guid draft, CancellationToken token, string? correlation = null)
    {
        await using var sql = new SqlConnection(Connection);
        await sql.ExecuteAsync(Command("""
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(SYSUTCDATETIME(),@actor,'AnnouncementMailHistoryRead',@correlation,@details);
            """, new { actor = owner.ToString("D"), correlation = correlation ?? "mail-history", details = JsonSerializer.Serialize(new { draft }) }, null, token));
        return (await sql.QueryAsync<Row>(Command("SELECT TOP(100) CommandId,IntentJson,Version,State,CreatedAt,UpdatedAt,OutcomeJson FROM announcements.MailCommands WHERE OwnerId=@owner AND DraftId=@draft ORDER BY CreatedAt DESC,CommandId", new { owner, draft }, null, token))).Select(Map).ToArray();
    }

    /// <summary>Replay lookup remains owner-scoped even when current draft/profile changed.</summary>
    public async Task<AnnouncementMailCommand?> GetAsync(Guid id, Guid owner, CancellationToken token)
    {
        await using var sql = new SqlConnection(Connection);
        Row? row = await sql.QuerySingleOrDefaultAsync<Row>(Command("SELECT CommandId,IntentJson,Version,State,CreatedAt,UpdatedAt,OutcomeJson FROM announcements.MailCommands WHERE CommandId=@id AND OwnerId=@owner", new { id, owner }, null, token));
        return row is null ? null : Map(row);
    }

    private static async Task<bool> AuthorizedAsync(SqlConnection sql, SqlTransaction tx, AnnouncementMailIntent intent, CancellationToken token) =>
        await sql.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(*) FROM security.Users u WHERE UserId=@owner AND AccessStatus='Approved' AND AccessVersion=@AccessVersion
                AND CONVERT(varbinary(max),Mail)=CONVERT(varbinary(max),@Sender)
                AND EXISTS(SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                    WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value=@capability)
                AND EXISTS(SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                    WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value='Announcements.Drafts');
            """, new { owner = intent.Initiator.Id, intent.AccessVersion, intent.Sender, capability = intent.Kind == "SelfTest" ? "Announcements.SelfTest" : "Announcements.Send" }, tx, token)) == 1;

    private static Task EventAsync(SqlConnection sql, SqlTransaction tx, AnnouncementMailIntent intent, string outcome, OperationExecutor? executor, CancellationToken token) =>
        SqlOperationEvidence.AppendAsync(sql, tx, new(Guid.NewGuid(), intent.CommandId, "Announcement", intent.DraftId.ToString("D"),
            "Announcement" + intent.Kind, intent.DraftVersion, DateTimeOffset.UtcNow, outcome, intent.Initiator, executor, null,
            intent.CorrelationId, intent.PreparationId.ToString("D"), intent.MessageId), token);

    private static Task MailLockAsync(SqlConnection sql, SqlTransaction tx, Guid draft, CancellationToken token) => sql.ExecuteAsync(Command("""
        DECLARE @result int;
        EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @result<0 THROW 51204,'Mail intent lock unavailable.',1;
        """, new { resource = "SecureOps.AnnouncementMail:" + draft.ToString("D") }, tx, token));

    private static AnnouncementMailCommand Map(Row row)
    {
        AnnouncementTransportOutcome? outcome = row.OutcomeJson is null ? null : JsonSerializer.Deserialize<AnnouncementTransportOutcome>(row.OutcomeJson);
        return new(JsonSerializer.Deserialize<AnnouncementMailIntent>(row.IntentJson)!, row.Version, row.State, row.CreatedAt, row.UpdatedAt,
            outcome?.Accepted.ToArray() ?? [], outcome?.Rejected.ToArray() ?? [], row.State is "Failed" or "Denied" or "Unknown" ? "AnnouncementMail" + row.State : null);
    }
    private static CommandDefinition Command(string sql, object? arguments, SqlTransaction? transaction, CancellationToken token) => new(sql, arguments, transaction, commandTimeout: 15, cancellationToken: token);
    private sealed class Row
    {
        public Guid CommandId { get; init; }
        public string IntentJson { get; init; } = "";
        public long Version { get; init; }
        public string State { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public string? OutcomeJson { get; init; }
        public byte[] MessageBytes { get; init; } = [];
    }
}
