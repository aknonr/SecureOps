using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>An account currently in a plan, with the attributes that place it in the caller's scope.</summary>
public sealed record ChangePlanAccountState(ChangePlanAccountView View, AccountScopeAnchor Anchor);

/// <summary>A stored plan: everything the service needs to decide visibility and permissions and to build the view.</summary>
public sealed record ChangePlanSnapshot(Guid Id, string Kind, string Title, ChangePlanStatus Status, int? CurrentPreviewVersion, Guid CreatedById,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Version, IReadOnlyList<ChangePlanAccountState> Accounts,
    ChangePlanPreviewView? Preview, Guid? PreviewCreatedById, ChangePlanApprovalView? Approval, IReadOnlyList<ChangePlanEventView> Events,
    IReadOnlySet<Guid> Editors);

/// <summary>Who runs a plan command and which module capabilities they hold; scope is re-read inside the transaction.</summary>
public sealed record ChangePlanCaller(SaActor Actor, bool Work, bool Verify);

/// <summary>Validated, normalized approval values.</summary>
public sealed record ChangePlanApproval(int PreviewVersion, string Sha256, string OcoNumber, DateTimeOffset WindowStart, DateTimeOffset WindowEnd,
    string Reason);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>Repository-only refusal: the stored rows no longer produce the stored digest (the service logs it and answers previewStale).</summary>
    internal const string PreviewDigestMismatch = "previewDigestMismatch";

    /// <summary>Each account's latest row in the plan; Removed rows end membership (ChangePlanAccounts is append-only).</summary>
    private const string _currentPlanAccounts = """
        cur AS (
            SELECT x.AccountId, x.TargetGmsaName, x.RequestId, x.ChangedAt FROM (
                SELECT ca.AccountId, ca.Action, ca.TargetGmsaName, ca.RequestId, ca.ChangedAt,
                    ROW_NUMBER() OVER (PARTITION BY ca.AccountId ORDER BY ca.Id DESC) AS rn
                FROM svcacct.ChangePlanAccounts ca WHERE ca.PlanId = @planId) x
            WHERE x.rn = 1 AND x.Action <> 'Removed')
        """;

    private const string _actorName = "COALESCE(u.DisplayName, u.LoginName, 'Kullanıcı')";

    /// <summary>Whether migration 033 is installed on this database.</summary>
    public async Task<bool> ChangePlansInstalledAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT CASE WHEN OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NULL OR OBJECT_ID(N'svcacct.ChangePlanEvents', N'U') IS NULL THEN 0 ELSE 1 END;
            """, null, null, cancellationToken)) == 1;
    }

    /// <summary>
    /// Plans whose every current account is inside <paramref name="scope"/>, newest change first. The scope predicate is
    /// evaluated through CASE so an account it cannot place (NULL organization and owner) counts as outside, never inside.
    /// </summary>
    public async Task<ChangePlanPage> ChangePlansAsync(ServiceAccountScope scope, ChangePlanListQuery query, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        DynamicParameters parameters = ScopeParameters(scope, new
        {
            query.Status,
            query.AccountId,
            skip = (query.Page - 1) * query.PageSize,
            take = query.PageSize
        });
        string visible = $"""
            WITH live AS (
                SELECT x.PlanId, x.AccountId FROM (
                    SELECT ca.PlanId, ca.AccountId, ca.Action, ROW_NUMBER() OVER (PARTITION BY ca.PlanId, ca.AccountId ORDER BY ca.Id DESC) AS rn
                    FROM svcacct.ChangePlanAccounts ca) x
                WHERE x.rn = 1 AND x.Action <> 'Removed'),
            visible AS (
                SELECT p.Id FROM svcacct.ChangePlans p
                WHERE (@Status IS NULL OR p.Status = @Status)
                  AND (@AccountId IS NULL OR EXISTS (SELECT 1 FROM live l WHERE l.PlanId = p.Id AND l.AccountId = @AccountId))
                  AND EXISTS (SELECT 1 FROM live l WHERE l.PlanId = p.Id)
                  AND NOT EXISTS (SELECT 1 FROM live l JOIN svcacct.Accounts a ON a.Id = l.AccountId
                                  WHERE l.PlanId = p.Id AND CASE WHEN {ScopePredicate} THEN 1 ELSE 0 END = 0))
            """;
        return await RetryReadOnDeadlockAsync(async () =>
        {
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd($"""
                {visible}
                SELECT COUNT(*) FROM visible;
                {visible}
                SELECT p.Id, p.Kind, p.Title, p.Status, (SELECT COUNT(*) FROM live l WHERE l.PlanId = p.Id) AS AccountCount, p.CurrentPreviewVersion,
                    ap.OcoNumber, ap.WindowStart, ap.WindowEnd, {_actorName} AS CreatedBy, p.CreatedAt, p.UpdatedAt
                FROM visible v JOIN svcacct.ChangePlans p ON p.Id = v.Id
                LEFT JOIN svcacct.ChangePlanPreviews pv ON pv.PlanId = p.Id AND pv.Version = p.CurrentPreviewVersion
                LEFT JOIN svcacct.ChangePlanApprovals ap ON ap.PreviewId = pv.Id
                LEFT JOIN security.Users u ON u.UserId = p.CreatedBy
                ORDER BY p.UpdatedAt DESC, p.Id OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
                """, parameters, null, cancellationToken));
            int total = await grid.ReadSingleAsync<int>();
            ChangePlanListItem[] items = [.. (await grid.ReadAsync<CpListRow>()).Select(r => new ChangePlanListItem(r.Id, r.Kind, r.Title, r.Status,
                ChangePlanRules.StatusLabel(Enum.Parse<ChangePlanStatus>(r.Status)), r.AccountCount, r.CurrentPreviewVersion, r.OcoNumber, r.WindowStart,
                r.WindowEnd, r.CreatedBy, r.CreatedAt, r.UpdatedAt))];
            return new ChangePlanPage(items, total, query.Page, query.PageSize);
        });
    }

    /// <summary>The stored plan, or null when it does not exist (the service decides visibility from the account anchors).</summary>
    public async Task<ChangePlanSnapshot?> ChangePlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await RetryReadOnDeadlockAsync(() => SnapshotAsync(connection, null, planId, false, cancellationToken));
    }

    /// <summary>Rows of the plan's current preview (<paramref name="previewVersion"/>), page by page.</summary>
    public async Task<ChangePlanItemPage> ChangePlanItemsAsync(Guid planId, int previewVersion, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await RetryReadOnDeadlockAsync(async () =>
        {
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
                SELECT COUNT(*) FROM svcacct.ChangePlanItems i JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId
                WHERE v.PlanId = @planId AND v.Version = @previewVersion;
                SELECT i.Id, i.AccountId, a.AccountName, i.ServerName, i.ComponentType, i.ComponentName, i.CurrentIdentity, i.TargetIdentity, i.Flag, i.ScanAt
                FROM svcacct.ChangePlanItems i JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId JOIN svcacct.Accounts a ON a.Id = i.AccountId
                WHERE v.PlanId = @planId AND v.Version = @previewVersion
                ORDER BY a.AccountName, i.AccountId, i.ServerName, i.ComponentType, i.ComponentName, i.Id
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
                """, new { planId, previewVersion, skip = (page - 1) * pageSize, take = pageSize }, null, cancellationToken));
            int total = await grid.ReadSingleAsync<int>();
            ChangePlanItemView[] items = [.. (await grid.ReadAsync<CpItemRow>()).Select(r =>
            {
                ChangePlanFlag flag = Enum.Parse<ChangePlanFlag>(r.Flag);
                return new ChangePlanItemView(r.Id, r.AccountId, r.AccountName, r.ServerName, r.ComponentType, r.ComponentName, r.CurrentIdentity, r.TargetIdentity,
                    r.Flag, ChangePlanRules.FlagLabel(flag), r.ScanAt);
            })];
            return new ChangePlanItemPage(previewVersion, items, total, page, pageSize);
        });
    }

    /// <summary>
    /// Creates a Draft plan. Inside one write transaction the requested accounts are locked (UPDLOCK, HOLDLOCK, in a fixed
    /// order), the caller's scope is re-read with HOLDLOCK, and every account is answered on its own: responsible basis,
    /// 031 name rule, an open gMSA request of the same account, and not already in another open plan (owner decision 6).
    /// Any refusal refuses the whole request and nothing is written; the per-account answers come back in <c>Current</c>.
    /// </summary>
    public async Task<SaResult<Guid>> CreateChangePlanAsync(string title, IReadOnlyList<ChangePlanAccountInput> accounts, ChangePlanCaller caller,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        Guid[] ids = [.. accounts.Select(a => a.AccountId).Distinct()];
        await LockAccountsAsync(connection, transaction, ids, cancellationToken);
        ServiceAccountScope scope = await LockedScopeAsync(connection, transaction, caller.Actor.UserId, cancellationToken);
        ChangePlanAccountResult[] results = await EvaluateAsync(connection, transaction, Guid.Empty, accounts, _ => true, scope, cancellationToken);
        if (results.Any(r => r.Outcome != _accepted))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanAccountsRefused, "accounts", results);
        }

        var planId = Guid.NewGuid();
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ChangePlans(Id, Kind, Title, Status, CurrentPreviewVersion, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
            VALUES(@planId, 'GmsaConversion', @title, 'Draft', NULL, @UserId, @now, @UserId, @now);
            """, new { planId, title, caller.Actor.UserId, now }, transaction, cancellationToken));
        foreach (ChangePlanAccountInput account in accounts)
        {
            await InsertPlanAccountAsync(connection, transaction, planId, account with { TargetGmsaName = ChangePlanRules.TargetName(account.TargetGmsaName)! },
                "Added", caller.Actor, now, cancellationToken);
            await HistoryAsync(connection, transaction, "ChangePlan", planId, account.AccountId, "ChangePlanCreated", new { Status = "Draft" }, null,
                caller.Actor, now, cancellationToken);
        }

        await PlanEventAsync(connection, transaction, planId, "Created", null, ChangePlanStatus.Draft, null, null, caller.Actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "ChangePlanCreated", new { PlanId = planId, Accounts = accounts.Count }, caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>
    /// Replaces the plan's account list at <paramref name="expectedVersion"/> (Draft or Previewed). The plan row is locked
    /// first, then the affected accounts, then scope is re-read; every change is a new append-only row and a real change
    /// returns the plan to Draft without a current preview. An identical list writes nothing.
    /// </summary>
    public async Task<SaResult<Guid>> UpdateChangePlanAsync(Guid planId, string expectedVersion, IReadOnlyList<ChangePlanAccountInput> accounts,
        ChangePlanCaller caller, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        ChangePlanSnapshot? plan = await SnapshotAsync(connection, transaction, planId, true, cancellationToken);
        if (plan is null)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        var current = plan.Accounts.ToDictionary(a => a.View.AccountId, a => a.View);
        await LockAccountsAsync(connection, transaction, [.. accounts.Select(a => a.AccountId).Union(current.Keys).Distinct()], cancellationToken);
        ServiceAccountScope scope = await LockedScopeAsync(connection, transaction, caller.Actor.UserId, cancellationToken);
        if (Guard(plan, scope, caller.Work, expectedVersion) is { } refused)
        {
            return refused;
        }

        if (plan.Status is not (ChangePlanStatus.Draft or ChangePlanStatus.Previewed))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, ChangePlanRules.IsOpen(plan.Status) ? "planNotEditable" : "planClosed");
        }

        bool Changed(ChangePlanAccountInput input) => !current.TryGetValue(input.AccountId, out ChangePlanAccountView? existing)
            || !string.Equals(existing.TargetGmsaName, ChangePlanRules.TargetName(input.TargetGmsaName), StringComparison.Ordinal)
            || existing.RequestId != input.RequestId;
        ChangePlanAccountResult[] results = await EvaluateAsync(connection, transaction, planId, accounts, Changed, scope, cancellationToken);
        if (results.Any(r => r.Outcome != _accepted))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanAccountsRefused, "accounts", results);
        }

        HashSet<Guid> desired = [.. accounts.Select(a => a.AccountId)];
        List<(ChangePlanAccountInput Input, string Action)> changes = [.. accounts.Where(Changed)
            .Select(a => (a with { TargetGmsaName = ChangePlanRules.TargetName(a.TargetGmsaName)! }, current.ContainsKey(a.AccountId) ? "Renamed" : "Added"))];
        changes.AddRange(current.Values.Where(a => !desired.Contains(a.AccountId))
            .Select(a => (new ChangePlanAccountInput(a.AccountId, a.TargetGmsaName, a.RequestId), "Removed")));
        if (changes.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return planId;
        }

        foreach ((ChangePlanAccountInput input, string action) in changes)
        {
            await InsertPlanAccountAsync(connection, transaction, planId, input, action, caller.Actor, now, cancellationToken);
            await HistoryAsync(connection, transaction, "ChangePlan", planId, input.AccountId, "ChangePlanAccount" + action, new { Status = "Draft" }, null,
                caller.Actor, now, cancellationToken);
        }

        await SetPlanStatusAsync(connection, transaction, planId, ChangePlanStatus.Draft, null, caller.Actor, now, cancellationToken);
        await PlanEventAsync(connection, transaction, planId, "Updated", plan.Status, ChangePlanStatus.Draft, null, null, caller.Actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "ChangePlanUpdated", new
        {
            PlanId = planId,
            Added = changes.Count(c => c.Action == "Added"),
            Removed = changes.Count(c => c.Action == "Removed"),
            Renamed = changes.Count(c => c.Action == "Renamed")
        }, caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>
    /// Builds a new preview version from each current account's latest Discovery scan (Draft or Previewed) and stores its
    /// rows with the server-computed digest. The plan becomes Previewed with this version current.
    /// </summary>
    public async Task<SaResult<Guid>> PreviewChangePlanAsync(Guid planId, string expectedVersion, ChangePlanCaller caller, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        ChangePlanSnapshot? plan = await SnapshotAsync(connection, transaction, planId, true, cancellationToken);
        if (plan is null)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        ServiceAccountScope scope = await LockedScopeAsync(connection, transaction, caller.Actor.UserId, cancellationToken);
        if (Guard(plan, scope, caller.Work, expectedVersion) is { } refused)
        {
            return refused;
        }

        if (plan.Status is not (ChangePlanStatus.Draft or ChangePlanStatus.Previewed))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, ChangePlanRules.IsOpen(plan.Status) ? "planNotEditable" : "planClosed");
        }

        IReadOnlyList<ChangePlanPreviewRow> rows = ChangePlanRules.BuildPreview(
            await PreviewSourcesAsync(connection, transaction, plan.Accounts.Select(a => a.View).ToArray(), cancellationToken), now);
        string sha = ChangePlanRules.Digest(rows);
        var previewId = Guid.NewGuid();
        int version = await connection.ExecuteScalarAsync<int>(Cmd("""
            DECLARE @version int = 1 + ISNULL((SELECT MAX(Version) FROM svcacct.ChangePlanPreviews WHERE PlanId = @planId), 0);
            INSERT INTO svcacct.ChangePlanPreviews(Id, PlanId, Version, Sha256, ScanFreshDays, ItemCount, CreatedBy, CreatedAt)
            VALUES(@previewId, @planId, @version, @sha, @fresh, @count, @UserId, @now);
            SELECT @version;
            """, new { planId, previewId, sha, fresh = ChangePlanRules.ScanFreshDays, count = rows.Count, caller.Actor.UserId, now }, transaction, cancellationToken));
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ChangePlanItems(Id, PreviewId, AccountId, ScanLinkId, ServerName, ComponentType, ComponentName, CurrentIdentity, TargetIdentity, Flag, ScanAt)
            VALUES(NEWID(), @previewId, @AccountId, @ScanLinkId, @ServerName, @ComponentType, @ComponentName, @CurrentIdentity, @TargetIdentity, @Flag, @ScanAt);
            """, rows.Select(r => new
        {
            previewId,
            r.AccountId,
            r.ScanLinkId,
            r.ServerName,
            r.ComponentType,
            r.ComponentName,
            r.CurrentIdentity,
            r.TargetIdentity,
            Flag = r.Flag.ToString(),
            r.ScanAt
        }), transaction, cancellationToken));
        await SetPlanStatusAsync(connection, transaction, planId, ChangePlanStatus.Previewed, version, caller.Actor, now, cancellationToken);
        await PlanEventAsync(connection, transaction, planId, "Previewed", plan.Status, ChangePlanStatus.Previewed, version, null, caller.Actor, now, cancellationToken);
        foreach (ChangePlanAccountState account in plan.Accounts)
        {
            await HistoryAsync(connection, transaction, "ChangePlan", planId, account.View.AccountId, "ChangePlanPreviewed", new { PreviewVersion = version }, null,
                caller.Actor, now, cancellationToken);
        }

        await AuditAsync(connection, transaction, "ChangePlanPreviewed", new { PlanId = planId, PreviewVersion = version, Items = rows.Count }, caller.Actor, now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>
    /// Approves the plan's current preview (design T1–T3, T7). With the plan row locked: scope re-read (outside = 404), Verify
    /// on every account (403), approver separation (403 <c>approverIsPlanner</c> / <c>approverChangedPlan</c>), status
    /// Previewed (409 <c>planNotPreviewed</c>), the requested version and digest are the current ones (409
    /// <c>previewStale</c>) and the stored rows still produce the stored digest. Nothing is written on any refusal; the
    /// unique preview index and the separation trigger refuse the same again in the database.
    /// </summary>
    public async Task<SaResult<Guid>> ApproveChangePlanAsync(Guid planId, ChangePlanApproval approval, ChangePlanCaller caller, CancellationToken cancellationToken)
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
        if (Guard(plan, scope, caller.Verify, null) is { } refused)
        {
            return refused;
        }

        if (ChangePlanRules.ApproverRefusal(caller.Actor.UserId, plan.CreatedById, plan.PreviewCreatedById ?? Guid.Empty, plan.Editors) is { } separation)
        {
            return SaResult<Guid>.Fail(SaErrors.Forbidden, separation);
        }

        if (plan.Status != ChangePlanStatus.Previewed || plan.Preview is not { } preview)
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "planNotPreviewed");
        }

        if (preview.Version != approval.PreviewVersion || !string.Equals(preview.Sha256, approval.Sha256, StringComparison.Ordinal))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "previewStale");
        }

        (Guid previewId, ChangePlanPreviewRow[] stored) = await StoredPreviewAsync(connection, transaction, planId, preview.Version, cancellationToken);
        if (!string.Equals(ChangePlanRules.Digest(stored), preview.Sha256, StringComparison.Ordinal))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, PreviewDigestMismatch);
        }

        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ChangePlanApprovals(Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy, ApprovedAt)
            VALUES(NEWID(), @planId, @previewId, @Sha256, @OcoNumber, @WindowStart, @WindowEnd, @Reason, @UserId, @now);
            """, new
        {
            planId,
            previewId,
            approval.Sha256,
            approval.OcoNumber,
            approval.WindowStart,
            approval.WindowEnd,
            approval.Reason,
            caller.Actor.UserId,
            now
        }, transaction, cancellationToken));
        await SetPlanStatusAsync(connection, transaction, planId, ChangePlanStatus.Approved, preview.Version, caller.Actor, now, cancellationToken);
        await PlanEventAsync(connection, transaction, planId, "Approved", plan.Status, ChangePlanStatus.Approved, preview.Version, approval.Reason, caller.Actor, now,
            cancellationToken);
        foreach (ChangePlanAccountState account in plan.Accounts)
        {
            await HistoryAsync(connection, transaction, "ChangePlan", planId, account.View.AccountId, "ChangePlanApproved", new { PreviewVersion = preview.Version },
                approval.Reason, caller.Actor, now, cancellationToken);
        }

        await AuditAsync(connection, transaction, "ChangePlanApproved", new { PlanId = planId, PreviewVersion = preview.Version }, caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>
    /// Cancels an open plan with a reason at <paramref name="expectedVersion"/>: the planner with Work, or anyone with Verify,
    /// in both cases responsible for every account. Every record is kept.
    /// </summary>
    public async Task<SaResult<Guid>> CancelChangePlanAsync(Guid planId, string expectedVersion, string reason, ChangePlanCaller caller,
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
        if (Guard(plan, scope, MayCancel(plan, caller), expectedVersion) is { } refused)
        {
            return refused;
        }

        if (!ChangePlanRules.IsOpen(plan.Status))
        {
            return SaResult<Guid>.Fail(SaErrors.ChangePlanState, "planClosed");
        }

        await SetPlanStatusAsync(connection, transaction, planId, ChangePlanStatus.Cancelled, plan.CurrentPreviewVersion, caller.Actor, now, cancellationToken);
        await PlanEventAsync(connection, transaction, planId, "Cancelled", plan.Status, ChangePlanStatus.Cancelled, plan.CurrentPreviewVersion, reason, caller.Actor,
            now, cancellationToken);
        foreach (ChangePlanAccountState account in plan.Accounts)
        {
            await HistoryAsync(connection, transaction, "ChangePlan", planId, account.View.AccountId, "ChangePlanCancelled", new { From = plan.Status.ToString() },
                reason, caller.Actor, now, cancellationToken);
        }

        await AuditAsync(connection, transaction, "ChangePlanCancelled", new { PlanId = planId, From = plan.Status.ToString() }, caller.Actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    /// <summary>The planner with Work, or a verifier.</summary>
    internal static bool MayCancel(ChangePlanSnapshot plan, ChangePlanCaller caller) =>
        caller.Verify || caller.Work && plan.CreatedById == caller.Actor.UserId;

    /// <summary>Organization-level scope or the confirmed owner team: may plan the whole account (not only through a request).</summary>
    internal static bool PlanResponsible(ServiceAccountScope scope, AccountScopeAnchor anchor) =>
        scope.CoversAtOrganizationLevel(anchor) || scope.CoversTeam(anchor.OwnerTeamId);

    /// <summary>
    /// Shared refusal order for a locked plan: an account outside scope makes the whole plan NotFound (indistinguishable from
    /// missing); without the capability or the responsible basis on every account it is Forbidden; a stale expected version
    /// is Conflict. Null when the command may continue.
    /// </summary>
    private static SaResult<Guid>? Guard(ChangePlanSnapshot plan, ServiceAccountScope scope, bool allowed, string? expectedVersion)
    {
        if (plan.Accounts.Count == 0 || plan.Accounts.Any(a => !scope.Covers(a.Anchor)))
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound);
        }

        if (!allowed || plan.Accounts.Any(a => !PlanResponsible(scope, a.Anchor)))
        {
            return SaResult<Guid>.Fail(SaErrors.Forbidden, "scope");
        }

        return expectedVersion is not null && !Version(expectedVersion).AsSpan().SequenceEqual(Version(plan.Version))
            ? SaResult<Guid>.Fail(SaErrors.Conflict, "expectedVersion")
            : null;
    }

    private const string _accepted = "Accepted";

    /// <summary>
    /// Answers every requested account in order (all accounts are checked; <paramref name="checkChange"/> limits the name,
    /// request and open-plan checks to new or changed accounts). Outside scope and missing look the same and carry no name.
    /// </summary>
    private static async Task<ChangePlanAccountResult[]> EvaluateAsync(SqlConnection connection, SqlTransaction transaction, Guid planId,
        IReadOnlyList<ChangePlanAccountInput> accounts, Func<ChangePlanAccountInput, bool> checkChange, ServiceAccountScope scope,
        CancellationToken cancellationToken)
    {
        Guid[] ids = [.. accounts.Select(a => a.AccountId).Distinct()];
        Dictionary<Guid, (AccountScopeAnchor Anchor, string Name, string? Domain)> anchors = await AnchorsAsync(connection, transaction, ids, cancellationToken);
        string json = JsonSerializer.Serialize(ids.Select(i => i.ToString("D")));
        HashSet<Guid> inOpenPlan = [.. await connection.QueryAsync<Guid>(Cmd("""
            SELECT DISTINCT x.AccountId FROM (
                SELECT ca.PlanId, ca.AccountId, ca.Action, ROW_NUMBER() OVER (PARTITION BY ca.PlanId, ca.AccountId ORDER BY ca.Id DESC) AS rn
                FROM svcacct.ChangePlanAccounts ca WHERE ca.AccountId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@json))) x
            JOIN svcacct.ChangePlans p ON p.Id = x.PlanId
            WHERE x.rn = 1 AND x.Action <> 'Removed' AND p.Status NOT IN ('Completed','Cancelled') AND p.Id <> @planId;
            """, new { json, planId }, transaction, cancellationToken))];
        Guid[] requestIds = [.. accounts.Where(a => a.RequestId is not null).Select(a => a.RequestId!.Value).Distinct()];
        Dictionary<Guid, Guid> openGmsaRequests = requestIds.Length == 0 ? [] : (await connection.QueryAsync<(Guid Id, Guid AccountId)>(Cmd("""
            SELECT Id, AccountId FROM svcacct.WorkRequests
            WHERE Id IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@requests)) AND Status = 'Open'
              AND ActionType IN ('GmsaConversion','GmsaHandover');
            """, new { requests = JsonSerializer.Serialize(requestIds.Select(i => i.ToString("D"))) }, transaction, cancellationToken)))
            .ToDictionary(r => r.Id, r => r.AccountId);

        HashSet<Guid> seen = [];
        List<ChangePlanAccountResult> results = [];
        foreach (ChangePlanAccountInput input in accounts)
        {
            bool visible = anchors.TryGetValue(input.AccountId, out (AccountScopeAnchor Anchor, string Name, string? Domain) known) && scope.Covers(known.Anchor);
            string outcome = !seen.Add(input.AccountId) ? "Duplicate"
                : !visible || !PlanResponsible(scope, known.Anchor) ? "Unavailable"
                : !checkChange(input) ? _accepted
                : ChangePlanRules.TargetName(input.TargetGmsaName) is null ? "InvalidName"
                : input.RequestId is { } request && (!openGmsaRequests.TryGetValue(request, out Guid owner) || owner != input.AccountId) ? "InvalidRequest"
                : inOpenPlan.Contains(input.AccountId) ? "InOpenPlan"
                : _accepted;
            results.Add(new ChangePlanAccountResult(input.AccountId, visible ? known.Name : null, outcome, OutcomeLabel(outcome)));
        }

        return [.. results];
    }

    private static string OutcomeLabel(string outcome) => outcome switch
    {
        _accepted => "Plana alınabilir",
        "Unavailable" => "Bulunamadı veya bu hesabı planlama yetkiniz yok",
        "InOpenPlan" => "Hesap başka bir açık planda; o plan kapanmadan veya iptal edilmeden eklenemez",
        "InvalidName" => $"gMSA adı geçersiz: en çok {ServiceAccountGmsaName.Limit} karakter (alan adı öneki ve sondaki $ sayılmaz)",
        "InvalidRequest" => "Bağlanan talep bu hesabın açık gMSA talebi değil",
        _ => "Hesap istekte birden fazla kez geçiyor"
    };

    /// <summary>Locks the account rows one by one in a fixed order (UPDLOCK, HOLDLOCK) so two plan writers never cross.</summary>
    private static async Task LockAccountsAsync(SqlConnection connection, SqlTransaction transaction, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        foreach (Guid id in ids.Distinct().Order())
        {
            await connection.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM svcacct.Accounts WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id;", new { id },
                transaction, cancellationToken));
        }
    }

    /// <summary>The caller's active grants and the organization/team tree, read with HOLDLOCK inside the write transaction.</summary>
    private static async Task<ServiceAccountScope> LockedScopeAsync(SqlConnection connection, SqlTransaction transaction, Guid userId, CancellationToken cancellationToken)
    {
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT ScopeKind, OrganizationId, TeamId FROM svcacct.ScopeGrants WITH (HOLDLOCK) WHERE UserId = @userId AND RevokedAt IS NULL;
            SELECT Id, ParentId FROM svcacct.Organizations WITH (HOLDLOCK);
            SELECT Id, OrganizationId FROM svcacct.Teams WITH (HOLDLOCK);
            """, new { userId }, transaction, cancellationToken));
        ScopeGrant[] grants = [.. (await grid.ReadAsync<(string Kind, Guid? OrganizationId, Guid? TeamId)>())
            .Select(g => new ScopeGrant(Enum.Parse<ScopeKind>(g.Kind), g.OrganizationId, g.TeamId))];
        OrganizationNode[] orgs = [.. (await grid.ReadAsync<(Guid Id, Guid? ParentId)>()).Select(o => new OrganizationNode(o.Id, o.ParentId))];
        TeamNode[] teams = [.. (await grid.ReadAsync<(Guid Id, Guid? OrganizationId)>()).Select(t => new TeamNode(t.Id, t.OrganizationId))];
        return ServiceAccountScope.Resolve(grants, orgs, teams);
    }

    /// <summary>Scope anchors, names and domains of several accounts (missing accounts are absent).</summary>
    private static async Task<Dictionary<Guid, (AccountScopeAnchor Anchor, string Name, string? Domain)>> AnchorsAsync(SqlConnection connection,
        SqlTransaction? transaction, Guid[] ids, CancellationToken cancellationToken)
    {
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT Id, ReportOrganizationId, CurrentOwnerTeamId, AccountName, Domain FROM svcacct.Accounts
            WHERE Id IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@json));
            SELECT AccountId, TargetTeamId FROM svcacct.WorkRequests
            WHERE AccountId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@json)) AND Status = 'Open' AND TargetTeamId IS NOT NULL;
            SELECT AccountId, TargetTeamId FROM svcacct.Handovers
            WHERE AccountId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@json)) AND Status IN ('Proposed','Accepted');
            """, new { json = JsonSerializer.Serialize(ids.Select(i => i.ToString("D"))) }, transaction, cancellationToken));
        (Guid Id, Guid? Org, Guid? Owner, string Name, string? Domain)[] accounts = (await grid.ReadAsync<(Guid Id, Guid? Org, Guid? Owner, string Name, string? Domain)>()).ToArray();
        ILookup<Guid, Guid> requests = (await grid.ReadAsync<(Guid AccountId, Guid TeamId)>()).ToLookup(r => r.AccountId, r => r.TeamId);
        ILookup<Guid, Guid> handovers = (await grid.ReadAsync<(Guid AccountId, Guid TeamId)>()).ToLookup(r => r.AccountId, r => r.TeamId);
        return accounts.ToDictionary(a => a.Id, a => (new AccountScopeAnchor(a.Org, a.Owner, [.. requests[a.Id]], [.. handovers[a.Id]]), a.Name, a.Domain));
    }

    /// <summary>Reads the stored plan; <paramref name="lockPlan"/> takes the plan row with UPDLOCK, HOLDLOCK first.</summary>
    private static async Task<ChangePlanSnapshot?> SnapshotAsync(SqlConnection connection, SqlTransaction? transaction, Guid planId, bool lockPlan,
        CancellationToken cancellationToken)
    {
        string hint = lockPlan ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        CpPlanRow? plan;
        ChangePlanAccountView[] accounts;
        CpPreviewRow? preview;
        Dictionary<string, int> flags;
        ChangePlanApprovalView? approval;
        CpEventRow[] events;
        using (SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd($"""
            SELECT p.Id, p.Kind, p.Title, p.Status, p.CurrentPreviewVersion, p.CreatedBy AS CreatedById, {_actorName} AS CreatedBy, p.CreatedAt, p.UpdatedAt, p.RowVer
            FROM svcacct.ChangePlans p {hint} LEFT JOIN security.Users u ON u.UserId = p.CreatedBy WHERE p.Id = @planId;
            WITH {_currentPlanAccounts}
            SELECT c.AccountId, a.AccountName, a.Domain, c.TargetGmsaName, c.RequestId, c.ChangedAt
            FROM cur c JOIN svcacct.Accounts a ON a.Id = c.AccountId ORDER BY a.AccountName, c.AccountId;
            SELECT v.Version, v.Sha256, v.ScanFreshDays, v.ItemCount, v.CreatedBy AS CreatedById, {_actorName} AS CreatedBy, v.CreatedAt
            FROM svcacct.ChangePlanPreviews v JOIN svcacct.ChangePlans p ON p.Id = v.PlanId AND p.CurrentPreviewVersion = v.Version
            LEFT JOIN security.Users u ON u.UserId = v.CreatedBy WHERE v.PlanId = @planId;
            SELECT i.Flag, COUNT(*) FROM svcacct.ChangePlanItems i JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId
            JOIN svcacct.ChangePlans p ON p.Id = v.PlanId AND p.CurrentPreviewVersion = v.Version WHERE v.PlanId = @planId GROUP BY i.Flag;
            SELECT v.Version AS PreviewVersion, ap.Sha256, ap.OcoNumber, ap.WindowStart, ap.WindowEnd, ap.Reason, {_actorName} AS ApprovedBy, ap.ApprovedAt
            FROM svcacct.ChangePlanApprovals ap JOIN svcacct.ChangePlanPreviews v ON v.Id = ap.PreviewId
            JOIN svcacct.ChangePlans p ON p.Id = v.PlanId AND p.CurrentPreviewVersion = v.Version
            LEFT JOIN security.Users u ON u.UserId = ap.ApprovedBy WHERE ap.PlanId = @planId;
            SELECT e.Event, e.FromStatus, e.ToStatus, e.PreviewVersion, e.Reason, {_actorName} AS Actor, e.At, e.Actor AS ActorId
            FROM svcacct.ChangePlanEvents e LEFT JOIN security.Users u ON u.UserId = e.Actor WHERE e.PlanId = @planId ORDER BY e.Id;
            """, new { planId }, transaction, cancellationToken)))
        {
            plan = await grid.ReadSingleOrDefaultAsync<CpPlanRow>();
            if (plan is null)
            {
                return null;
            }

            accounts = [.. await grid.ReadAsync<ChangePlanAccountView>()];
            preview = await grid.ReadSingleOrDefaultAsync<CpPreviewRow>();
            flags = (await grid.ReadAsync<(string Flag, int Count)>()).ToDictionary(f => f.Flag, f => f.Count, StringComparer.Ordinal);
            approval = await grid.ReadSingleOrDefaultAsync<ChangePlanApprovalView>();
            events = [.. await grid.ReadAsync<CpEventRow>()];
        }

        Dictionary<Guid, (AccountScopeAnchor Anchor, string Name, string? Domain)> anchors =
            await AnchorsAsync(connection, transaction, [.. accounts.Select(a => a.AccountId)], cancellationToken);
        int Count(ChangePlanFlag flag) => flags.GetValueOrDefault(flag.ToString());
        return new ChangePlanSnapshot(plan.Id, plan.Kind, plan.Title, Enum.Parse<ChangePlanStatus>(plan.Status), plan.CurrentPreviewVersion, plan.CreatedById,
            plan.CreatedBy, plan.CreatedAt, plan.UpdatedAt, Version(plan.RowVer),
            [.. accounts.Select(a => new ChangePlanAccountState(a, anchors[a.AccountId].Anchor))],
            preview is null ? null : new ChangePlanPreviewView(preview.Version, preview.Sha256, preview.ScanFreshDays, preview.ItemCount,
                new ChangePlanFlagCounts(Count(ChangePlanFlag.Ok), Count(ChangePlanFlag.StaleScan), Count(ChangePlanFlag.NoScan), Count(ChangePlanFlag.NotCovered),
                    Count(ChangePlanFlag.ManualOnly), Count(ChangePlanFlag.NameTooLong), Count(ChangePlanFlag.NothingFound)),
                preview.CreatedBy, preview.CreatedAt),
            preview?.CreatedById, approval,
            [.. events.Select(e => new ChangePlanEventView(e.Event, e.FromStatus, e.ToStatus, e.PreviewVersion, e.Reason, e.Actor, e.At))],
            events.Where(e => e.Event == "Updated").Select(e => e.ActorId).ToHashSet());
    }

    /// <summary>Each account's latest Discovery scan (by scan time, then link time) with its planned servers and its own matched components.</summary>
    private static async Task<ChangePlanPreviewAccount[]> PreviewSourcesAsync(SqlConnection connection, SqlTransaction transaction,
        ChangePlanAccountView[] accounts, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(accounts.Select(a => a.AccountId.ToString("D")));
        (Guid AccountId, Guid LinkId, Guid ScanId, string MatchedAccount, DateTimeOffset ScanAt)[] links = [.. await connection.QueryAsync<(Guid, Guid, Guid, string, DateTimeOffset)>(Cmd("""
            SELECT x.AccountId, x.LinkId, x.ScanId, x.MatchedAccount, x.ScanAt FROM (
                SELECT l.AccountId, l.Id AS LinkId, l.ScanId, l.MatchedAccount, COALESCE(s.LastScannedAt, s.CombinedAt) AS ScanAt,
                    ROW_NUMBER() OVER (PARTITION BY l.AccountId ORDER BY COALESCE(s.LastScannedAt, s.CombinedAt) DESC, l.LinkedAt DESC, l.Id) AS rn
                FROM svcacct.UsageScanLinks l JOIN svcacct.UsageScans s ON s.Id = l.ScanId
                WHERE s.Purpose = 'Discovery' AND l.AccountId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@json))) x
            WHERE x.rn = 1;
            """, new { json }, transaction, cancellationToken))];
        string scans = JsonSerializer.Serialize(links.Select(l => l.ScanId).Distinct().Select(i => i.ToString("D")));
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT ScanId, ServerName, Result FROM svcacct.UsageScanServers WHERE ScanId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@scans));
            SELECT ScanId, ServerName, ComponentType, ComponentName, ConfiguredIdentity, MatchedAccount FROM svcacct.UsageScanItems
            WHERE Role = 'Former' AND ScanId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@scans));
            """, new { scans }, transaction, cancellationToken));
        ILookup<Guid, ChangePlanScanServer> servers = (await grid.ReadAsync<(Guid ScanId, string ServerName, string Result)>())
            .ToLookup(s => s.ScanId, s => new ChangePlanScanServer(s.ServerName, s.Result));
        (Guid ScanId, string ServerName, string ComponentType, string ComponentName, string Identity, string Matched)[] items = (await grid.ReadAsync<(Guid ScanId, string ServerName, string ComponentType, string ComponentName, string Identity, string Matched)>()).ToArray();
        return [.. accounts.Select(a =>
        {
            ChangePlanScanSource? source = links.Where(l => l.AccountId == a.AccountId).Select(l => new ChangePlanScanSource(l.LinkId, l.ScanAt, [.. servers[l.ScanId]],
                [.. items.Where(i => i.ScanId == l.ScanId && string.Equals(i.Matched, l.MatchedAccount, StringComparison.OrdinalIgnoreCase))
                    .Select(i => new ChangePlanScanComponent(i.ServerName, i.ComponentType, i.ComponentName, i.Identity))])).SingleOrDefault();
            return new ChangePlanPreviewAccount(a.AccountId, a.TargetGmsaName, source);
        })];
    }

    /// <summary>The stored rows of one preview version (for re-computing its digest) and the preview id.</summary>
    private static async Task<(Guid PreviewId, ChangePlanPreviewRow[] Rows)> StoredPreviewAsync(SqlConnection connection, SqlTransaction transaction, Guid planId,
        int version, CancellationToken cancellationToken)
    {
        Guid previewId = await connection.ExecuteScalarAsync<Guid>(Cmd("SELECT Id FROM svcacct.ChangePlanPreviews WHERE PlanId = @planId AND Version = @version;",
            new { planId, version }, transaction, cancellationToken));
        CpStoredItemRow[] rows = [.. await connection.QueryAsync<CpStoredItemRow>(Cmd("""
            SELECT AccountId, ScanLinkId, ServerName, ComponentType, ComponentName, CurrentIdentity, TargetIdentity, Flag, ScanAt
            FROM svcacct.ChangePlanItems WHERE PreviewId = @previewId;
            """, new { previewId }, transaction, cancellationToken))];
        return (previewId, [.. rows.Select(r => new ChangePlanPreviewRow(r.AccountId, r.ScanLinkId, r.ServerName, r.ComponentType, r.ComponentName,
            r.CurrentIdentity, r.TargetIdentity, Enum.Parse<ChangePlanFlag>(r.Flag), r.ScanAt))]);
    }

    private static Task InsertPlanAccountAsync(SqlConnection connection, SqlTransaction transaction, Guid planId, ChangePlanAccountInput account, string action,
        SaActor actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ChangePlanAccounts(PlanId, AccountId, Action, TargetGmsaName, RequestId, ChangedBy, ChangedAt)
            VALUES(@planId, @AccountId, @action, @TargetGmsaName, @RequestId, @UserId, @now);
            """, new { planId, account.AccountId, action, account.TargetGmsaName, account.RequestId, actor.UserId, now }, transaction, cancellationToken));

    private static Task SetPlanStatusAsync(SqlConnection connection, SqlTransaction transaction, Guid planId, ChangePlanStatus status, int? previewVersion,
        SaActor actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.ChangePlans SET Status = @status, CurrentPreviewVersion = @previewVersion, UpdatedBy = @UserId, UpdatedAt = @now WHERE Id = @planId;
            """, new { planId, status = status.ToString(), previewVersion, actor.UserId, now }, transaction, cancellationToken));

    private static Task PlanEventAsync(SqlConnection connection, SqlTransaction transaction, Guid planId, string planEvent, ChangePlanStatus? from,
        ChangePlanStatus to, int? previewVersion, string? reason, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ChangePlanEvents(PlanId, Event, FromStatus, ToStatus, PreviewVersion, Reason, Actor, At)
            VALUES(@planId, @planEvent, @from, @to, @previewVersion, @reason, @UserId, @now);
            """, new { planId, planEvent, from = from?.ToString(), to = to.ToString(), previewVersion, reason, actor.UserId, now }, transaction, cancellationToken));

    private sealed class CpPlanRow
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? CurrentPreviewVersion { get; set; }
        public Guid CreatedById { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public byte[] RowVer { get; set; } = [];
    }

    private sealed class CpPreviewRow
    {
        public int Version { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public int ScanFreshDays { get; set; }
        public int ItemCount { get; set; }
        public Guid CreatedById { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class CpEventRow
    {
        public string Event { get; set; } = string.Empty;
        public string? FromStatus { get; set; }
        public string ToStatus { get; set; } = string.Empty;
        public int? PreviewVersion { get; set; }
        public string? Reason { get; set; }
        public string Actor { get; set; } = string.Empty;
        public DateTimeOffset At { get; set; }
        public Guid ActorId { get; set; }
    }

    private sealed class CpListRow
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int AccountCount { get; set; }
        public int? CurrentPreviewVersion { get; set; }
        public string? OcoNumber { get; set; }
        public DateTimeOffset? WindowStart { get; set; }
        public DateTimeOffset? WindowEnd { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class CpItemRow
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string? ServerName { get; set; }
        public string? ComponentType { get; set; }
        public string? ComponentName { get; set; }
        public string? CurrentIdentity { get; set; }
        public string TargetIdentity { get; set; } = string.Empty;
        public string Flag { get; set; } = string.Empty;
        public DateTimeOffset? ScanAt { get; set; }
    }

    private sealed class CpStoredItemRow
    {
        public Guid AccountId { get; set; }
        public Guid? ScanLinkId { get; set; }
        public string? ServerName { get; set; }
        public string? ComponentType { get; set; }
        public string? ComponentName { get; set; }
        public string? CurrentIdentity { get; set; }
        public string TargetIdentity { get; set; } = string.Empty;
        public string Flag { get; set; } = string.Empty;
        public DateTimeOffset? ScanAt { get; set; }
    }
}
