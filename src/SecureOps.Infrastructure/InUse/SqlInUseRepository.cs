using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Local SQL state with exact-version writes and same-transaction audit.</summary>
public sealed partial class SqlInUseRepository(IConfiguration configuration) : IInUseRepository
{
    // Keep SQL provenance checks identical to .NET IsNullOrWhiteSpace, including tabs and Unicode spaces.
    private static readonly string _nonWhitespacePattern = "%[^" + new string(Enumerable.Range(0, char.MaxValue + 1)
        .Select(i => (char)i).Where(char.IsWhiteSpace).ToArray()) + "]%";
    private const string _activityProjection = """
        FROM ops.InUseRecords r OUTER APPLY (
            SELECT MAX(v.OccurredAt) AS ActivityVerifiedAt FROM ops.InUseExecutions e
            JOIN ops.InUseExecutionEvents v ON v.OperationId=e.OperationId
            WHERE e.RecordId=r.Id AND JSON_VALUE(e.IntentJson,'$.VerificationMode')='WasasActivityManual'
                AND JSON_VALUE(v.EvidenceJson,'$.Step')='Bpm' AND JSON_VALUE(v.EvidenceJson,'$.Outcome')='Verified'
        ) activity
        CROSS APPLY (SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM ops.InUseExecutions e WHERE e.RecordId=r.Id AND e.Active=1
        ) THEN 1 ELSE 0 END AS bit) AS HasActiveExecution) execution
        """;
    private const string _listProjection = """

        CROSS APPLY (SELECT JSON_VALUE(RecordJson,'$.Source.Creation.Value') AS Value,
            PATINDEX(@NonWhitespacePattern,COALESCE(JSON_VALUE(RecordJson,'$.Source.Creation.Source'),N'') COLLATE Latin1_General_100_BIN2) AS Provenance) creation
        CROSS APPLY (SELECT CASE WHEN creation.Provenance>0
            AND LEFT(creation.Value,19) COLLATE Latin1_General_100_BIN2 LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]'
            AND ((LEN(creation.Value) IN (20,28) AND RIGHT(creation.Value,1) COLLATE Latin1_General_100_BIN2='Z')
                OR (LEN(creation.Value) IN (25,33) AND SUBSTRING(creation.Value,LEN(creation.Value)-5,1) IN ('+','-')
                    AND SUBSTRING(creation.Value,LEN(creation.Value)-2,1)=':'))
            AND (LEN(creation.Value) IN (20,25) OR (SUBSTRING(creation.Value,20,1)='.'
                AND SUBSTRING(creation.Value,21,7) COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'))
            AND TRY_CONVERT(datetimeoffset(7),creation.Value,127)<=@Now
            THEN TRY_CONVERT(datetimeoffset(7),creation.Value,127) END AS CreatedAt) requestDate
        CROSS APPLY (SELECT CASE
            WHEN activity.ActivityVerifiedAt IS NOT NULL OR (JSON_VALUE(RecordJson,'$.Source.WasasActivity.Value')+N'#' COLLATE Latin1_General_100_BIN2=N'Completed#'
                AND PATINDEX(@NonWhitespacePattern,COALESCE(JSON_VALUE(RecordJson,'$.Source.WasasActivity.Source'),N'') COLLATE Latin1_General_100_BIN2)>0) THEN 'Completed'
            WHEN JSON_VALUE(RecordJson,'$.Source.Lifecycle.Value')+N'#' COLLATE Latin1_General_100_BIN2=N'Closed#'
                AND PATINDEX(@NonWhitespacePattern,COALESCE(JSON_VALUE(RecordJson,'$.Source.Lifecycle.Source'),N'') COLLATE Latin1_General_100_BIN2)>0 THEN 'OrClosed'
            WHEN COALESCE(JSON_VALUE(RecordJson,'$.Discarded'),'false')<>'true'
                AND COALESCE(JSON_VALUE(RecordJson,'$.SourceObservationMissing'),'false')<>'true'
                AND execution.HasActiveExecution=0 AND JSON_VALUE(RecordJson,'$.Source.WasasActivity.Value')+N'#' COLLATE Latin1_General_100_BIN2=N'Pending#'
                AND PATINDEX(@NonWhitespacePattern,COALESCE(JSON_VALUE(RecordJson,'$.Source.WasasActivity.Source'),N'') COLLATE Latin1_General_100_BIN2)>0 THEN 'Pending'
            ELSE 'VerificationPending' END AS ActivityStatus) workflow
        """;
    private sealed record QueryRow(string RecordJson, DateTimeOffset? ActivityVerifiedAt, bool HasActiveExecution);
    private static InUseRecord Project(QueryRow row) => Read<InUseRecord>(row.RecordJson) with
    { ActivityVerifiedAt = row.ActivityVerifiedAt, HasActiveExecution = row.HasActiveExecution };
    private readonly string _connectionString = configuration.GetConnectionString("SecureOpsDb")
        ?? throw new InvalidOperationException("In Use persistence requires SecureOpsDb.");

    /// <inheritdoc />
    public async Task<InUseOverview> OverviewAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        InUseRefreshState state = await ReadStateAsync(connection, transaction, cancellationToken);
        InUseRecord[] records = (await connection.QueryAsync<string>(Command("SELECT RecordJson FROM ops.InUseRecords;", null, cancellationToken, transaction)))
            .Select(Read<InUseRecord>).ToArray();
        InUseOverview result = InUseProgress.Summarize(records, state, DateTimeOffset.UtcNow);
        await AuditAsync(connection, transaction, audit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public Task<IAsyncDisposable?> TryAcquireRefreshAsync(CancellationToken cancellationToken) =>
        InUseRefreshLock.TryAcquireAsync(_connectionString, cancellationToken);

    /// <inheritdoc />
    public async Task<InUsePage> QueryAsync(InUseQuery query, Guid actorId, CancellationToken cancellationToken)
    {
        const string where = _activityProjection + _listProjection + """
             WHERE
             (@Search IS NULL OR CHARINDEX(@Search, Code COLLATE Latin1_General_100_CI_AS) > 0
              OR CHARINDEX(@Search, Title COLLATE Turkish_100_CI_AS) > 0
              OR EXISTS (SELECT 1 FROM OPENJSON(RecordJson, '$.Source.Servers') WITH (
                  Hostname nvarchar(4000) '$.Fields.HOSTNAME.Value',
                  Reporter nvarchar(4000) '$.RelatedRequestReporter.Display',
                  Account nvarchar(4000) '$.RelatedRequestReporter.UserReference',
                  ReporterState nvarchar(64) '$.RelatedRequestReporter.State',
                  DisplayState nvarchar(64) '$.RelatedRequestReporter.DisplayState',
                  ReferenceState nvarchar(64) '$.RelatedRequestReporter.ReferenceState') AS server
                  WHERE CHARINDEX(@Search, server.Hostname COLLATE Latin1_General_100_CI_AS) > 0
                  OR (server.ReporterState IN ('ExactMatch', 'Stale') AND (
                      (server.DisplayState = 'Returned' AND CHARINDEX(@Search, server.Reporter COLLATE Turkish_100_CI_AS) > 0)
                      OR (server.ReferenceState = 'Returned' AND CHARINDEX(@Search, server.Account COLLATE Latin1_General_100_CI_AS) > 0)))))
             AND ((@Status='Discarded' AND JSON_VALUE(RecordJson,'$.Discarded')='true')
              OR (COALESCE(@Status,'')<>'Discarded' AND COALESCE(JSON_VALUE(RecordJson,'$.Discarded'),'false')='false'
                  AND (@Status IS NULL OR ReviewStatus = @Status)))
             AND (@View = 'all' OR (@View = 'mine' AND AssigneeId = @ActorId) OR (@View = 'unassigned' AND AssigneeId IS NULL)
              OR (@View = 'tracking' AND workflow.ActivityStatus IN ('Completed','OrClosed'))
              OR (@View = 'review' AND workflow.ActivityStatus NOT IN ('Completed','OrClosed'))
              OR (@View = 'pending' AND workflow.ActivityStatus='Pending')
              OR (@View = 'verification' AND workflow.ActivityStatus='VerificationPending'))
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // State is always locked first, matching mutation lock order.
        InUseRefreshState state = await ReadStateAsync(connection, transaction, cancellationToken);
        string direction = query.Sort == "newest" ? "DESC" : "ASC";
        string dateOrder = query.Sort == "code" ? "" : "CASE WHEN requestDate.CreatedAt IS NULL THEN 1 ELSE 0 END, requestDate.CreatedAt " + direction + ", ";
        using SqlMapper.GridReader rows = await connection.QueryMultipleAsync(Command("SELECT COUNT(*) " + where + "; SELECT RecordJson, activity.ActivityVerifiedAt, execution.HasActiveExecution " + where
            + " ORDER BY " + dateOrder + "Code COLLATE Latin1_General_100_BIN2, CONVERT(char(36),r.Id) COLLATE Latin1_General_100_BIN2 OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", new
            {
                Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
                query.Status,
                query.View,
                Now = DateTimeOffset.UtcNow,
                NonWhitespacePattern = _nonWhitespacePattern,
                ActorId = actorId,
                Offset = (query.Page - 1) * query.PageSize,
                query.PageSize
            }, cancellationToken, transaction));
        int count = await rows.ReadSingleAsync<int>();
        InUseRecord[] records = [.. (await rows.ReadAsync<QueryRow>()).Select(Project)];
        await transaction.CommitAsync(cancellationToken);
        return new(records, count, query.Page, query.PageSize, state);
    }

    /// <inheritdoc />
    public async Task<InUseRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        QueryRow? row = await connection.QuerySingleOrDefaultAsync<QueryRow>(Command(
            "SELECT RecordJson, activity.ActivityVerifiedAt, execution.HasActiveExecution " + _activityProjection + " WHERE r.Id = @Id;", new { Id = id }, cancellationToken));
        return row is null ? null : Project(row);
    }

    /// <inheritdoc />
    public async Task<InUseRefreshState> StateAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        return await ReadStateAsync(connection, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(long expectedVersion, InUseBatch? batch, string? error, AuditEvent audit, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        InUseRefreshState state = await ReadStateAsync(connection, transaction, cancellationToken);
        if (state.Version != expectedVersion)
        { return false; }
        // Retention is local database evidence, not a source inventory query.
        InUseRecord[] retained = (await connection.QueryAsync<string>(Command(
            "SELECT RecordJson FROM ops.InUseRecords WITH (UPDLOCK, HOLDLOCK);", null, cancellationToken, transaction)))
            .Select(Read<InUseRecord>).Where(r => !(batch?.Records.Any(s => s.Id == r.Source.Id) ?? false)).ToArray();
        foreach (InUseRecord old in retained)
        {
            InUseRecord next = InUseState.RetainUnobserved(old);
            if (next.Version != old.Version)
            { await PersistAsync(connection, transaction, next, false, cancellationToken); }
        }
        foreach (InUseSource source in batch?.Records ?? [])
        {
            string? json = await connection.QuerySingleOrDefaultAsync<string>(Command(
                "SELECT RecordJson FROM ops.InUseRecords WITH (UPDLOCK, HOLDLOCK) WHERE SourceId = @Id;",
                new { source.Id }, cancellationToken, transaction));
            InUseRecord next = InUseState.Merge(json is null ? null : Read<InUseRecord>(json), source, audit.OccurredAt);
            await PersistAsync(connection, transaction, next, json is null, cancellationToken);
        }
        InUseRefreshState nextState = InUseState.Refresh(state, batch, error, audit.OccurredAt);
        await connection.ExecuteAsync(Command("UPDATE ops.InUseRefresh SET Version = @Version, StateJson = @Json WHERE Id = 1;",
            new { nextState.Version, Json = JsonSerializer.Serialize(nextState) }, cancellationToken, transaction));
        await AuditAsync(connection, transaction, audit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> SaveAsync(InUseRecord next, long expectedVersion, AuditEvent audit, CancellationToken cancellationToken) =>
        WriteAsync(next.Id, expectedVersion, next, audit, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExportAsync(Guid id, long expectedVersion, AuditEvent audit, CancellationToken cancellationToken) =>
        WriteAsync(id, expectedVersion, null, audit, cancellationToken);

    private async Task<bool> WriteAsync(Guid id, long expectedVersion, InUseRecord? next, AuditEvent audit, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (audit.Operation is { } operation)
        {
            await Access.SqlAccessRepository.LockAdministrationAsync(connection, transaction, cancellationToken);
            string capability = operation.Action is "InUseAssigned" or "InUseReporterSuggestionRead" ? "InUse.Assign" : "InUse.Review";
            const string authority = """
                SELECT COUNT(*) FROM security.Users u WHERE u.UserId=@userId AND u.AccessStatus='Approved' AND EXISTS(
                    SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                    WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value=@capability);
                """;
            foreach (string required in new[] { "InUse.View", capability })
            {
                if (await connection.ExecuteScalarAsync<int>(Command(authority, new { userId = operation.Initiator.Id, capability = required }, cancellationToken, transaction)) != 1)
                { return false; }
            }
            if (operation.Action == "InUseAssigned" && next?.AssigneeId is { } assignee)
            {
                foreach (string assigneeCapability in new[] { "InUse.View", "InUse.Review" })
                {
                    if (await connection.ExecuteScalarAsync<int>(Command(authority, new { userId = assignee, capability = assigneeCapability }, cancellationToken, transaction)) != 1)
                    { return false; }
                }
            }
        }
        await ReadStateAsync(connection, transaction, cancellationToken);
        // Execution creation/claim takes the same administration lock first. Never hide a running or uncertain remote effect.
        if (audit.Operation?.Action == "InUseDraftLifecycle" && await connection.ExecuteScalarAsync<int>(Command(
            "SELECT COUNT(*) FROM ops.InUseExecutions WITH(UPDLOCK,HOLDLOCK) WHERE RecordId=@id AND Active=1;",
            new { id }, cancellationToken, transaction)) > 0)
        { return false; }
        if (audit.Operation?.Action == "InUseDraftSaved")
        {
            foreach (Guid reviewId in (next?.Draft?.Answers ?? []).Where(a => a.Origin?.ReviewId is not null).Select(a => a.Origin!.ReviewId!.Value).Distinct())
            {
                int valid = await connection.ExecuteScalarAsync<int>(Command("""
                    SELECT COUNT(*) FROM ops.InUseServerReviews h JOIN ops.InUseRecords r WITH(HOLDLOCK) ON r.Id=h.RecordId
                    WHERE h.ReviewId=@reviewId AND COALESCE(JSON_VALUE(r.RecordJson,'$.Discarded'),'false')='false'
                        AND h.RecordVersion>COALESCE(TRY_CONVERT(bigint,JSON_VALUE(r.RecordJson,'$.InvalidatedReviewsThrough')),0);
                    """, new { reviewId }, cancellationToken, transaction));
                if (valid != 1)
                { return false; }
            }
        }
        long? version = await connection.QuerySingleOrDefaultAsync<long?>(Command(
            "SELECT Version FROM ops.InUseRecords WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id;", new { Id = id }, cancellationToken, transaction));
        if (version != expectedVersion)
        { return false; }
        if (next is not null)
        {
            await PersistAsync(connection, transaction, next, false, cancellationToken);
            if (audit.Operation?.Action == "InUseDraftSaved")
            {
                foreach (InUseServerReview review in InUseReviewHistory.Snapshots(next))
                {
                    await connection.ExecuteAsync(Command("""
                        INSERT INTO ops.InUseServerReviews(ReviewId, RecordId, RecordVersion, IdentityKey, ReviewedAt, SearchText, SnapshotJson)
                        VALUES(@Id,@RecordId,@RecordVersion,@IdentityKey,@ReviewedAt,@SearchText,@Json);
                        """, new
                    {
                        review.Id,
                        review.RecordId,
                        review.RecordVersion,
                        review.IdentityKey,
                        review.ReviewedAt,
                        SearchText = InUseReviewHistory.SearchText(review),
                        Json = JsonSerializer.Serialize(review)
                    }, cancellationToken, transaction));
                }
            }
        }
        await AuditAsync(connection, transaction, audit, cancellationToken);
        if (audit.Operation is not null)
        { await Commands.SqlOperationEvidence.AppendAsync(connection, transaction, audit.Operation, cancellationToken, writeAudit: false); }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<InUseRefreshState> ReadStateAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken token) =>
        Read<InUseRefreshState>(await connection.QuerySingleAsync<string>(Command(
            "SELECT StateJson FROM ops.InUseRefresh WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1;", null, token, transaction)));

    private static Task PersistAsync(SqlConnection connection, SqlTransaction transaction, InUseRecord record, bool insert, CancellationToken token) =>
        connection.ExecuteAsync(Command(insert ? """
            INSERT INTO ops.InUseRecords(Id, SourceId, Code, Title, AssigneeId, ReviewStatus, Version, RecordJson)
            VALUES(@Id, @SourceId, @Code, @Title, @AssigneeId, @ReviewStatus, @Version, @Json);
            """ : """
            UPDATE ops.InUseRecords SET Code = @Code, Title = @Title, AssigneeId = @AssigneeId,
                ReviewStatus = @ReviewStatus, Version = @Version, RecordJson = @Json WHERE Id = @Id;
            """, new
        {
            record.Id,
            SourceId = record.Source.Id,
            record.Source.Code,
            record.Source.Title,
            record.AssigneeId,
            ReviewStatus = record.Status,
            record.Version,
            Json = JsonSerializer.Serialize(record)
        }, token, transaction));

    private static Task AuditAsync(SqlConnection connection, SqlTransaction transaction, AuditEvent audit, CancellationToken token) =>
        connection.ExecuteAsync(Command("""
            INSERT INTO audit.AuditLog(OccurredAt, Actor, Action, CorrelationId, DetailsJson)
            VALUES(@OccurredAt, @Actor, @Action, @CorrelationId, @DetailsJson);
            """, new
        {
            audit.OccurredAt,
            audit.Actor,
            audit.Action,
            audit.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(audit.Details)
        }, token, transaction));

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("In Use stored state is invalid.");
    private static CommandDefinition Command(string sql, object? values, CancellationToken token, SqlTransaction? transaction = null) =>
        new(sql, values, transaction, commandTimeout: 15, cancellationToken: token);
}
