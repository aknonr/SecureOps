using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>One stored "open OR" attempt (migration 035) with its append-only events, oldest first.</summary>
public sealed record OperationalRecordSubmissionRecord(Guid Id, Guid PlanId, Guid AccountId, OperationalRecordState State, string? ReasonCode, string Provider,
    string? ExternalId, string? ExternalNumber, string? LinkVerification, string Environment, string ShortDescription, string Description, string Fingerprint,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<OperationalRecordEventView> Events);

/// <summary>The account's place in the plan's current preview: preview id, version, digest and the account's servers.</summary>
public sealed record OperationalRecordPlanContext(Guid PreviewId, int PreviewVersion, string PreviewSha256, IReadOnlyList<string> Servers);

/// <summary>Everything a reservation stores before the remote call: the exact content that will be sent.</summary>
public sealed record OperationalRecordReservation(Guid PlanId, Guid AccountId, Guid PreviewId, int PreviewVersion, Guid IdempotencyKey, string Fingerprint,
    string Provider, string Environment, string RequesterExternalId, string ShortDescription, string Description);

/// <summary>Outcome to persist after the call; numbers only when the provider proved them.</summary>
public sealed record OperationalRecordOutcomeWrite(OperationalRecordState State, string? ReasonCode, string? ExternalId, string? ExternalNumber);

public sealed partial class SqlServiceAccountRepository : IOperationalRecordStore
{
    /// <summary>Repository-only answer: the idempotency key already holds a submission (the service compares fingerprints).</summary>
    internal const string SubmissionKeyExists = "submissionKeyExists";

    private const string _liveStates = "('Sending','Created','PartiallyCreated','OutcomeUnknown','Linked')";

    /// <summary>Whether migration 035 is installed on this database.</summary>
    public async Task<bool> OperationalRecordsInstalledAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT CASE WHEN OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions', N'U') IS NULL
                OR OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents', N'U') IS NULL THEN 0 ELSE 1 END;
            """, null, null, cancellationToken)) == 1;
    }

    /// <summary>The account's rows in the plan's current preview (servers), or null when the account has none.</summary>
    public async Task<OperationalRecordPlanContext?> OperationalRecordContextAsync(Guid planId, Guid accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await RetryReadOnDeadlockAsync(() => ContextAsync(connection, null, planId, accountId, cancellationToken));
    }

    /// <summary>Every submission of the plan with its events, newest first per account.</summary>
    public async Task<IReadOnlyList<OperationalRecordSubmissionRecord>> OperationalRecordSubmissionsAsync(Guid planId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await RetryReadOnDeadlockAsync(() => SubmissionsAsync(connection, null, "s.PlanId = @planId", new { planId }, cancellationToken));
    }

    /// <summary>The submission an idempotency key already holds on this account, or null.</summary>
    public async Task<OperationalRecordSubmissionRecord?> OperationalRecordByKeyAsync(Guid accountId, Guid key, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return (await RetryReadOnDeadlockAsync(() => SubmissionsAsync(connection, null, "s.AccountId = @accountId AND s.IdempotencyKey = @key",
            new { accountId, key }, cancellationToken))).SingleOrDefault();
    }

    /// <summary>
    /// Reserves one submission in state Sending BEFORE any remote call (ADR-0030 §3). With the plan row locked: scope re-read
    /// (outside = 404), Work and the responsible basis on every account (403), plan Approved or InProgress (409
    /// <c>planNotApproved</c>/<c>planClosed</c>), the account in the current preview (404), the preview still the one the content
    /// was built from (409 <c>orPreviewStale</c>), the key not used yet (<see cref="SubmissionKeyExists"/>) and no live submission
    /// for the plan account (409 <c>submissionExists</c>; the filtered unique index refuses the same in the database).
    /// </summary>
    public async Task<SaResult<Guid>> ReserveOperationalRecordAsync(OperationalRecordReservation reservation, ChangePlanCaller caller,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        ChangePlanSnapshot? plan = await SnapshotAsync(connection, transaction, reservation.PlanId, true, cancellationToken);
        if (plan is null)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        ServiceAccountScope scope = await LockedScopeAsync(connection, transaction, caller.Actor.UserId, cancellationToken);
        if (Guard(plan, scope, caller.Work, null) is { } refused)
        {
            return refused;
        }

        if (plan.Status is not (ChangePlanStatus.Approved or ChangePlanStatus.InProgress) || plan.Approval is null)
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, ChangePlanRules.IsOpen(plan.Status) ? "planNotApproved" : "planClosed");
        }

        if (plan.Accounts.All(a => a.View.AccountId != reservation.AccountId)
            || await ContextAsync(connection, transaction, reservation.PlanId, reservation.AccountId, cancellationToken) is not { } context)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        if (context.PreviewId != reservation.PreviewId || context.PreviewVersion != reservation.PreviewVersion)
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "orPreviewStale");
        }

        (int keyed, int live) = await connection.QuerySingleAsync<(int, int)>(Cmd($"""
            SELECT (SELECT COUNT(*) FROM svcacct.ChangePlanRecordSubmissions WITH (UPDLOCK, HOLDLOCK) WHERE AccountId = @AccountId AND IdempotencyKey = @IdempotencyKey),
                (SELECT COUNT(*) FROM svcacct.ChangePlanRecordSubmissions WITH (UPDLOCK, HOLDLOCK)
                 WHERE PlanId = @PlanId AND AccountId = @AccountId AND State IN {_liveStates});
            """, new { reservation.AccountId, reservation.IdempotencyKey, reservation.PlanId }, transaction, cancellationToken));
        if (keyed > 0)
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, SubmissionKeyExists);
        }

        if (live > 0)
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "submissionExists");
        }

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ChangePlanRecordSubmissions(Id, PlanId, AccountId, PreviewId, IdempotencyKey, Fingerprint, Provider, RecordKind, Environment,
                RequesterExternalId, ShortDescription, Description, State, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
            VALUES(@id, @PlanId, @AccountId, @PreviewId, @IdempotencyKey, @Fingerprint, @Provider, 'OR', @Environment, @RequesterExternalId, @ShortDescription,
                @Description, 'Sending', @UserId, @now, @UserId, @now);
            """, new
        {
            id,
            reservation.PlanId,
            reservation.AccountId,
            reservation.PreviewId,
            reservation.IdempotencyKey,
            reservation.Fingerprint,
            reservation.Provider,
            reservation.Environment,
            reservation.RequesterExternalId,
            reservation.ShortDescription,
            reservation.Description,
            caller.Actor.UserId,
            now
        }, transaction, cancellationToken));
        await SubmissionEventAsync(connection, transaction, id, "Started", null, OperationalRecordState.Sending, null, null, null, caller.Actor, now, cancellationToken);
        await HistoryAsync(connection, transaction, "ChangePlanRecord", id, reservation.AccountId, "OperationalRecordSubmissionStarted",
            new { PlanId = reservation.PlanId, reservation.Provider, reservation.Fingerprint }, null, caller.Actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "OperationalRecordSubmissionStarted", new
        {
            PlanId = reservation.PlanId,
            reservation.AccountId,
            SubmissionId = id,
            reservation.Fingerprint,
            reservation.Provider
        }, caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Persists the provider's outcome in its own transaction, only while the row is still Sending. False when the row is no
    /// longer Sending or the write failed on a uniqueness rule; the row then stays as it is and the person reconciles.
    /// </summary>
    public async Task<bool> RecordOperationalRecordOutcomeAsync(Guid submissionId, OperationalRecordOutcomeWrite outcome, SaActor actor,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        (Guid PlanId, Guid AccountId)[] rows = [.. await connection.QueryAsync<(Guid PlanId, Guid AccountId)>(Cmd("""
            SELECT PlanId, AccountId FROM svcacct.ChangePlanRecordSubmissions WITH (UPDLOCK, HOLDLOCK) WHERE Id = @submissionId AND State = 'Sending';
            """, new { submissionId }, transaction, cancellationToken))];
        if (rows.Length != 1)
        {
            return false;
        }

        (Guid PlanId, Guid AccountId) target = rows[0];

        try
        {
            await connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.ChangePlanRecordSubmissions SET State = @state, ExternalId = @ExternalId, ExternalNumber = @ExternalNumber,
                    OutcomeReason = @ReasonCode, UpdatedBy = @UserId, UpdatedAt = @now
                WHERE Id = @submissionId AND State = 'Sending';
                """, new { submissionId, state = outcome.State.ToString(), outcome.ExternalId, outcome.ExternalNumber, outcome.ReasonCode, actor.UserId, now },
                transaction, cancellationToken));
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return false;
        }

        await SubmissionEventAsync(connection, transaction, submissionId, outcome.State.ToString(), OperationalRecordState.Sending, outcome.State, outcome.ReasonCode,
            outcome.ExternalNumber, null, actor, now, cancellationToken);
        string action = "OperationalRecord" + outcome.State;
        await HistoryAsync(connection, transaction, "ChangePlanRecord", submissionId, target.AccountId, action,
            new { target.PlanId, State = outcome.State.ToString(), outcome.ReasonCode, RecordNumber = outcome.ExternalNumber }, null, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, action, new
        {
            target.PlanId,
            target.AccountId,
            SubmissionId = submissionId,
            State = outcome.State.ToString(),
            outcome.ReasonCode,
            RecordNumber = outcome.ExternalNumber
        }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// A person's reconciliation of the account's unresolved submission: link an OR number (<paramref name="linkNumber"/> set)
    /// or confirm that none was created. With the plan row locked: scope and basis as for every plan command (the plan may be
    /// in any status, so a cancelled plan's OR can still be recorded truthfully), the submission is the account's in this plan
    /// (404), its state allows the step (409 <c>submissionResolved</c>), it is not possibly still in flight (409
    /// <c>submissionInFlight</c>), a known internal id matches the looked-up record (409 <c>recordMismatch</c>) and the number
    /// is not held by another live submission (409 <c>recordNumberInUse</c>).
    /// </summary>
    public async Task<SaResult<Guid>> ReconcileOperationalRecordAsync(Guid planId, Guid accountId, Guid submissionId, string? linkNumber, string? linkedSourceId,
        bool verified, string reason, ChangePlanCaller caller, TimeSpan dispatchBound, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        ChangePlanSnapshot? plan = await SnapshotAsync(connection, transaction, planId, true, cancellationToken);
        if (plan is null)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        ServiceAccountScope scope = await LockedScopeAsync(connection, transaction, caller.Actor.UserId, cancellationToken);
        if (Guard(plan, scope, caller.Work, null) is { } refused)
        {
            return refused;
        }

        OperationalRecordSubmissionRecord? submission = (await SubmissionsAsync(connection, transaction, "s.Id = @submissionId AND s.PlanId = @planId AND s.AccountId = @accountId",
            new { submissionId, planId, accountId }, cancellationToken, true)).SingleOrDefault();
        if (submission is null)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        bool link = linkNumber is not null;
        if (link ? !OperationalRecordRules.CanLink(submission.State) : !OperationalRecordRules.CanConfirmNotCreated(submission.State))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "submissionResolved");
        }

        if (OperationalRecordRules.MayStillBeInFlight(submission.State, submission.CreatedAt, now, dispatchBound))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "submissionInFlight");
        }

        string? externalId = submission.ExternalId;
        if (link && linkedSourceId is not null)
        {
            if (externalId is not null && !string.Equals(externalId, linkedSourceId, StringComparison.Ordinal))
            {
                return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "recordMismatch");
            }

            externalId = linkedSourceId;
        }

        if (link && await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM svcacct.ChangePlanRecordSubmissions WITH (UPDLOCK, HOLDLOCK)
            WHERE ExternalNumber = @linkNumber AND Id <> @submissionId AND State IN ('Created','PartiallyCreated','Linked');
            """, new { linkNumber, submissionId }, transaction, cancellationToken)) > 0)
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "recordNumberInUse");
        }

        OperationalRecordState next = link ? OperationalRecordState.Linked : OperationalRecordState.ConfirmedNotCreated;
        await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.ChangePlanRecordSubmissions SET State = @state, ExternalId = @externalId, ExternalNumber = @linkNumber,
                LinkVerification = @verification, UpdatedBy = @UserId, UpdatedAt = @now
            WHERE Id = @submissionId;
            """, new
        {
            submissionId,
            state = next.ToString(),
            externalId,
            linkNumber,
            verification = link ? verified ? "Found" : "NotVerified" : null,
            caller.Actor.UserId,
            now
        }, transaction, cancellationToken));
        await SubmissionEventAsync(connection, transaction, submissionId, next.ToString(), submission.State, next, null, linkNumber, reason, caller.Actor, now,
            cancellationToken);
        string action = "OperationalRecord" + next;
        await HistoryAsync(connection, transaction, "ChangePlanRecord", submissionId, accountId, action,
            new { PlanId = planId, From = submission.State.ToString(), RecordNumber = linkNumber, Verified = link ? verified : (bool?)null }, reason, caller.Actor, now,
            cancellationToken);
        await AuditAsync(connection, transaction, action, new
        {
            PlanId = planId,
            AccountId = accountId,
            SubmissionId = submissionId,
            From = submission.State.ToString(),
            RecordNumber = linkNumber,
            Verified = link ? verified : (bool?)null
        }, caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return submissionId;
    }

    private static async Task<OperationalRecordPlanContext?> ContextAsync(SqlConnection connection, SqlTransaction? transaction, Guid planId, Guid accountId,
        CancellationToken cancellationToken)
    {
        (Guid PreviewId, int Version, string Sha)[] previews = [.. await connection.QueryAsync<(Guid PreviewId, int Version, string Sha)>(Cmd("""
            SELECT v.Id, v.Version, v.Sha256 FROM svcacct.ChangePlanPreviews v
            JOIN svcacct.ChangePlans p ON p.Id = v.PlanId AND p.CurrentPreviewVersion = v.Version WHERE v.PlanId = @planId;
            """, new { planId }, transaction, cancellationToken))];
        if (previews.Length != 1)
        {
            return null;
        }

        (Guid PreviewId, int Version, string Sha) current = previews[0];

        string?[] servers = [.. await connection.QueryAsync<string?>(Cmd("""
            SELECT ServerName FROM svcacct.ChangePlanItems WHERE PreviewId = @PreviewId AND AccountId = @accountId;
            """, new { current.PreviewId, accountId }, transaction, cancellationToken))];
        return servers.Length == 0 ? null : new OperationalRecordPlanContext(current.PreviewId, current.Version, current.Sha, OperationalRecordRules.Servers(servers));
    }

    private static async Task<IReadOnlyList<OperationalRecordSubmissionRecord>> SubmissionsAsync(SqlConnection connection, SqlTransaction? transaction, string where,
        object parameters, CancellationToken cancellationToken, bool lockRows = false)
    {
        string hint = lockRows ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd($"""
            SELECT s.Id, s.PlanId, s.AccountId, s.State, s.OutcomeReason, s.Provider, s.ExternalId, s.ExternalNumber, s.LinkVerification, s.Environment,
                s.ShortDescription, s.Description, s.Fingerprint, {_actorName} AS CreatedBy, s.CreatedAt, s.UpdatedAt
            FROM svcacct.ChangePlanRecordSubmissions s {hint} LEFT JOIN security.Users u ON u.UserId = s.CreatedBy
            WHERE {where} ORDER BY s.CreatedAt DESC, s.Id;
            SELECT e.SubmissionId, e.Event, e.FromState, e.ToState, e.ReasonCode, e.ExternalNumber, e.Reason, {_actorName} AS Actor, e.At
            FROM svcacct.ChangePlanRecordSubmissionEvents e JOIN svcacct.ChangePlanRecordSubmissions s ON s.Id = e.SubmissionId
            LEFT JOIN security.Users u ON u.UserId = e.Actor
            WHERE {where} ORDER BY e.Id;
            """, parameters, transaction, cancellationToken));
        OrSubmissionRow[] rows = [.. await grid.ReadAsync<OrSubmissionRow>()];
        ILookup<Guid, OperationalRecordEventView> events = (await grid.ReadAsync<OrEventRow>()).ToLookup(e => e.SubmissionId,
            e => new OperationalRecordEventView(e.Event, e.FromState, e.ToState, e.ReasonCode, e.ExternalNumber, e.Reason, e.Actor, e.At));
        return [.. rows.Select(r => new OperationalRecordSubmissionRecord(r.Id, r.PlanId, r.AccountId, Enum.Parse<OperationalRecordState>(r.State), r.OutcomeReason,
            r.Provider, r.ExternalId, r.ExternalNumber, r.LinkVerification, r.Environment, r.ShortDescription, r.Description, r.Fingerprint, r.CreatedBy, r.CreatedAt,
            r.UpdatedAt, [.. events[r.Id]]))];
    }

    private static Task SubmissionEventAsync(SqlConnection connection, SqlTransaction transaction, Guid submissionId, string submissionEvent,
        OperationalRecordState? from, OperationalRecordState to, string? reasonCode, string? number, string? reason, SaActor actor, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ChangePlanRecordSubmissionEvents(SubmissionId, Event, FromState, ToState, ReasonCode, ExternalNumber, Reason, Actor, At)
            VALUES(@submissionId, @submissionEvent, @from, @to, @reasonCode, @number, @reason, @UserId, @now);
            """, new { submissionId, submissionEvent, from = from?.ToString(), to = to.ToString(), reasonCode, number, reason, actor.UserId, now },
            transaction, cancellationToken));

    private sealed class OrSubmissionRow
    {
        public Guid Id { get; set; }
        public Guid PlanId { get; set; }
        public Guid AccountId { get; set; }
        public string State { get; set; } = string.Empty;
        public string? OutcomeReason { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string? ExternalId { get; set; }
        public string? ExternalNumber { get; set; }
        public string? LinkVerification { get; set; }
        public string Environment { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class OrEventRow
    {
        public Guid SubmissionId { get; set; }
        public string Event { get; set; } = string.Empty;
        public string? FromState { get; set; }
        public string ToState { get; set; } = string.Empty;
        public string? ReasonCode { get; set; }
        public string? ExternalNumber { get; set; }
        public string? Reason { get; set; }
        public string Actor { get; set; } = string.Empty;
        public DateTimeOffset At { get; set; }
    }
}
