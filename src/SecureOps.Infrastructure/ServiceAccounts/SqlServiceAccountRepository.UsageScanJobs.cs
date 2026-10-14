using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Web usage scan jobs (migration 037, SA-010). See <see cref="IUsageScanJobStore"/>.</summary>
public sealed partial class SqlServiceAccountRepository : IUsageScanJobStore
{
    private const int _uniqueIndexViolation = 2601;

    /// <inheritdoc />
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN OBJECT_ID(N'svcacct.UsageScanJobs', N'U') IS NULL THEN 0 ELSE 1 END;",
            null, null, cancellationToken)) == 1;
    }

    /// <inheritdoc />
    public async Task<Guid?> CreateAsync(NewUsageScanJob job, SaActor actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        // The filtered unique index decides; this read under lock only names the account that holds a job, so the answer is useful.
        Guid? busy = await connection.QueryFirstOrDefaultAsync<Guid?>(Cmd("""
            SELECT TOP (1) AccountId FROM svcacct.UsageScanJobAccounts WITH (UPDLOCK, HOLDLOCK)
            WHERE IsActive = 1 AND AccountId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ids));
            """, new { ids = JsonSerializer.Serialize(job.Accounts.Select(a => a.AccountId)) }, transaction, cancellationToken));
        if (busy is not null)
        {
            return busy;
        }

        try
        {
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.UsageScanJobs(Id, State, Purpose, ExpectedAccount, AccountsJson, PlannedServers, PlanSha256, Collector, RetryOfJobId,
                    StartedBy, CreatedAt)
                VALUES(@Id, 'Queued', @Purpose, @ExpectedAccount, @AccountsJson, @PlannedServers, @PlanSha256, @Collector, @RetryOfJobId, @StartedBy, @CreatedAt);
                INSERT INTO svcacct.UsageScanJobAccounts(JobId, AccountId, SearchedName, IsActive)
                SELECT @Id, AccountId, SearchedName, 1 FROM OPENJSON(@accounts) WITH (AccountId uniqueidentifier, SearchedName nvarchar(100));
                INSERT INTO svcacct.UsageScanJobServers(JobId, Ordinal, ServerName, State)
                SELECT @Id, CONVERT(int, [key]) + 1, value, 'Pending' FROM OPENJSON(@servers);
                """, new
            {
                job.Id,
                job.Purpose,
                job.ExpectedAccount,
                AccountsJson = JsonSerializer.Serialize(job.Accounts.Select(a => a.SearchedName)),
                PlannedServers = job.Servers.Count,
                job.PlanSha256,
                job.Collector,
                job.RetryOfJobId,
                job.StartedBy,
                job.CreatedAt,
                accounts = JsonSerializer.Serialize(job.Accounts),
                servers = JsonSerializer.Serialize(job.Servers)
            }, transaction, cancellationToken));
        }
        catch (SqlException exception) when (exception.Number == _uniqueIndexViolation)
        {
            return job.Accounts[0].AccountId;
        }

        foreach (UsageScanJobAccountInput account in job.Accounts)
        {
            await HistoryAsync(connection, transaction, "UsageScanJob", job.Id, account.AccountId, "UsageScanJobRequested",
                new { job.Purpose, Planned = job.Servers.Count, job.Collector, job.RetryOfJobId, job.PlanSha256 }, null, actor, job.CreatedAt, cancellationToken);
        }

        // R2-5: counts and the plan hash; server names stay in the job record, never in the audit text.
        await AuditAsync(connection, transaction, "UsageScanJobRequested", new
        {
            EntityType = "UsageScanJob",
            EntityId = job.Id,
            AccountIds = job.Accounts.Select(a => a.AccountId),
            Planned = job.Servers.Count,
            job.Purpose,
            job.Collector,
            job.RetryOfJobId,
            job.PlanSha256
        }, actor, job.CreatedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    /// <inheritdoc />
    public async Task<UsageScanJobRecord?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IReadOnlyList<UsageScanJobRecord> jobs = await ReadJobsAsync(connection, [jobId], cancellationToken);
        return jobs.Count == 0 ? null : jobs[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageScanJobRecord>> ForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Guid[] ids = [.. await connection.QueryAsync<Guid>(Cmd("""
            SELECT TOP (@take) j.Id FROM svcacct.UsageScanJobAccounts a JOIN svcacct.UsageScanJobs j ON j.Id = a.JobId
            WHERE a.AccountId = @accountId ORDER BY j.CreatedAt DESC, j.Id;
            """, new { accountId, take }, null, cancellationToken))];
        return ids.Length == 0 ? [] : await ReadJobsAsync(connection, ids, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ActiveJobsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<Guid>(Cmd("""
            SELECT DISTINCT JobId FROM svcacct.UsageScanJobAccounts
            WHERE IsActive = 1 AND AccountId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ids));
            """, new { ids = JsonSerializer.Serialize(accountIds) }, null, cancellationToken))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageScanJobFoundItem>> ItemsAfterAsync(Guid jobId, int afterSeq, int maxItems, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<(int FinishSeq, string ServerName, string ItemsJson)> rows = await connection.QueryAsync<(int, string, string)>(Cmd("""
            SELECT FinishSeq, ServerName, ItemsJson FROM svcacct.UsageScanJobServers
            WHERE JobId = @jobId AND FinishSeq > @afterSeq AND Status = 'Ok' AND FormerMatches + ISNULL(ExpectedMatches, 0) > 0
            ORDER BY FinishSeq;
            """, new { jobId, afterSeq }, null, cancellationToken));
        List<UsageScanJobFoundItem> items = [];
        foreach ((int seq, string _, string json) in rows)
        {
            ParsedScanItem[] found = JsonSerializer.Deserialize<ParsedScanItem[]>(json) ?? [];
            if (items.Count > 0 && items.Count + found.Length > maxItems)
            {
                break;
            }

            items.AddRange(found.Select(i => new UsageScanJobFoundItem(seq, i)));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<bool> RequestCancelAsync(Guid jobId, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        string? state = await connection.QuerySingleOrDefaultAsync<string?>(Cmd("""
            SELECT State FROM svcacct.UsageScanJobs WITH (UPDLOCK, HOLDLOCK) WHERE Id = @jobId;
            """, new { jobId }, transaction, cancellationToken));
        if (state is not ("Queued" or "Running"))
        {
            return false;
        }

        await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.UsageScanJobs SET CancelRequestedAt = ISNULL(CancelRequestedAt, @now), CancelRequestedBy = ISNULL(CancelRequestedBy, @UserId)
            WHERE Id = @jobId;
            """, new { jobId, now, actor.UserId }, transaction, cancellationToken));
        if (state == "Queued")
        {
            // Nothing was read yet: close it here, the runner will find it not claimable.
            await FinishInTransactionAsync(connection, transaction, jobId, new UsageScanJobFinish(UsageScanJobState.Cancelled, null, null,
                new Dictionary<Guid, string>(), now), actor, cancellationToken);
        }
        else
        {
            await AuditAsync(connection, transaction, "UsageScanJobCancelRequested", new { EntityType = "UsageScanJob", EntityId = jobId }, actor, now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> ExpireAsync(IReadOnlyCollection<Guid> jobIds, DateTimeOffset now, DateTimeOffset queuedBefore, CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
        {
            return 0;
        }

        await using SqlConnection connection = await OpenAsync(cancellationToken);
        Guid[] stale = [.. await connection.QueryAsync<Guid>(Cmd("""
            SELECT Id FROM svcacct.UsageScanJobs
            WHERE Id IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ids))
              AND ((State = 'Running' AND LeaseUntil < @now) OR (State = 'Queued' AND CreatedAt < @queuedBefore));
            """, new { ids = JsonSerializer.Serialize(jobIds), now, queuedBefore }, null, cancellationToken))];
        int closed = 0;
        foreach (Guid jobId in stale)
        {
            await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
            (string State, Guid StartedBy, DateTimeOffset? LeaseUntil, DateTimeOffset CreatedAt)? row =
                await connection.QuerySingleOrDefaultAsync<(string, Guid, DateTimeOffset?, DateTimeOffset)?>(Cmd("""
                    SELECT State, StartedBy, LeaseUntil, CreatedAt FROM svcacct.UsageScanJobs WITH (UPDLOCK, HOLDLOCK) WHERE Id = @jobId;
                    """, new { jobId }, transaction, cancellationToken));
            string? code = row switch
            {
                { State: "Running" } r when r.LeaseUntil < now => "workerLost",
                { State: "Queued" } r when r.CreatedAt < queuedBefore => "notStarted",
                _ => null
            };
            if (code is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            await FinishInTransactionAsync(connection, transaction, jobId, new UsageScanJobFinish(UsageScanJobState.Failed, code, null,
                new Dictionary<Guid, string>(), now), new SaActor(row!.Value.StartedBy, "usage-scan-job-expiry:" + jobId.ToString("N")), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            closed++;
        }

        return closed;
    }

    /// <inheritdoc />
    public async Task<bool> ClaimAsync(Guid jobId, Guid leaseId, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        (string State, DateTimeOffset? CancelRequestedAt, Guid StartedBy)? row = await connection.QuerySingleOrDefaultAsync<(string, DateTimeOffset?, Guid)?>(Cmd("""
            SELECT State, CancelRequestedAt, StartedBy FROM svcacct.UsageScanJobs WITH (UPDLOCK, HOLDLOCK) WHERE Id = @jobId;
            """, new { jobId }, transaction, cancellationToken));
        if (row is not { State: "Queued" } queued)
        {
            return false;
        }

        if (queued.CancelRequestedAt is not null)
        {
            await FinishInTransactionAsync(connection, transaction, jobId, new UsageScanJobFinish(UsageScanJobState.Cancelled, null, null,
                new Dictionary<Guid, string>(), now), new SaActor(queued.StartedBy, "usage-scan-job:" + jobId.ToString("N")), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.UsageScanJobs SET State = 'Running', StartedAt = @now, LeaseId = @leaseId, LeaseUntil = @leaseUntil WHERE Id = @jobId;
            """, new { jobId, now, leaseId, leaseUntil }, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<(bool Owned, bool CancelRequested)> HeartbeatAsync(Guid jobId, Guid leaseId, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        (int Owned, int Cancel)? row = await connection.QuerySingleOrDefaultAsync<(int, int)?>(Cmd("""
            UPDATE svcacct.UsageScanJobs SET LeaseUntil = @leaseUntil
            OUTPUT 1, CASE WHEN inserted.CancelRequestedAt IS NULL THEN 0 ELSE 1 END
            WHERE Id = @jobId AND State = 'Running' AND LeaseId = @leaseId;
            """, new { jobId, leaseId, leaseUntil }, null, cancellationToken));
        return row is { } r ? (r.Owned == 1, r.Cancel == 1) : (false, false);
    }

    /// <inheritdoc />
    public async Task MarkConnectingAsync(Guid jobId, Guid leaseId, string serverName, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(Cmd("""
            UPDATE s SET State = 'Connecting' FROM svcacct.UsageScanJobServers s
            WHERE s.JobId = @jobId AND s.ServerName = @serverName AND s.State = 'Pending'
              AND EXISTS (SELECT 1 FROM svcacct.UsageScanJobs j WHERE j.Id = @jobId AND j.State = 'Running' AND j.LeaseId = @leaseId);
            """, new { jobId, leaseId, serverName }, null, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> CompleteServerAsync(Guid jobId, Guid leaseId, UsageScanServerCompletion completion, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken);
        bool ok = completion.Status == UsageCollectionStatus.Ok;
        int changed = await connection.ExecuteAsync(Cmd("""
            IF EXISTS (SELECT 1 FROM svcacct.UsageScanJobs WITH (UPDLOCK, HOLDLOCK) WHERE Id = @jobId AND State = 'Running' AND LeaseId = @leaseId)
            BEGIN
                DECLARE @seq int = (SELECT ISNULL(MAX(FinishSeq), 0) + 1 FROM svcacct.UsageScanJobServers WITH (UPDLOCK, HOLDLOCK) WHERE JobId = @jobId);
                UPDATE svcacct.UsageScanJobServers
                SET State = 'Done', Status = @Status, DocumentResult = @DocumentResult, Document = @Document, ItemsJson = @ItemsJson,
                    FormerMatches = @FormerMatches, ExpectedMatches = @ExpectedMatches, RefusalCode = @RefusalCode, ElapsedMs = @ElapsedMs,
                    FinishedAt = @FinishedAt, FinishSeq = @seq
                WHERE JobId = @jobId AND ServerName = @ServerName AND State <> 'Done';
            END
            """, new
        {
            jobId,
            leaseId,
            completion.ServerName,
            Status = completion.Status.ToString(),
            DocumentResult = ok ? completion.DocumentResult?.ToString() : null,
            Document = ok ? completion.Document : null,
            ItemsJson = ok ? JsonSerializer.Serialize(completion.Items) : null,
            FormerMatches = ok ? completion.Items.Count(i => i.Role == "Former") : (int?)null,
            ExpectedMatches = ok ? completion.Items.Count(i => i.Role == "Expected") : (int?)null,
            completion.RefusalCode,
            completion.ElapsedMs,
            completion.FinishedAt
        }, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return changed > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, byte[]>> DocumentsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return (await connection.QueryAsync<(string ServerName, byte[] Document)>(Cmd("""
            SELECT ServerName, Document FROM svcacct.UsageScanJobServers WHERE JobId = @jobId AND Status = 'Ok';
            """, new { jobId }, null, cancellationToken, _commitTimeoutSeconds)))
            .ToDictionary(r => r.ServerName, r => r.Document, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<SaResult<(Guid ScanId, bool Attached)>> AttachEvidenceAsync(Guid accountId, UsageScanUpload upload, SaActor actor, CancellationToken cancellationToken) =>
        // Same write as an upload: scope and responsibility re-read under lock for the starter (organization scope or owner team).
        AttachUsageScanAsync(accountId, upload, null, actor,
            (scope, anchor, _) => scope.CoversAtOrganizationLevel(anchor) || scope.CoversTeam(anchor.OwnerTeamId), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> FinishAsync(Guid jobId, Guid? leaseId, UsageScanJobFinish finish, SaActor actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        int open = await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM svcacct.UsageScanJobs WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = @jobId AND State IN ('Queued','Running') AND (@leaseId IS NULL OR LeaseId = @leaseId);
            """, new { jobId, leaseId }, transaction, cancellationToken));
        if (open == 0)
        {
            return false;
        }

        await FinishInTransactionAsync(connection, transaction, jobId, finish, actor, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task FinishInTransactionAsync(SqlConnection connection, SqlTransaction transaction, Guid jobId, UsageScanJobFinish finish, SaActor actor,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Cmd("""
            DECLARE @seq int = (SELECT ISNULL(MAX(FinishSeq), 0) FROM svcacct.UsageScanJobServers WITH (UPDLOCK, HOLDLOCK) WHERE JobId = @jobId);
            WITH open_servers AS (
                SELECT FinishSeq, Status, State, FinishedAt, ROW_NUMBER() OVER (ORDER BY Ordinal) AS n
                FROM svcacct.UsageScanJobServers WHERE JobId = @jobId AND State <> 'Done')
            UPDATE open_servers SET State = 'Done', Status = 'NotRun', FinishedAt = @At, FinishSeq = @seq + n;
            UPDATE a SET IsActive = 0, LinkOutcome = ISNULL(l.Outcome, 'NotLinked')
            FROM svcacct.UsageScanJobAccounts a
            LEFT JOIN OPENJSON(@links) WITH (AccountId uniqueidentifier, Outcome varchar(16)) l ON l.AccountId = a.AccountId
            WHERE a.JobId = @jobId;
            UPDATE svcacct.UsageScanJobs SET State = @State, FailureCode = @FailureCode, ScanId = @ScanId, FinishedAt = @At, LeaseId = NULL, LeaseUntil = NULL
            WHERE Id = @jobId;
            """, new
        {
            jobId,
            finish.At,
            State = finish.State.ToString(),
            finish.FailureCode,
            finish.ScanId,
            links = JsonSerializer.Serialize(finish.LinkOutcomes.Select(l => new { AccountId = l.Key, Outcome = l.Value }))
        }, transaction, cancellationToken));
        (int Planned, int Ok, int Found, int NotCovered) counts = await connection.QuerySingleAsync<(int, int, int, int)>(Cmd("""
            SELECT COUNT(*), SUM(CASE WHEN Status = 'Ok' THEN 1 ELSE 0 END), ISNULL(SUM(FormerMatches), 0),
                SUM(CASE WHEN Status <> 'Ok' THEN 1 ELSE 0 END)
            FROM svcacct.UsageScanJobServers WHERE JobId = @jobId;
            """, new { jobId }, transaction, cancellationToken));
        Guid[] accounts = [.. await connection.QueryAsync<Guid>(Cmd("SELECT AccountId FROM svcacct.UsageScanJobAccounts WHERE JobId = @jobId;",
            new { jobId }, transaction, cancellationToken))];
        string action = finish.State switch
        {
            UsageScanJobState.Completed => "UsageScanJobCompleted",
            UsageScanJobState.Cancelled => "UsageScanJobCancelled",
            _ => "UsageScanJobFailed"
        };
        foreach (Guid accountId in accounts)
        {
            await HistoryAsync(connection, transaction, "UsageScanJob", jobId, accountId, action, new
            {
                counts.Planned,
                Answered = counts.Ok,
                counts.NotCovered,
                finish.FailureCode,
                finish.ScanId,
                Link = finish.LinkOutcomes.TryGetValue(accountId, out string? link) ? link : "NotLinked"
            }, null, actor, finish.At, cancellationToken);
        }

        await AuditAsync(connection, transaction, action, new
        {
            EntityType = "UsageScanJob",
            EntityId = jobId,
            counts.Planned,
            Answered = counts.Ok,
            Matches = counts.Found,
            counts.NotCovered,
            finish.FailureCode,
            finish.ScanId
        }, actor, finish.At, cancellationToken);
    }

    private static async Task<IReadOnlyList<UsageScanJobRecord>> ReadJobsAsync(SqlConnection connection, Guid[] ids, CancellationToken cancellationToken)
    {
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT Id, State, Purpose, ExpectedAccount, AccountsJson, PlannedServers, PlanSha256, Collector, RetryOfJobId, StartedBy, CreatedAt, StartedAt,
                FinishedAt, CancelRequestedAt, FailureCode, ScanId, LeaseUntil
            FROM svcacct.UsageScanJobs WHERE Id IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ids));
            SELECT JobId, AccountId, SearchedName, IsActive, LinkOutcome FROM svcacct.UsageScanJobAccounts
            WHERE JobId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ids));
            SELECT JobId, Ordinal, ServerName, State, Status, DocumentResult, FormerMatches, ExpectedMatches, RefusalCode, ElapsedMs, FinishedAt, FinishSeq
            FROM svcacct.UsageScanJobServers WHERE JobId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ids)) ORDER BY JobId, Ordinal;
            """, new { ids = JsonSerializer.Serialize(ids) }, null, cancellationToken));
        JobRow[] jobs = [.. await grid.ReadAsync<JobRow>()];
        ILookup<Guid, JobAccountRow> accounts = (await grid.ReadAsync<JobAccountRow>()).ToLookup(a => a.JobId);
        ILookup<Guid, JobServerRow> servers = (await grid.ReadAsync<JobServerRow>()).ToLookup(s => s.JobId);
        return [.. ids.Select(id => jobs.FirstOrDefault(j => j.Id == id)).Where(j => j is not null).Select(j => new UsageScanJobRecord(j!.Id,
            Enum.Parse<UsageScanJobState>(j.State), j.Purpose, j.ExpectedAccount, JsonSerializer.Deserialize<string[]>(j.AccountsJson) ?? [], j.PlannedServers,
            j.PlanSha256, j.Collector, j.RetryOfJobId, j.StartedBy, j.CreatedAt, j.StartedAt, j.FinishedAt, j.CancelRequestedAt, j.FailureCode, j.ScanId,
            j.LeaseUntil,
            [.. accounts[j.Id].Select(a => new UsageScanJobAccount(a.AccountId, a.SearchedName, a.IsActive, a.LinkOutcome))],
            [.. servers[j.Id].Select(s => new UsageScanJobServer(s.Ordinal, s.ServerName, Enum.Parse<UsageScanJobServerState>(s.State),
                s.Status is null ? null : Enum.Parse<UsageCollectionStatus>(s.Status), s.DocumentResult is null ? null : Enum.Parse<ScanServerResult>(s.DocumentResult),
                s.FormerMatches, s.ExpectedMatches, s.RefusalCode, s.ElapsedMs, s.FinishedAt, s.FinishSeq))]))];
    }

    private sealed record JobRow(Guid Id, string State, string Purpose, string? ExpectedAccount, string AccountsJson, int PlannedServers, string PlanSha256,
        string Collector, Guid? RetryOfJobId, Guid StartedBy, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
        DateTimeOffset? CancelRequestedAt, string? FailureCode, Guid? ScanId, DateTimeOffset? LeaseUntil);

    private sealed record JobAccountRow(Guid JobId, Guid AccountId, string SearchedName, bool IsActive, string? LinkOutcome);

    private sealed record JobServerRow(Guid JobId, int Ordinal, string ServerName, string State, string? Status, string? DocumentResult, int? FormerMatches,
        int? ExpectedMatches, string? RefusalCode, int? ElapsedMs, DateTimeOffset? FinishedAt, int? FinishSeq);
}
