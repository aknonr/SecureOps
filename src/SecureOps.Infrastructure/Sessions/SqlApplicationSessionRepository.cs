using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.Sessions;

/// <summary>SQL Server application-session repository.</summary>
public sealed class SqlApplicationSessionRepository : IApplicationSessionRepository
{
    private const int _commandTimeoutSeconds = 15;
    private readonly string _connectionString;

    /// <summary>Initializes SQL session persistence.</summary>
    public SqlApplicationSessionRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for SQL session persistence.");
    }

    /// <inheritdoc />
    public async Task InsertAsync(ApplicationSession session, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO security.ApplicationSessions
                (SessionId, UserId, StartedAtUtc, LastSeenAtUtc, AbsoluteExpiresAtUtc, EndedAtUtc, EndReason, AuthenticationMethod, AccessVersion)
            VALUES
                (@SessionId, @UserId, @StartedAtUtc, @LastSeenAtUtc, @AbsoluteExpiresAtUtc, NULL, NULL, @AuthenticationMethod, @AccessVersion);
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.ExecuteAsync(Command(sql, session, null, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ApplicationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        const string sql = $"{_readSql} WHERE SessionId = @SessionId;";
        await using SqlConnection connection = new(_connectionString);
        SessionRow? row = await connection.QuerySingleOrDefaultAsync<SessionRow>(Command(sql, new { SessionId = sessionId }, null, cancellationToken));
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<bool> TouchAsync(Guid sessionId, DateTimeOffset lastSeenAtUtc, DateTimeOffset persistBeforeUtc, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE security.ApplicationSessions
            SET LastSeenAtUtc = @LastSeenAtUtc
            WHERE SessionId = @SessionId
              AND EndedAtUtc IS NULL
              AND LastSeenAtUtc <= @PersistBeforeUtc;
            """;
        await using SqlConnection connection = new(_connectionString);
        int affected = await connection.ExecuteAsync(Command(sql, new { SessionId = sessionId, LastSeenAtUtc = lastSeenAtUtc, PersistBeforeUtc = persistBeforeUtc }, null, cancellationToken));
        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> EndAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE security.ApplicationSessions
            SET EndedAtUtc = @EndedAtUtc, EndReason = @EndReason
            WHERE SessionId = @SessionId AND EndedAtUtc IS NULL;
            """;
        await using SqlConnection connection = new(_connectionString);
        int affected = await connection.ExecuteAsync(Command(sql, new { SessionId = sessionId, EndedAtUtc = endedAtUtc, EndReason = reason.ToString() }, null, cancellationToken));
        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> EndWithAuditAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE security.ApplicationSessions
            SET EndedAtUtc = @EndedAtUtc, EndReason = @EndReason
            WHERE SessionId = @SessionId AND EndedAtUtc IS NULL;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int affected = await connection.ExecuteAsync(Command(sql, new { SessionId = sessionId, EndedAtUtc = endedAtUtc, EndReason = reason.ToString() }, transaction, cancellationToken));
        await AppendAuditAsync(connection, transaction, auditEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, Func<ApplicationSession, AuditEvent> auditFactory, CancellationToken cancellationToken)
    {
        const string select = $"{_readSql} WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId AND EndedAtUtc IS NULL;";
        const string update = "UPDATE security.ApplicationSessions SET EndedAtUtc = @EndedAtUtc, EndReason = @EndReason WHERE UserId = @UserId AND EndedAtUtc IS NULL;";
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        ApplicationSession[] affected = (await connection.QueryAsync<SessionRow>(Command(select, new { UserId = userId }, transaction, cancellationToken))).Select(Map).ToArray();
        if (affected.Length > 0)
        {
            await connection.ExecuteAsync(Command(update, new { UserId = userId, EndedAtUtc = endedAtUtc, EndReason = reason.ToString() }, transaction, cancellationToken));
        }

        foreach (ApplicationSession session in affected)
        {
            await AppendAuditAsync(connection, transaction, auditFactory(session), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return affected;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationSession>> EndExpiredAsync(
        DateTimeOffset nowUtc,
        DateTimeOffset idleCutoffUtc,
        int maximumCount,
        Func<ApplicationSession, AuditEvent> auditFactory,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Ended TABLE (
                SessionId uniqueidentifier, UserId uniqueidentifier,
                StartedAtUtc datetimeoffset(7), LastSeenAtUtc datetimeoffset(7),
                AbsoluteExpiresAtUtc datetimeoffset(7), EndedAtUtc datetimeoffset(7),
                EndReason nvarchar(32), AuthenticationMethod nvarchar(64), AccessVersion bigint);
            UPDATE TOP (@MaximumCount) security.ApplicationSessions
            SET EndedAtUtc = @NowUtc,
                EndReason = CASE
                    WHEN AbsoluteExpiresAtUtc <= @NowUtc THEN 'AbsoluteTimeout'
                    ELSE 'IdleTimeout'
                END
            OUTPUT inserted.SessionId, inserted.UserId, inserted.StartedAtUtc, inserted.LastSeenAtUtc,
                inserted.AbsoluteExpiresAtUtc, inserted.EndedAtUtc, inserted.EndReason,
                inserted.AuthenticationMethod, inserted.AccessVersion INTO @Ended
            WHERE EndedAtUtc IS NULL
              AND (AbsoluteExpiresAtUtc <= @NowUtc OR LastSeenAtUtc <= @IdleCutoffUtc);
            SELECT * FROM @Ended;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        ApplicationSession[] ended = (await connection.QueryAsync<SessionRow>(
            Command(sql, new { NowUtc = nowUtc, IdleCutoffUtc = idleCutoffUtc, MaximumCount = maximumCount }, transaction, cancellationToken))).Select(Map).ToArray();
        foreach (ApplicationSession session in ended)
        {
            await AppendAuditAsync(connection, transaction, auditFactory(session), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ended;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationSession>> ListActiveAsync(DateTimeOffset absoluteCutoffUtc, DateTimeOffset idleCutoffUtc, int skip, int take, CancellationToken cancellationToken)
    {
        const string sql = $"""
            {_readSql}
            WHERE EndedAtUtc IS NULL
              AND AbsoluteExpiresAtUtc > @AbsoluteCutoffUtc
              AND LastSeenAtUtc > @IdleCutoffUtc
            ORDER BY LastSeenAtUtc DESC, SessionId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        await using SqlConnection connection = new(_connectionString);
        IEnumerable<SessionRow> rows = await connection.QueryAsync<SessionRow>(Command(sql, new { AbsoluteCutoffUtc = absoluteCutoffUtc, IdleCutoffUtc = idleCutoffUtc, Skip = skip, Take = take }, null, cancellationToken));
        return rows.Select(Map).ToArray();
    }

    private static ApplicationSession Map(SessionRow row) => new(
        row.SessionId,
        row.UserId,
        row.StartedAtUtc,
        row.LastSeenAtUtc,
        row.AbsoluteExpiresAtUtc,
        row.EndedAtUtc,
        string.IsNullOrWhiteSpace(row.EndReason) ? null : Enum.Parse<SessionEndReason>(row.EndReason, true),
        row.AuthenticationMethod,
        row.AccessVersion);

    private static async Task AppendAuditAsync(SqlConnection connection, SqlTransaction transaction, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        // Queueing cannot enlist in the session transaction. Append to the existing audit table directly.
        const string sql = """
            INSERT INTO audit.AuditLog (OccurredAt, Actor, Action, AlertId, ServerName, CorrelationId, DetailsJson, SourceIp)
            VALUES (@OccurredAt, @Actor, @Action, @AlertId, @ServerName, @CorrelationId, @DetailsJson, @SourceIp);
            """;
        try
        {
            await connection.ExecuteAsync(Command(sql, new
            {
                auditEvent.OccurredAt,
                auditEvent.Actor,
                auditEvent.Action,
                auditEvent.AlertId,
                auditEvent.ServerName,
                auditEvent.CorrelationId,
                DetailsJson = auditEvent.Details is null ? null : System.Text.Json.JsonSerializer.Serialize(auditEvent.Details, AuditJson.SerializerOptions),
                auditEvent.SourceIp
            }, transaction, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AuditWriteUnavailableException("Application-session transactional audit failed.", exception);
        }
    }

    private static CommandDefinition Command(string sql, object? parameters, IDbTransaction? transaction, CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, _commandTimeoutSeconds, cancellationToken: cancellationToken);

    private const string _readSql = """
        SELECT SessionId, UserId, StartedAtUtc, LastSeenAtUtc, AbsoluteExpiresAtUtc,
            EndedAtUtc, EndReason, AuthenticationMethod, AccessVersion
        FROM security.ApplicationSessions
        """;

    private sealed class SessionRow
    {
        public Guid SessionId { get; init; }
        public Guid UserId { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset LastSeenAtUtc { get; init; }
        public DateTimeOffset AbsoluteExpiresAtUtc { get; init; }
        public DateTimeOffset? EndedAtUtc { get; init; }
        public string? EndReason { get; init; }
        public string AuthenticationMethod { get; init; } = string.Empty;
        public long AccessVersion { get; init; }
    }
}
