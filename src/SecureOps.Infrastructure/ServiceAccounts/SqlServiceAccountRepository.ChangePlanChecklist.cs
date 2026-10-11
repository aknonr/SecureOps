using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>A validated plan evidence file (type, signature and size checked by the service).</summary>
public sealed record ChangePlanEvidenceFile(string FileName, string ContentType, byte[] Content, string Sha256, string? Label);

public sealed partial class SqlServiceAccountRepository
{
    // Checklist, plan evidence and completion (CHANGE-PLAN-DESIGN.md PR 2). Same pattern as every plan command: the plan row is
    // locked (UPDLOCK, HOLDLOCK), scope is re-read with HOLDLOCK, Guard answers 404/403/409, then the write, history and audit
    // happen in one transaction. A row is addressed only through the plan's current preview (otherwise 404, like missing).

    /// <summary>Checklist marks of one row, newest first (the caller has already been shown the plan and the row).</summary>
    public async Task<IReadOnlyList<ChangePlanItemCheckView>> ChangePlanItemChecksAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await RetryReadOnDeadlockAsync(async () => (IReadOnlyList<ChangePlanItemCheckView>)[.. (await connection.QueryAsync<CpCheckRow>(Cmd($"""
            SELECT c.Id, c.State, c.Note, c.OutsideWindow, {_actorName} AS CheckedBy, c.CheckedAt
            FROM svcacct.ChangeItemChecks c LEFT JOIN security.Users u ON u.UserId = c.CheckedBy WHERE c.ItemId = @itemId ORDER BY c.Id DESC;
            """, new { itemId }, null, cancellationToken))).Select(r => CheckView(r.Id, r.State, r.Note, r.OutsideWindow, r.CheckedBy, r.CheckedAt))]);
    }

    /// <summary>
    /// Records the person's statement for one row of the approved preview. Refusals in order: plan missing/out of scope (404),
    /// no Work or responsible basis (403), row not in the current preview (404), plan not approved or closed (409), the row
    /// was marked since <paramref name="expectedCheckId"/> (409 <c>checkStale</c>), nothing to roll back (409). The first
    /// mark moves Approved to InProgress (Started event); every mark stamps the plan, so its version changes.
    /// </summary>
    public async Task<SaResult<Guid>> CheckChangePlanItemAsync(Guid planId, Guid itemId, ChangePlanCheckState state, string? note, long? expectedCheckId,
        ChangePlanCaller caller, DateTimeOffset now, CancellationToken cancellationToken)
    {
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

        if (plan.Items.FirstOrDefault(i => i.ItemId == itemId) is not { } item)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        if (StateRefusal(plan, ChangePlanStatus.Approved, ChangePlanStatus.InProgress) is { } closed)
        {
            return closed;
        }

        if (item.CheckId != expectedCheckId)
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "checkStale");
        }

        if (state == ChangePlanCheckState.RolledBack && !ChangePlanRules.CanRollBack(item.State))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "nothingToRollBack");
        }

        bool outside = ChangePlanRules.IsOutsideWindow(now, plan.Approval!.WindowStart, plan.Approval.WindowEnd);
        long checkId = await connection.ExecuteScalarAsync<long>(Cmd("""
            INSERT INTO svcacct.ChangeItemChecks(ItemId, State, Note, OutsideWindow, CheckedBy, CheckedAt)
            VALUES(@itemId, @state, @note, @outside, @UserId, @now);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """, new { itemId, state = state.ToString(), note, outside, caller.Actor.UserId, now }, transaction, cancellationToken));
        await SetPlanStatusAsync(connection, transaction, planId, ChangePlanStatus.InProgress, plan.CurrentPreviewVersion, caller.Actor, now, cancellationToken);
        if (plan.Status == ChangePlanStatus.Approved)
        {
            await PlanEventAsync(connection, transaction, planId, "Started", plan.Status, ChangePlanStatus.InProgress, plan.CurrentPreviewVersion, null, caller.Actor, now,
                cancellationToken);
        }

        await HistoryAsync(connection, transaction, "ChangePlan", planId, item.AccountId, "ChangePlanItemChecked",
            new { ItemId = itemId, CheckId = checkId, State = state.ToString(), OutsideWindow = outside }, note, caller.Actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "ChangePlanItemChecked", new { PlanId = planId, ItemId = itemId, State = state.ToString(), OutsideWindow = outside },
            caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>
    /// Stores plan evidence (Approved, InProgress or Completed) in the immutable Evidence table with owner type ChangePlan.
    /// The account anchor required by CK_SaEvidence_Anchor is the plan's smallest account id; visibility comes from the plan.
    /// Before migration 034 the answer is ChangePlanEvidenceNotInstalled and nothing is written.
    /// </summary>
    public async Task<SaResult<Guid>> AddChangePlanEvidenceAsync(Guid planId, ChangePlanEvidenceFile file, ChangePlanCaller caller, CancellationToken cancellationToken)
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
        if ((Guard(plan, scope, caller.Work, null) ?? StateRefusal(plan, ChangePlanStatus.Approved, ChangePlanStatus.InProgress, ChangePlanStatus.Completed))
            is { } refused)
        {
            return refused;
        }

        if (!await PlanEvidenceInstalledAsync(connection, transaction, cancellationToken))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanEvidenceNotInstalled);
        }

        var evidenceId = Guid.NewGuid();
        await InsertEvidenceAsync(connection, transaction, evidenceId, PlanEvidenceOwner, planId, plan.Accounts.Min(a => a.View.AccountId), null, file.FileName,
            file.ContentType, file.Content, file.Sha256, file.Label, caller.Actor, now, cancellationToken);
        // Account history is readable with one account's scope: no file name or label there (those stay with the plan).
        foreach (ChangePlanAccountState account in plan.Accounts)
        {
            await HistoryAsync(connection, transaction, "ChangePlan", planId, account.View.AccountId, "ChangePlanEvidenceAdded",
                new { EvidenceId = evidenceId, Size = file.Content.Length }, null, caller.Actor, now, cancellationToken);
        }

        await AuditAsync(connection, transaction, "ChangePlanEvidenceAdded", new { PlanId = planId, EvidenceId = evidenceId }, caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>
    /// Closes an InProgress plan whose every row is marked, at <paramref name="expectedVersion"/>. For each account whose
    /// outcome is Performed it records one GmsaConversion / Performed / Intermediate action through the action table (actual
    /// time = the account's latest Done mark, linked request if it belongs to the account, the OCO number as an external
    /// record). It never verifies, never closes a request and never touches the gMSA transition record.
    /// </summary>
    public async Task<SaResult<Guid>> CompleteChangePlanAsync(Guid planId, string expectedVersion, string? note, ChangePlanCaller caller,
        CancellationToken cancellationToken)
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
        if ((Guard(plan, scope, caller.Work, expectedVersion) ?? StateRefusal(plan, ChangePlanStatus.Approved, ChangePlanStatus.InProgress)) is { } refused)
        {
            return refused;
        }

        if (plan.Status != ChangePlanStatus.InProgress || plan.Items.Any(i => i.State is null))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "checklistIncomplete");
        }

        ChangePlanApprovalView approval = plan.Approval!;
        int performed = 0;
        foreach (ChangePlanAccountState account in plan.Accounts)
        {
            Guid accountId = account.View.AccountId;
            ChangePlanItemState[] rows = [.. plan.Items.Where(i => i.AccountId == accountId)];
            ChangePlanAccountOutcome outcome = ChangePlanRules.AccountOutcome(rows.Select(r => r.State));
            Guid? actionId = null;
            if (outcome == ChangePlanAccountOutcome.Performed)
            {
                actionId = Guid.NewGuid();
                DateTimeOffset actualAt = rows.Where(r => r.State == ChangePlanCheckState.Done).Max(r => r.CheckedAt!.Value);
                DateOnly actualOn = ReportCalendar.LocalDate(actualAt);
                int done = rows.Count(r => r.State == ChangePlanCheckState.Done);
                await connection.ExecuteAsync(Cmd("""
                    INSERT INTO svcacct.ActionEvents(Id, AccountId, RequestId, ActionType, Result, RecordKind, ActualOn, ActualAt, ActualPrecision, PerformerTeamId,
                        PerformerPersonId, EvidenceNote, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                    SELECT @actionId, @accountId, (SELECT Id FROM svcacct.WorkRequests WHERE Id = @RequestId AND AccountId = @accountId), 'GmsaConversion',
                        'Performed', 'Intermediate', @actualOn, @actualAt, 'Instant', NULL, NULL, @evidenceNote, @now, @UserId, @now, @UserId;
                    """, new
                {
                    actionId,
                    accountId,
                    account.View.RequestId,
                    actualOn,
                    actualAt,
                    evidenceNote = $"Değişiklik planı kontrol listesiyle kaydedildi (plan {planId:D}, {approval.OcoNumber}): {done}/{rows.Length} bileşen " +
                        "'Yapıldı' işaretli. 'Yapıldı' kişinin beyanıdır, doğrulama değildir.",
                    now,
                    caller.Actor.UserId
                }, transaction, cancellationToken));
                await ReferencesAsync(connection, transaction, [new SaExternalRef("OCO", approval.OcoNumber)], "Action", actionId.Value, accountId, caller.Actor, now,
                    cancellationToken);
                await HistoryAsync(connection, transaction, "Action", actionId.Value, accountId, "ActionReported", new
                {
                    Type = nameof(ServiceAccountActionType.GmsaConversion),
                    Result = nameof(ServiceAccountActionResult.Performed),
                    Kind = nameof(ServiceAccountRecordKind.Intermediate),
                    ActualOn = actualOn,
                    PlanId = planId
                }, null, caller.Actor, now, cancellationToken);
                await AuditAsync(connection, transaction, "ActionReported", new { EntityType = "Action", EntityId = actionId, AccountId = accountId }, caller.Actor, now,
                    cancellationToken);
                performed++;
            }

            await HistoryAsync(connection, transaction, "ChangePlan", planId, accountId, "ChangePlanCompleted",
                new { Outcome = outcome.ToString(), ActionId = actionId }, note, caller.Actor, now, cancellationToken);
        }

        await SetPlanStatusAsync(connection, transaction, planId, ChangePlanStatus.Completed, plan.CurrentPreviewVersion, caller.Actor, now, cancellationToken);
        await PlanEventAsync(connection, transaction, planId, "Completed", plan.Status, ChangePlanStatus.Completed, plan.CurrentPreviewVersion, note, caller.Actor, now,
            cancellationToken);
        await AuditAsync(connection, transaction, "ChangePlanCompleted", new { PlanId = planId, Performed = performed, NotPerformed = plan.Accounts.Count - performed },
            caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>Evidence owner type of a plan (migration 034).</summary>
    internal const string PlanEvidenceOwner = "ChangePlan";

    /// <summary>Whether migration 034 is applied (the evidence owner check accepts ChangePlan).</summary>
    private static async Task<bool> PlanEvidenceInstalledAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner'
              AND definition LIKE N'%''ChangePlan''%';
            """, null, transaction, cancellationToken)) == 1;

    /// <summary>
    /// Null when the plan is in one of <paramref name="allowed"/>; otherwise 409 <c>planClosed</c> (Completed or Cancelled) or
    /// <c>planNotApproved</c> (Draft or Previewed).
    /// </summary>
    private static SaResult<Guid>? StateRefusal(ChangePlanSnapshot plan, params ChangePlanStatus[] allowed) =>
        allowed.Contains(plan.Status) ? null
        : SaResult<Guid>.Fail(SaErrors.ChangePlanState, plan.Status is ChangePlanStatus.Completed or ChangePlanStatus.Cancelled ? "planClosed" : "planNotApproved");

    internal static ChangePlanItemCheckView CheckView(long id, string state, string? note, bool outsideWindow, string checkedBy, DateTimeOffset checkedAt) =>
        new(id, state, ChangePlanRules.CheckStateLabel(Enum.Parse<ChangePlanCheckState>(state)), note, outsideWindow, checkedBy, checkedAt);

    private sealed class CpCheckRow
    {
        public long Id { get; set; }
        public string State { get; set; } = string.Empty;
        public string? Note { get; set; }
        public bool OutsideWindow { get; set; }
        public string CheckedBy { get; set; } = string.Empty;
        public DateTimeOffset CheckedAt { get; set; }
    }
}
