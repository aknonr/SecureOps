using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>SQL Server repository with transactional workflow acquisition and database idempotency.</summary>
public sealed partial class SqlOperationalRecordRepository : IOperationalRecordRepository
{
    private const int CommandTimeoutSeconds = 15;
    private readonly string _connectionString;

    /// <summary>Initializes the SQL repository.</summary>
    public SqlOperationalRecordRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for operational-record SQL persistence.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationalRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        IEnumerable<OperationalRecordRow> rows = await connection.QueryAsync<OperationalRecordRow>(Command(ReadSql, cancellationToken));
        return rows.Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async Task<OperationalRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        OperationalRecordRow? row = await connection.QuerySingleOrDefaultAsync<OperationalRecordRow>(Command($"{ReadSql} WHERE r.OperationalRecordId = @Id", new { Id = id }, cancellationToken));
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> UpsertImportedAsync(OperationalRecordSourceItem sourceItem, string correlationId, CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Id uniqueidentifier = (SELECT OperationalRecordId FROM ops.OperationalRecords WITH (UPDLOCK, HOLDLOCK) WHERE SourceRecordId = @SourceRecordId);
            DECLARE @Action nvarchar(10) = 'UPDATE';
            IF @Id IS NULL
            BEGIN
                SET @Id = NEWID();
                SET @Action = 'INSERT';
                INSERT INTO ops.OperationalRecords
                    (OperationalRecordId, SourceRecordId, OrCode, Title, Description, Requester, SourceCreatedAt,
                     EnvironmentName, ServerReference, ApplicationReference, SourceConcurrencyToken, LastSourceValidationAt,
                     Classification, JiraEligible, EligibilityReason, WorkflowState, CorrelationId, UpdatedAt)
                VALUES
                    (@Id, @SourceRecordId, @OrCode, @Title, @Description, @Requester, @CreatedAt,
                     @Environment, @ServerReference, @ApplicationReference, @SourceConcurrencyToken, SYSUTCDATETIME(),
                     'NeedsManualReview', 0, 'Classification pending.', 'Imported', @CorrelationId, SYSUTCDATETIME());
            END
            ELSE
                UPDATE ops.OperationalRecords SET OrCode = @OrCode, Title = @Title, Description = @Description,
                    Requester = @Requester, SourceCreatedAt = @CreatedAt, EnvironmentName = @Environment,
                    ServerReference = @ServerReference, ApplicationReference = @ApplicationReference,
                    SourceConcurrencyToken = @SourceConcurrencyToken, LastSourceValidationAt = SYSUTCDATETIME(),
                    CorrelationId = @CorrelationId, UpdatedAt = SYSUTCDATETIME()
                WHERE OperationalRecordId = @Id
                  AND WorkflowState NOT IN ('CreatingJira', 'JiraCreated', 'ClosingOperationalRecord', 'OperationalRecordCloseFailed', 'Completed');
            SELECT @Id AS Id, @Action AS MergeAction;
            """;

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        MergeResult merged = await connection.QuerySingleAsync<MergeResult>(Command(sql, new
        {
            sourceItem.SourceRecordId,
            sourceItem.OrCode,
            sourceItem.Title,
            sourceItem.Description,
            sourceItem.Requester,
            sourceItem.CreatedAt,
            sourceItem.Environment,
            sourceItem.ServerReference,
            sourceItem.ApplicationReference,
            SourceConcurrencyToken = OperationalRecordSourceConcurrency.Create(sourceItem),
            CorrelationId = correlationId
        }, cancellationToken, transaction));
        if (string.Equals(merged.MergeAction, "INSERT", StringComparison.OrdinalIgnoreCase))
        {
            await WriteHistoryAsync(connection, transaction, merged.Id, OperationalRecordWorkflowState.Imported, "system:source-import", correlationId, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(merged.Id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> SetClassificationAsync(Guid id, OperationalRecordClassificationResult classification, string correlationId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        OperationalRecordRow row = await GetForUpdateAsync(connection, transaction, id, cancellationToken)
            ?? throw new KeyNotFoundException("Operational record was not found.");
        OperationalRecord current = Map(row);
        if (CanApplyClassification(current))
        {
            OperationalRecordWorkflowState finalState = classification.JiraEligible
                ? OperationalRecordWorkflowState.Eligible
                : OperationalRecordWorkflowState.NeedsManualReview;
            const string update = """
                UPDATE ops.OperationalRecords SET Classification = @Classification, JiraEligible = @JiraEligible,
                    EligibilityReason = @EligibilityReason, WorkflowState = @WorkflowState,
                    CorrelationId = @CorrelationId, UpdatedAt = SYSUTCDATETIME()
                WHERE OperationalRecordId = @Id;
                """;
            await connection.ExecuteAsync(Command(update, new
            {
                Id = id,
                Classification = classification.Classification.ToString(),
                classification.JiraEligible,
                classification.EligibilityReason,
                WorkflowState = finalState.ToString(),
                CorrelationId = correlationId
            }, cancellationToken, transaction));
            await WriteHistoryAsync(connection, transaction, id, OperationalRecordWorkflowState.Classified, "system:classifier", correlationId, null, cancellationToken);
            await WriteHistoryAsync(connection, transaction, id, finalState, "system:classifier", correlationId, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<WorkflowClaimResult> TryClaimAsync(Guid id, string actor, TimeSpan leaseDuration, string correlationId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        OperationalRecordRow? row = await GetForUpdateAsync(connection, transaction, id, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WorkflowClaimResult(WorkflowAcquireDisposition.NotFound, null);
        }

        OperationalRecord current = Map(row);
        if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new WorkflowClaimResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
        }

        if (current.WorkflowState == OperationalRecordWorkflowState.CreatingJira)
        {
            await transaction.CommitAsync(cancellationToken);
            return new WorkflowClaimResult(WorkflowAcquireDisposition.InProgress, current);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (current.ClaimExpiresAt > now && !string.Equals(current.ClaimedBy, actor, StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);
            return new WorkflowClaimResult(WorkflowAcquireDisposition.AlreadyClaimed, current);
        }

        const string update = """
            UPDATE ops.OperationalRecords SET ClaimedBy = @Actor, ClaimedAt = SYSUTCDATETIME(),
                ClaimExpiresAt = @ClaimExpiresAt, CorrelationId = @CorrelationId, UpdatedAt = SYSUTCDATETIME()
            WHERE OperationalRecordId = @Id;
            """;
        await connection.ExecuteAsync(Command(update, new { Id = id, Actor = actor, ClaimExpiresAt = now.Add(leaseDuration), CorrelationId = correlationId }, cancellationToken, transaction));
        await transaction.CommitAsync(cancellationToken);
        return new WorkflowClaimResult(WorkflowAcquireDisposition.Acquired, await GetAsync(id, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<OperationalRecord?> ReleaseClaimAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ops.OperationalRecords SET ClaimedBy = NULL, ClaimedAt = NULL, ClaimExpiresAt = NULL,
                CorrelationId = @CorrelationId, UpdatedAt = SYSUTCDATETIME()
            WHERE OperationalRecordId = @Id AND ClaimedBy = @Actor;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.ExecuteAsync(Command(sql, new { Id = id, Actor = actor, CorrelationId = correlationId }, cancellationToken));
        return await GetAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordSourceValidationAsync(Guid id, DateTimeOffset validatedAt, string correlationId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ops.OperationalRecords SET LastSourceValidationAt = @ValidatedAt,
                CorrelationId = @CorrelationId, UpdatedAt = SYSUTCDATETIME()
            WHERE OperationalRecordId = @Id;
            """;
        await using SqlConnection connection = new(_connectionString);
        int count = await connection.ExecuteAsync(Command(sql, new { Id = id, ValidatedAt = validatedAt, CorrelationId = correlationId }, cancellationToken));
        if (count == 0)
        {
            throw new KeyNotFoundException("Operational record was not found.");
        }

        return (await GetAsync(id, cancellationToken))!;
    }

    /// <inheritdoc />
    public Task<WorkflowAcquireResult> MarkPreviewedAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken) =>
        AcquireAsync(id, actor, correlationId, cancellationToken, async (connection, transaction, current) =>
        {
            if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
            }

            if (!current.JiraEligible || current.WorkflowState is not (OperationalRecordWorkflowState.Eligible or OperationalRecordWorkflowState.Previewed or OperationalRecordWorkflowState.JiraCreateFailed))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            if (!await EnsureTransferAsync(connection, transaction, current.Id, mappingVersion, idempotencyKey, actor, cancellationToken))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            await UpdateStateAsync(connection, transaction, current.Id, OperationalRecordWorkflowState.Previewed, correlationId, null, cancellationToken);
            await WriteHistoryAsync(connection, transaction, current.Id, OperationalRecordWorkflowState.Previewed, actor, correlationId, null, cancellationToken);
            return new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, current with
            {
                WorkflowState = OperationalRecordWorkflowState.Previewed,
                MappingVersion = mappingVersion,
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId,
                LastErrorCode = null,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        });

    /// <inheritdoc />
    public Task<WorkflowAcquireResult> TryAcquireCreateAsync(Guid id, string mappingVersion, string idempotencyKey, string actor, string correlationId, CancellationToken cancellationToken, bool sourceCloseRequested = false) =>
        AcquireAsync(id, actor, correlationId, cancellationToken, async (connection, transaction, current) =>
        {
            if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
            }

            if (!string.IsNullOrWhiteSpace(current.JiraIssueKey))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.JiraAlreadyCreated, current);
            }

            if (current.ReconciliationRequired)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.ReconciliationRequired, current);
            }

            if (!ClaimOwnedBy(current, actor))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyClaimed, current);
            }

            if (current.WorkflowState is OperationalRecordWorkflowState.CreateRequested or OperationalRecordWorkflowState.CreatingJira or OperationalRecordWorkflowState.ClosingOperationalRecord)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            if (current.WorkflowState is not (OperationalRecordWorkflowState.Previewed or OperationalRecordWorkflowState.JiraCreateFailed))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            if (!await EnsureTransferAsync(connection, transaction, current.Id, mappingVersion, idempotencyKey, actor, cancellationToken))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            await UpdateStateAsync(connection, transaction, current.Id, OperationalRecordWorkflowState.CreateRequested, correlationId, null, cancellationToken);
            await connection.ExecuteAsync(Command("""
                UPDATE ops.JiraTransfers SET SourceCloseRequested = @SourceCloseRequested
                WHERE OperationalRecordId = @Id AND MappingVersion = @MappingVersion;
                """, new { current.Id, MappingVersion = mappingVersion, SourceCloseRequested = sourceCloseRequested }, cancellationToken, transaction));
            await WriteHistoryAsync(connection, transaction, current.Id, OperationalRecordWorkflowState.CreateRequested, actor, correlationId, null, cancellationToken);
            await UpdateStateAsync(connection, transaction, current.Id, OperationalRecordWorkflowState.CreatingJira, correlationId, null, cancellationToken);
            await WriteHistoryAsync(connection, transaction, current.Id, OperationalRecordWorkflowState.CreatingJira, actor, correlationId, null, cancellationToken);
            return new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, current with
            {
                WorkflowState = OperationalRecordWorkflowState.CreatingJira,
                SourceCloseRequested = sourceCloseRequested,
                MappingVersion = mappingVersion,
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId,
                LastErrorCode = null,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        });

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordJiraCreatedAsync(Guid id, string issueKey, string actor, string correlationId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        OperationalRecord current = Map(await GetForUpdateAsync(connection, transaction, id, cancellationToken)
            ?? throw new KeyNotFoundException("Operational record was not found."));
        if (string.IsNullOrWhiteSpace(current.JiraIssueKey))
        {
            if (current.WorkflowState != OperationalRecordWorkflowState.CreatingJira)
            {
                throw new InvalidOperationException("Jira creation cannot be persisted from the current state.");
            }

            const string updateTransfer = """
                UPDATE ops.JiraTransfers SET JiraIssueKey = @IssueKey, ReconciliationRequired = 0, UpdatedAt = SYSUTCDATETIME()
                WHERE OperationalRecordId = @Id AND MappingVersion = @MappingVersion;
                """;
            await connection.ExecuteAsync(Command(updateTransfer, new { Id = id, IssueKey = issueKey, current.MappingVersion }, cancellationToken, transaction));
            await UpdateStateAsync(connection, transaction, id, OperationalRecordWorkflowState.JiraCreated, correlationId, null, cancellationToken);
            await WriteHistoryAsync(connection, transaction, id, OperationalRecordWorkflowState.JiraCreated, actor, correlationId, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(id, cancellationToken))!;
    }

    /// <inheritdoc />
    public Task<WorkflowAcquireResult> TryAcquireCloseAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken) =>
        AcquireAsync(id, actor, correlationId, cancellationToken, async (connection, transaction, current) =>
        {
            if (current.WorkflowState == OperationalRecordWorkflowState.Completed)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyCompleted, current);
            }

            if (string.IsNullOrWhiteSpace(current.JiraIssueKey) || !current.SourceCloseRequested)
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            if (!ClaimOwnedBy(current, actor))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.AlreadyClaimed, current);
            }

            if (current.WorkflowState == OperationalRecordWorkflowState.ClosingOperationalRecord)
            {
                return ClaimOwnedBy(current, actor)
                    ? new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, current)
                    : new WorkflowAcquireResult(WorkflowAcquireDisposition.Conflict, current);
            }

            if (current.WorkflowState is not (OperationalRecordWorkflowState.JiraCreated or OperationalRecordWorkflowState.OperationalRecordCloseFailed))
            {
                return new WorkflowAcquireResult(WorkflowAcquireDisposition.InvalidState, current);
            }

            await UpdateStateAsync(connection, transaction, id, OperationalRecordWorkflowState.ClosingOperationalRecord, correlationId, null, cancellationToken);
            await WriteHistoryAsync(connection, transaction, id, OperationalRecordWorkflowState.ClosingOperationalRecord, actor, correlationId, null, cancellationToken);
            return new WorkflowAcquireResult(WorkflowAcquireDisposition.Acquired, current with
            {
                WorkflowState = OperationalRecordWorkflowState.ClosingOperationalRecord,
                CorrelationId = correlationId,
                LastErrorCode = null,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        });

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordCompletedAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        OperationalRecord current = Map(await GetForUpdateAsync(connection, transaction, id, cancellationToken)
            ?? throw new KeyNotFoundException("Operational record was not found."));
        if (current.WorkflowState != OperationalRecordWorkflowState.ClosingOperationalRecord)
        {
            throw new InvalidOperationException("Workflow completion cannot be persisted from the current state.");
        }

        await UpdateStateAsync(connection, transaction, id, OperationalRecordWorkflowState.Completed, correlationId, null, cancellationToken);
        await WriteHistoryAsync(connection, transaction, id, OperationalRecordWorkflowState.Completed, actor, correlationId, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<OperationalRecord> RecordFailureAsync(Guid id, WorkflowFailureStage stage, string errorCode, bool reconciliationRequired, string actor, string correlationId, CancellationToken cancellationToken)
    {
        OperationalRecordWorkflowState state = stage == WorkflowFailureStage.JiraCreate
            ? OperationalRecordWorkflowState.JiraCreateFailed
            : OperationalRecordWorkflowState.OperationalRecordCloseFailed;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        _ = await GetForUpdateAsync(connection, transaction, id, cancellationToken)
            ?? throw new KeyNotFoundException("Operational record was not found.");
        await UpdateStateAsync(connection, transaction, id, state, correlationId, errorCode, cancellationToken);
        if (stage == WorkflowFailureStage.JiraCreate)
        {
            const string transferUpdate = """
                UPDATE ops.JiraTransfers SET ReconciliationRequired = @ReconciliationRequired,
                    LastErrorCode = @ErrorCode, UpdatedAt = SYSUTCDATETIME()
                WHERE OperationalRecordId = @Id;
                """;
            await connection.ExecuteAsync(Command(transferUpdate, new { Id = id, ReconciliationRequired = reconciliationRequired, ErrorCode = errorCode }, cancellationToken, transaction));
        }

        await WriteHistoryAsync(connection, transaction, id, state, actor, correlationId, errorCode, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<OperationalRecord?> RecordRetryRequestedAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken)
    {
        const string update = """
            UPDATE ops.OperationalRecords SET RetryCount = RetryCount + 1, CorrelationId = @CorrelationId, UpdatedAt = SYSUTCDATETIME()
            WHERE OperationalRecordId = @Id;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        int count = await connection.ExecuteAsync(Command(update, new { Id = id, CorrelationId = correlationId }, cancellationToken, transaction));
        if (count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        OperationalRecordRow row = (await GetForUpdateAsync(connection, transaction, id, cancellationToken))!;
        await WriteHistoryAsync(connection, transaction, id, ParseState(row.WorkflowState), actor, correlationId, row.LastErrorCode, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<WorkflowAcquireResult> AcquireAsync(
        Guid id,
        string actor,
        string correlationId,
        CancellationToken cancellationToken,
        Func<SqlConnection, SqlTransaction, OperationalRecord, Task<WorkflowAcquireResult>> transition)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        OperationalRecordRow? row = await GetForUpdateAsync(connection, transaction, id, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WorkflowAcquireResult(WorkflowAcquireDisposition.NotFound, null);
        }

        WorkflowAcquireResult result = await transition(connection, transaction, Map(row));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<bool> EnsureTransferAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid id,
        string mappingVersion,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM ops.JiraTransfers WITH (UPDLOCK, HOLDLOCK) WHERE OperationalRecordId = @Id)
            BEGIN
                INSERT INTO ops.JiraTransfers
                    (JiraTransferId, OperationalRecordId, MappingVersion, IdempotencyKey, CreatedByActor, CreatedAt, UpdatedAt)
                VALUES (NEWID(), @Id, @MappingVersion, @IdempotencyKey, @Actor, SYSUTCDATETIME(), SYSUTCDATETIME());
                SELECT CAST(1 AS bit);
            END
            ELSE
                SELECT CAST(CASE WHEN EXISTS
                    (SELECT 1 FROM ops.JiraTransfers WHERE OperationalRecordId = @Id
                        AND MappingVersion = @MappingVersion AND IdempotencyKey = @IdempotencyKey)
                    THEN 1 ELSE 0 END AS bit);
            """;
        return await connection.QuerySingleAsync<bool>(Command(sql, new { Id = id, MappingVersion = mappingVersion, IdempotencyKey = idempotencyKey, Actor = actor }, cancellationToken, transaction));
    }

    private static Task<OperationalRecordRow?> GetForUpdateAsync(SqlConnection connection, SqlTransaction transaction, Guid id, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<OperationalRecordRow>(Command($"{ReadSql.Replace("FROM ops.OperationalRecords r", "FROM ops.OperationalRecords r WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)} WHERE r.OperationalRecordId = @Id", new { Id = id }, cancellationToken, transaction));

    private static Task<int> UpdateStateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid id,
        OperationalRecordWorkflowState state,
        string correlationId,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ops.OperationalRecords SET WorkflowState = @State, LastErrorCode = @ErrorCode,
                CorrelationId = @CorrelationId, UpdatedAt = SYSUTCDATETIME()
            WHERE OperationalRecordId = @Id;
            """;
        return connection.ExecuteAsync(Command(sql, new { Id = id, State = state.ToString(), ErrorCode = errorCode, CorrelationId = correlationId }, cancellationToken, transaction));
    }

    private static Task<int> WriteHistoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid id,
        OperationalRecordWorkflowState state,
        string actor,
        string correlationId,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ops.OperationalRecordWorkflowHistory
                (OperationalRecordId, WorkflowState, Actor, CorrelationId, ErrorCode, OccurredAt, SourceCloseRequested)
            VALUES (@Id, @State, @Actor, @CorrelationId, @ErrorCode, SYSUTCDATETIME(),
                COALESCE((SELECT TOP (1) SourceCloseRequested FROM ops.JiraTransfers
                    WHERE OperationalRecordId = @Id ORDER BY CreatedAt DESC), 0));
            """;
        return connection.ExecuteAsync(Command(sql, new { Id = id, State = state.ToString(), Actor = actor, CorrelationId = correlationId, ErrorCode = errorCode }, cancellationToken, transaction));
    }

    private static CommandDefinition Command(string sql, CancellationToken cancellationToken) =>
        new(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken);

    private static CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken, IDbTransaction? transaction = null) =>
        new(sql, parameters, transaction, CommandTimeoutSeconds, cancellationToken: cancellationToken);

    private static OperationalRecord Map(OperationalRecordRow row) => new()
    {
        Id = row.Id,
        SourceRecordId = row.SourceRecordId,
        OrCode = row.OrCode,
        Title = row.Title,
        Description = row.Description,
        Requester = row.Requester,
        CreatedAt = row.CreatedAt,
        Environment = row.Environment,
        ServerReference = row.ServerReference,
        ApplicationReference = row.ApplicationReference,
        Classification = Enum.Parse<OperationalRecordClassification>(row.Classification, true),
        JiraEligible = row.JiraEligible,
        EligibilityReason = row.EligibilityReason,
        WorkflowState = ParseState(row.WorkflowState),
        JiraIssueKey = row.JiraIssueKey,
        LastErrorCode = row.LastErrorCode,
        CorrelationId = row.CorrelationId,
        MappingVersion = row.MappingVersion,
        SourceCloseRequested = row.SourceCloseRequested,
        IdempotencyKey = row.IdempotencyKey,
        ReconciliationRequired = row.ReconciliationRequired,
        RetryCount = row.RetryCount,
        UpdatedAt = row.UpdatedAt,
        SourceConcurrencyToken = row.SourceConcurrencyToken,
        LastSourceValidationAt = row.LastSourceValidationAt,
        ClaimedBy = row.ClaimedBy,
        ClaimedAt = row.ClaimedAt,
        ClaimExpiresAt = row.ClaimExpiresAt,
        Version = row.Version,
        SdmEvaluation = row.SdmEvaluationJson is null ? null
            : System.Text.Json.JsonSerializer.Deserialize<SdmEvaluationSnapshot>(row.SdmEvaluationJson)
    };

    private static OperationalRecordWorkflowState ParseState(string value) => Enum.Parse<OperationalRecordWorkflowState>(value, true);

    private static bool CanApplyClassification(OperationalRecord record) =>
        string.IsNullOrWhiteSpace(record.JiraIssueKey)
        && !record.ReconciliationRequired
        && record.WorkflowState is OperationalRecordWorkflowState.Imported
            or OperationalRecordWorkflowState.Classified
            or OperationalRecordWorkflowState.NeedsManualReview
            or OperationalRecordWorkflowState.Eligible;

    private const string ReadSql = """
        SELECT r.OperationalRecordId AS Id, r.SourceRecordId, r.OrCode, r.Title, r.Description, r.Requester,
            r.SourceCreatedAt AS CreatedAt, r.EnvironmentName AS Environment, r.ServerReference,
            r.ApplicationReference, r.Classification, r.JiraEligible, r.EligibilityReason, r.WorkflowState,
            r.LastErrorCode, r.CorrelationId, r.RetryCount, r.UpdatedAt, r.SourceConcurrencyToken,
            r.LastSourceValidationAt, r.ClaimedBy, r.ClaimedAt, r.ClaimExpiresAt, CONVERT(bigint, r.RowVersion) AS Version,
            transfer.MappingVersion, transfer.IdempotencyKey, transfer.JiraIssueKey, transfer.ReconciliationRequired,
            COALESCE(transfer.SourceCloseRequested, 0) AS SourceCloseRequested,
            r.SdmEvaluationJson
        FROM ops.OperationalRecords r
        LEFT JOIN ops.JiraTransfers transfer ON transfer.OperationalRecordId = r.OperationalRecordId
        """;

    private sealed class OperationalRecordRow
    {
        public Guid Id { get; init; }
        public string SourceRecordId { get; init; } = string.Empty;
        public string OrCode { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? Requester { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }
        public string? Environment { get; init; }
        public string? ServerReference { get; init; }
        public string? ApplicationReference { get; init; }
        public string Classification { get; init; } = string.Empty;
        public bool JiraEligible { get; init; }
        public string EligibilityReason { get; init; } = string.Empty;
        public string WorkflowState { get; init; } = string.Empty;
        public string? JiraIssueKey { get; init; }
        public string? LastErrorCode { get; init; }
        public string? CorrelationId { get; init; }
        public string? MappingVersion { get; init; }
        public bool SourceCloseRequested { get; init; }
        public string? IdempotencyKey { get; init; }
        public bool ReconciliationRequired { get; init; }
        public int RetryCount { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public string SourceConcurrencyToken { get; init; } = string.Empty;
        public DateTimeOffset? LastSourceValidationAt { get; init; }
        public string? ClaimedBy { get; init; }
        public DateTimeOffset? ClaimedAt { get; init; }
        public DateTimeOffset? ClaimExpiresAt { get; init; }
        public long Version { get; init; }
        public string? SdmEvaluationJson { get; init; }
    }

    private sealed record MergeResult(Guid Id, string MergeAction);

    private static bool ClaimOwnedBy(OperationalRecord record, string actor) =>
        string.Equals(record.ClaimedBy, actor, StringComparison.OrdinalIgnoreCase)
        && record.ClaimExpiresAt > DateTimeOffset.UtcNow;
}
