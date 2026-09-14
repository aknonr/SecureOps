using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.Commands;

/// <summary>SQL Server command execution store for retry and restart durability.</summary>
public sealed class SqlCommandIdempotencyStore : ICommandIdempotencyStore
{
    private const int _commandTimeoutSeconds = 15;
    private readonly string _connectionString;

    /// <summary>Initializes the SQL store.</summary>
    public SqlCommandIdempotencyStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for SQL command idempotency.");
    }

    /// <inheritdoc />
    public async Task<CommandBeginResult> TryBeginAsync(string commandName, string targetId, string idempotencyKey, string actor, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        const string select = """
            SELECT Actor, Status, LeaseExpiresAt, ErrorCode, ExecutionToken
            FROM ops.CommandExecutions WITH (UPDLOCK, HOLDLOCK)
            WHERE CommandName = @CommandName AND TargetId = @TargetId AND IdempotencyKey = @IdempotencyKey;
            """;
        ExecutionRow? row = await connection.QuerySingleOrDefaultAsync<ExecutionRow>(Command(select, new { CommandName = commandName, TargetId = targetId, IdempotencyKey = idempotencyKey }, transaction, cancellationToken));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var executionToken = Guid.NewGuid();
        if (row is null)
        {
            const string insert = """
                INSERT INTO ops.CommandExecutions
                    (CommandExecutionId, CommandName, TargetId, IdempotencyKey, Actor, Status, ExecutionToken, StartedAt, LeaseExpiresAt, UpdatedAt)
                VALUES (NEWID(), @CommandName, @TargetId, @IdempotencyKey, @Actor, 'InProgress', @ExecutionToken, SYSUTCDATETIME(), @LeaseExpiresAt, SYSUTCDATETIME());
                """;
            await connection.ExecuteAsync(Command(insert, new { CommandName = commandName, TargetId = targetId, IdempotencyKey = idempotencyKey, Actor = actor, ExecutionToken = executionToken, LeaseExpiresAt = now.Add(leaseDuration) }, transaction, cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return new CommandBeginResult(CommandBeginDisposition.Acquired, null, executionToken);
        }

        if (!string.Equals(row.Actor, actor, StringComparison.OrdinalIgnoreCase) && row.LeaseExpiresAt > now)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CommandBeginResult(CommandBeginDisposition.ActorConflict, null);
        }

        if (string.Equals(row.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);
            return string.Equals(row.Actor, actor, StringComparison.OrdinalIgnoreCase)
                ? new CommandBeginResult(CommandBeginDisposition.Completed, null)
                : new CommandBeginResult(CommandBeginDisposition.ActorConflict, null);
        }

        if (string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);
            return string.Equals(row.Actor, actor, StringComparison.OrdinalIgnoreCase)
                ? new CommandBeginResult(CommandBeginDisposition.Failed, row.ErrorCode)
                : new CommandBeginResult(CommandBeginDisposition.ActorConflict, null);
        }

        if (row.LeaseExpiresAt > now)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CommandBeginResult(CommandBeginDisposition.InProgress, null);
        }

        const string reacquire = """
            UPDATE ops.CommandExecutions SET Actor = @Actor, Status = 'InProgress', ErrorCode = NULL,
                ExecutionToken = @ExecutionToken, StartedAt = SYSUTCDATETIME(), LeaseExpiresAt = @LeaseExpiresAt, UpdatedAt = SYSUTCDATETIME()
            WHERE CommandName = @CommandName AND TargetId = @TargetId AND IdempotencyKey = @IdempotencyKey;
            """;
        await connection.ExecuteAsync(Command(reacquire, new { CommandName = commandName, TargetId = targetId, IdempotencyKey = idempotencyKey, Actor = actor, ExecutionToken = executionToken, LeaseExpiresAt = now.Add(leaseDuration) }, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new CommandBeginResult(CommandBeginDisposition.Acquired, null, executionToken);
    }

    /// <inheritdoc />
    public Task CompleteAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, CancellationToken cancellationToken) =>
        UpdateAsync(commandName, targetId, idempotencyKey, executionToken, "Completed", null, cancellationToken);

    /// <inheritdoc />
    public Task FailAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, string errorCode, CancellationToken cancellationToken) =>
        UpdateAsync(commandName, targetId, idempotencyKey, executionToken, "Failed", errorCode, cancellationToken);

    private async Task UpdateAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, string status, string? errorCode, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ops.CommandExecutions SET Status = @Status, ErrorCode = @ErrorCode,
                CompletedAt = CASE WHEN @Status = 'Completed' THEN SYSUTCDATETIME() ELSE CompletedAt END,
                UpdatedAt = SYSUTCDATETIME()
            WHERE CommandName = @CommandName AND TargetId = @TargetId AND IdempotencyKey = @IdempotencyKey
              AND ExecutionToken = @ExecutionToken AND Status = 'InProgress';
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.ExecuteAsync(Command(sql, new { CommandName = commandName, TargetId = targetId, IdempotencyKey = idempotencyKey, ExecutionToken = executionToken, Status = status, ErrorCode = errorCode }, null, cancellationToken));
    }

    private static CommandDefinition Command(string sql, object parameters, IDbTransaction? transaction, CancellationToken cancellationToken) => new(sql, parameters, transaction, _commandTimeoutSeconds, cancellationToken: cancellationToken);
    private sealed record ExecutionRow(string Actor, string Status, DateTimeOffset LeaseExpiresAt, string? ErrorCode, Guid ExecutionToken);
}
