using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>A claimed outbox row.</summary>
public sealed record ReminderClaim(Guid Id, Guid AccountId, string RuleCode, string Channel, string PayloadJson, int AttemptCount);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>Open requests with the dates the reminder rules read (job context; visibility is scoped when listing).</summary>
    public async Task<IReadOnlyList<ReminderInput>> ReminderInputsAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<(Guid RequestId, Guid AccountId, string Name, string Type, Guid? Team, DateOnly? End, DateOnly? Next, DateOnly? Sent, DateOnly? Reply)>(Cmd("""
            SELECT r.Id, r.AccountId, a.AccountName, r.ActionType, r.TargetTeamId, r.PlanEnd, r.NextFollowupOn, r.FirstSentOn, r.LastReplyOn
            FROM svcacct.WorkRequests r JOIN svcacct.Accounts a ON a.Id = r.AccountId WHERE r.Status = 'Open';
            """, null, null, cancellationToken, _commitTimeoutSeconds)))
            .Select(r => new ReminderInput(r.RequestId, r.AccountId, r.Name, Enum.Parse<ServiceAccountActionType>(r.Type), r.Team, r.End, r.Next, r.Sent, r.Reply))];
    }

    /// <summary>Adds due reminders once per (request, rule, due date, channel); existing keys are skipped.</summary>
    public async Task<int> EnqueueRemindersAsync(IEnumerable<(ReminderInput Request, DueReminder Due)> reminders, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken);
        int inserted = 0;
        foreach ((ReminderInput request, DueReminder due) in reminders)
        {
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{request.RequestId:N}|{due.RuleCode}|{due.DueDate:yyyy-MM-dd}|{due.Channel}"))).ToLowerInvariant();
            inserted += await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.ReminderOutbox(Id, RequestId, AccountId, RuleCode, DueDate, Channel, IdempotencyKey, TargetTeamId, Status, AttemptCount,
                    NextAttemptAt, PayloadJson, CreatedAt)
                SELECT NEWID(), @RequestId, @AccountId, @RuleCode, @DueDate, @Channel, @key, @TargetTeamId, 'Pending', 0, @now, @payload, @now
                WHERE NOT EXISTS (SELECT 1 FROM svcacct.ReminderOutbox WITH (UPDLOCK, HOLDLOCK) WHERE IdempotencyKey = @key);
                """, new
            {
                request.RequestId,
                request.AccountId,
                due.RuleCode,
                due.DueDate,
                Channel = due.Channel.ToString(),
                key,
                request.TargetTeamId,
                now,
                payload = JsonSerializer.Serialize(new { due.Message, Rule = ReminderRules.Label(due.RuleCode), Account = request.AccountName })
            }, transaction, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    /// <summary>
    /// Claims due rows under a lease (multi-instance safe: UPDLOCK + READPAST). The explicit READ COMMITTED transaction
    /// matters: a pooled connection can retain SERIALIZABLE from an earlier transaction, where READPAST is rejected.
    /// </summary>
    public async Task<IReadOnlyList<ReminderClaim>> ClaimRemindersAsync(string owner, int take, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.ReadCommitted);
        IReadOnlyList<ReminderClaim> claims = [.. await connection.QueryAsync<ReminderClaim>(Cmd("""
            WITH due AS (
                SELECT TOP (@take) * FROM svcacct.ReminderOutbox WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status IN ('Pending','Failed') AND NextAttemptAt <= @now AND (LeaseUntil IS NULL OR LeaseUntil < @now)
                ORDER BY NextAttemptAt, Id)
            UPDATE due SET LeaseOwner = @owner, LeaseUntil = @until
            OUTPUT inserted.Id, inserted.AccountId, inserted.RuleCode, inserted.Channel, inserted.PayloadJson, inserted.AttemptCount;
            """, new { take, now, owner, until = now + lease }, transaction, cancellationToken))];
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    /// <summary>Marks a claimed reminder delivered (only by its lease owner).</summary>
    public async Task<bool> CompleteReminderAsync(Guid id, string owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.ReminderOutbox SET Status = 'Delivered', DeliveredAt = @now, LeaseOwner = NULL, LeaseUntil = NULL, LastError = NULL
            WHERE Id = @id AND LeaseOwner = @owner;
            """, new { id, owner, now }, null, cancellationToken)) == 1;
    }

    /// <summary>Records a bounded retry, dead-lettering after the configured attempts; the failure stays visible.</summary>
    public async Task<string?> FailReminderAsync(Guid id, string owner, string error, int maxAttempts, DateTimeOffset nextAttempt, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(Cmd("""
            UPDATE svcacct.ReminderOutbox SET AttemptCount = AttemptCount + 1,
                Status = CASE WHEN AttemptCount + 1 >= @maxAttempts THEN 'DeadLetter' ELSE 'Failed' END,
                NextAttemptAt = @nextAttempt, LastError = @error, LeaseOwner = NULL, LeaseUntil = NULL
            OUTPUT inserted.Status
            WHERE Id = @id AND LeaseOwner = @owner;
            """, new { id, owner, error = Truncate(error, 400), maxAttempts, nextAttempt }, null, cancellationToken));
    }

    /// <summary>Scoped reminders (in-app list, coordinator drafts, failures).</summary>
    public async Task<IReadOnlyList<ReminderView>> RemindersAsync(ServiceAccountScope scope, string? status, bool myTeam, CancellationToken cancellationToken)
    {
        DynamicParameters parameters = ScopeParameters(scope, new
        {
            status,
            myTeam,
            myTeams = JsonSerializer.Serialize(scope.DirectTeams.Select(t => t.ToString("D")))
        });
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<ReminderRow>(Cmd($"""
            SELECT TOP (500) o.Id, o.RequestId, o.AccountId, a.AccountName, o.RuleCode, o.DueDate, o.Channel, o.Status, o.AttemptCount, o.LastError, o.PayloadJson,
                o.CreatedAt, o.RowVer
            FROM svcacct.ReminderOutbox o JOIN svcacct.Accounts a ON a.Id = o.AccountId
            WHERE {ScopePredicate} AND (@status IS NULL OR o.Status = @status)
              AND (@myTeam = 0 OR o.TargetTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@myTeams)))
            ORDER BY o.DueDate DESC, o.CreatedAt DESC, o.Id;
            """, parameters, null, cancellationToken)))
            .Select(r => new ReminderView(r.Id, r.RequestId, r.AccountId, r.AccountName, r.RuleCode, ReminderRules.Label(r.RuleCode), r.DueDate, r.Channel, r.Status,
                r.AttemptCount, r.LastError, JsonDocument.Parse(r.PayloadJson).RootElement.GetProperty("Message").GetString() ?? string.Empty, r.CreatedAt, Version(r.RowVer)))];
    }

    private sealed record ReminderRow(Guid Id, Guid RequestId, Guid AccountId, string AccountName, string RuleCode, DateOnly DueDate, string Channel, string Status,
        int AttemptCount, string? LastError, string PayloadJson, DateTimeOffset CreatedAt, byte[] RowVer);

    /// <summary>Dismisses a delivered reminder at its expected version.</summary>
    public async Task<SaResult<Guid>> DismissReminderAsync(Guid id, Guid accountId, string expectedVersion, SaActor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.ReadCommitted);
        int changed = await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.ReminderOutbox SET Status = 'Dismissed', DismissedBy = @UserId, DismissedAt = @now
            WHERE Id = @id AND AccountId = @accountId AND Status IN ('Delivered','DeadLetter') AND RowVer = @RowVer;
            """, new { actor.UserId, now, id, accountId, RowVer = Version(expectedVersion) }, transaction, cancellationToken));
        if (changed != 1)
        {
            return SaResult<Guid>.Fail(SaErrors.Conflict, "expectedVersion");
        }

        await HistoryAsync(connection, transaction, "Reminder", id, accountId, "ReminderDismissed", null, null, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "ReminderDismissed", new { ReminderId = id, AccountId = accountId }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Account of a reminder (for scope checks).</summary>
    public async Task<Guid?> ReminderAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(Cmd("SELECT AccountId FROM svcacct.ReminderOutbox WHERE Id = @id;", new { id }, null, cancellationToken));
    }
}
