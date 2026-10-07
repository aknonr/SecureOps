using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// gMSA conversion change plans on the real svcacct schema through migration 033 (CHANGE-PLAN-DESIGN.md threat table T1–T8,
/// PR 1 part): plan, preview, approval and cancel. Synthetic data only; nothing connects to a server. Runs alone: the race
/// tests hold plan locks under load and one test disables an append-only trigger for a moment (test database only).
/// </summary>
[Collection(ServiceAccountWriteGateCollection.Name)]
public sealed class ServiceAccountChangePlanSqlTests
{
    private const string _statement = "Sentetik: kendi yönetici hesabımla SYN-JUMP01 üzerinden çalıştırdım";
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];

    [ServiceAccountSqlFact]
    public async Task Plan_Preview_Approve_RecordsEventsHistoryAndAudit_WithoutSecretFields()
    {
        Setup s = await SetupAsync();
        (AccountDetail scanned, AccountDetail unscanned) = (await AccountAsync(s, "A1"), await AccountAsync(s, "A2"));
        await ScanAsync(s, scanned, ("SYN-APP01", "WindowsService", "SynSvc"), ("SYN-APP02", "IisVirtualDirectory", "Default/syn"));

        ChangePlanView draft = Ok(await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik gMSA geçişi " + s.Fx.Suffix, [Input(scanned, "gmsaSynA1"), Input(unscanned, "gmsaSynA2")]), _token));
        (draft.Status, draft.Kind, draft.Preview, draft.Approval).Should().Be(("Draft", "GmsaConversion", (ChangePlanPreviewView?)null, (ChangePlanApprovalView?)null));
        draft.Accounts.Select(a => (a.AccountId, a.TargetGmsaName)).Should().BeEquivalentTo(new[]
            { (scanned.Summary.Id, "gmsaSynA1"), (unscanned.Summary.Id, "gmsaSynA2") });

        ChangePlanView previewed = Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, draft.Id,
            new PreviewChangePlanRequest(draft.Version), _token));
        previewed.Status.Should().Be("Previewed");
        previewed.Preview!.Version.Should().Be(1);
        previewed.Preview.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
        (previewed.Preview.ItemCount, previewed.Preview.Flags.Ok, previewed.Preview.Flags.ManualOnly, previewed.Preview.Flags.NoScan, previewed.Preview.ScanFreshDays)
            .Should().Be((3, 1, 1, 1, 7));
        previewed.Permissions.Approve.Should().BeFalse("the planner never approves");
        previewed.Permissions.ApproveBlockedReason.Should().Be("approverIsPlanner");

        ChangePlanItemPage items = Ok(await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, draft.Id, 1, 50, _token));
        items.PreviewVersion.Should().Be(1);
        items.Items.Single(i => i.Flag == "NoScan").Should().Match<ChangePlanItemView>(i => i.AccountId == unscanned.Summary.Id && i.ServerName == null
            && i.TargetIdentity == "gmsaSynA2" && i.FlagLabel.Contains("Bilgi yok"));
        items.Items.Single(i => i.Flag == "Ok").Should().Match<ChangePlanItemView>(i => i.ServerName == "SYN-APP01" && i.ComponentName == "SynSvc"
            && i.CurrentIdentity == $"SYN\\{scanned.Summary.AccountName}" && i.ScanAt != null);
        ChangePlanRules.Digest(await StoredRowsAsync(s.Fx, draft.Id, 1)).Should().Be(previewed.Preview.Sha256, "the digest is the stored rows' digest");

        ChangePlanView verifierView = Ok(await s.Fx.Service.ChangePlanAsync(s.Verifier.Principal, s.Fx.Context, draft.Id, _token));
        (verifierView.Permissions.Approve, verifierView.Permissions.ApproveBlockedReason).Should().Be((true, (string?)null));

        ChangePlanView approved = Ok(await ApproveAsync(s, s.Verifier, previewed));
        approved.Status.Should().Be("Approved");
        approved.Approval!.Should().Match<ChangePlanApprovalView>(a => a.PreviewVersion == 1 && a.Sha256 == previewed.Preview.Sha256 && a.OcoNumber == "OCO-SYN-1001"
            && a.WindowEnd > a.WindowStart);
        approved.Events.Select(e => e.Event).Should().Equal("Created", "Previewed", "Approved");
        approved.Permissions.Should().Match<ChangePlanPermissions>(p => !p.Edit && !p.Preview && !p.Approve);

        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE EntityType = 'ChangePlan' AND EntityId = @id", new { id = draft.Id })).Should().Be(6,
            "one history row per account for create, preview and approval");
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.ChangePlan%' AND DetailsJson LIKE @id",
            new { id = "%" + draft.Id.ToString("D") + "%" })).Should().Be(3);
        System.Text.Json.JsonSerializer.Serialize(approved).Should().NotContainEquivalentOf("password").And.NotContainEquivalentOf("parola");
        (await s.Fx.CountAsync("""
            SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
            WHERE SCHEMA_NAME(t.schema_id) = 'svcacct' AND t.name LIKE 'Change%' AND (c.name LIKE '%Pass%' OR c.name LIKE '%Secret%' OR c.name LIKE '%Parola%')
            """)).Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task Approve_AfterPlanChanged_IsStale_AndWritesNothing()
    {
        Setup s = await SetupAsync();
        AccountDetail first = await AccountAsync(s, "T1A");
        AccountDetail added = await AccountAsync(s, "T1B");
        ChangePlanView previewed = await PreviewedAsync(s, first);

        ChangePlanView changed = Ok(await s.Fx.Service.UpdateChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id,
            new UpdateChangePlanRequest(previewed.Version, [Input(first, "gmsaSynT1"), Input(added, "gmsaSynT1b")]), _token));
        (changed.Status, changed.Preview).Should().Be(("Draft", (ChangePlanPreviewView?)null), "a change after the preview returns the plan to Draft");
        Counts before = await CountsAsync(s.Fx, previewed.Id);

        SaResult<ChangePlanView> stale = await ApproveAsync(s, s.Verifier, previewed);

        (stale.ErrorCode, stale.Field).Should().Be((SaErrors.ChangePlanState, "planNotPreviewed"));
        (await CountsAsync(s.Fx, previewed.Id)).Should().Be(before, "a refused approval writes no approval, event, history or audit");

        ChangePlanView again = Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id,
            new PreviewChangePlanRequest(changed.Version), _token));
        again.Preview!.Version.Should().Be(2);
        SaResult<ChangePlanView> old = await ApproveAsync(s, s.Verifier, previewed);
        (old.ErrorCode, old.Field).Should().Be((SaErrors.ChangePlanState, "previewStale"), "version 1 and its digest are no longer current");
        (await CountsAsync(s.Fx, previewed.Id)).Approvals.Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task Approve_WrongSha_Is409()
    {
        Setup s = await SetupAsync();
        ChangePlanView previewed = await PreviewedAsync(s, await AccountAsync(s, "T7W"));
        Counts before = await CountsAsync(s.Fx, previewed.Id);

        SaResult<ChangePlanView> wrong = await ApproveAsync(s, s.Verifier, previewed, sha: new string('0', 64));

        (wrong.ErrorCode, wrong.Field).Should().Be((SaErrors.ChangePlanState, "previewStale"));
        (await CountsAsync(s.Fx, previewed.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task Approve_RecomputesDigestFromStoredItems()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "T7R");
        await ScanAsync(s, account, ("SYN-APP01", "WindowsService", "SynSvc"));
        ChangePlanView previewed = await PreviewedAsync(s, account);
        Counts before = await CountsAsync(s.Fx, previewed.Id);

        // Test database only: the append-only trigger is lifted for one statement to simulate a tampered stored row.
        await using (SqlConnection connection = s.Fx.Connection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("DISABLE TRIGGER svcacct.TR_SaChangePlanItems_AppendOnly ON svcacct.ChangePlanItems;");
            try
            {
                await connection.ExecuteAsync("""
                    UPDATE i SET ComponentName = N'SynSvcTampered' FROM svcacct.ChangePlanItems i JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId
                    WHERE v.PlanId = @id AND i.ComponentName = N'SynSvc';
                    """, new { id = previewed.Id });
            }
            finally
            {
                await connection.ExecuteAsync("ENABLE TRIGGER svcacct.TR_SaChangePlanItems_AppendOnly ON svcacct.ChangePlanItems;");
            }
        }

        SaResult<ChangePlanView> result = await ApproveAsync(s, s.Verifier, previewed);

        (result.ErrorCode, result.Field).Should().Be((SaErrors.ChangePlanState, "previewStale"), "the stored rows no longer match the stored digest");
        (await CountsAsync(s.Fx, previewed.Id)).Should().Be(before);
        s.Fx.SqlDiagnostics.Should().BeEmpty("a digest mismatch is a refusal, not a persistence failure");
    }

    [ServiceAccountSqlFact]
    public async Task Approve_Replayed_IsRefused_OneApprovalRow()
    {
        Setup s = await SetupAsync();
        ChangePlanView previewed = await PreviewedAsync(s, await AccountAsync(s, "T2"));

        Ok(await ApproveAsync(s, s.Verifier, previewed));
        SaResult<ChangePlanView> replay = await ApproveAsync(s, s.Verifier, previewed);

        (replay.ErrorCode, replay.Field).Should().Be((SaErrors.ChangePlanState, "planNotPreviewed"));
        (await CountsAsync(s.Fx, previewed.Id)).Approvals.Should().Be(1);
    }

    [ServiceAccountSqlFact]
    public async Task Approve_AfterCancel_IsRefused()
    {
        Setup s = await SetupAsync();
        ChangePlanView previewed = await PreviewedAsync(s, await AccountAsync(s, "T2C"));
        ChangePlanView cancelled = Ok(await s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id,
            new CancelChangePlanRequest(previewed.Version, "Sentetik iptal"), _token));
        cancelled.Status.Should().Be("Cancelled");

        SaResult<ChangePlanView> late = await ApproveAsync(s, s.Verifier, previewed);

        (late.ErrorCode, late.Field).Should().Be((SaErrors.ChangePlanState, "planNotPreviewed"));
        (await CountsAsync(s.Fx, previewed.Id)).Approvals.Should().Be(0);
        cancelled.Accounts.Should().ContainSingle("cancelling keeps every record");
    }

    [ServiceAccountSqlFact]
    public async Task Approve_ByPlanner_Is403_AndWritesNothing()
    {
        Setup s = await SetupAsync();
        ChangePlanView previewed = await PreviewedAsync(s, await AccountAsync(s, "T3P"));
        Counts before = await CountsAsync(s.Fx, previewed.Id);

        SaResult<ChangePlanView> self = await ApproveAsync(s, s.Planner, previewed);

        (self.ErrorCode, self.Field).Should().Be((SaErrors.Forbidden, "approverIsPlanner"));
        (await CountsAsync(s.Fx, previewed.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task Approve_ByEditorOfPlan_Is403()
    {
        Setup s = await SetupAsync();
        AccountDetail first = await AccountAsync(s, "T3E");
        AccountDetail second = await AccountAsync(s, "T3F");
        ChangePlanView draft = await DraftAsync(s, first);
        // The verifier changes the plan, the planner previews it: the verifier may no longer approve.
        ChangePlanView edited = Ok(await s.Fx.Service.UpdateChangePlanAsync(s.Verifier.Principal, s.Fx.Context, draft.Id,
            new UpdateChangePlanRequest(draft.Version, [Input(first, "gmsaSynT3"), Input(second, "gmsaSynT3b")]), _token));
        ChangePlanView previewed = Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, draft.Id,
            new PreviewChangePlanRequest(edited.Version), _token));
        Counts before = await CountsAsync(s.Fx, draft.Id);

        SaResult<ChangePlanView> editor = await ApproveAsync(s, s.Verifier, previewed);

        (editor.ErrorCode, editor.Field).Should().Be((SaErrors.Forbidden, "approverChangedPlan"));
        (await CountsAsync(s.Fx, draft.Id)).Should().Be(before);
        Ok(await s.Fx.Service.ChangePlanAsync(s.Verifier.Principal, s.Fx.Context, draft.Id, _token)).Permissions.ApproveBlockedReason
            .Should().Be("approverChangedPlan");
    }

    [ServiceAccountSqlFact]
    public async Task Approve_ByPreviewer_Is403()
    {
        Setup s = await SetupAsync();
        ChangePlanView draft = await DraftAsync(s, await AccountAsync(s, "T3V"));
        ChangePlanView previewed = Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Verifier.Principal, s.Fx.Context, draft.Id,
            new PreviewChangePlanRequest(draft.Version), _token));

        SaResult<ChangePlanView> previewer = await ApproveAsync(s, s.Verifier, previewed);

        (previewer.ErrorCode, previewer.Field).Should().Be((SaErrors.Forbidden, "approverChangedPlan"));
        (await CountsAsync(s.Fx, draft.Id)).Approvals.Should().Be(0);
        Ok(await ApproveAsync(s, s.Second, previewed)).Status.Should().Be("Approved", "a third person may approve");
    }

    [ServiceAccountSqlFact]
    public async Task ApprovalTrigger_RefusesPlannerEvenWhenServiceIsBypassed()
    {
        Setup s = await SetupAsync();
        ChangePlanView previewed = await PreviewedAsync(s, await AccountAsync(s, "T3T"));
        await using SqlConnection connection = s.Fx.Connection();

        Func<Task> direct = () => connection.ExecuteAsync("""
            INSERT INTO svcacct.ChangePlanApprovals(Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy, ApprovedAt)
            SELECT NEWID(), v.PlanId, v.Id, v.Sha256, N'OCO-SYN-9', SYSUTCDATETIME(), DATEADD(hour, 1, SYSUTCDATETIME()), N'Atlatma', @planner, SYSUTCDATETIME()
            FROM svcacct.ChangePlanPreviews v WHERE v.PlanId = @id;
            """, new { id = previewed.Id, planner = s.Planner.User.Id });

        (await direct.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(51393);
        (await CountsAsync(s.Fx, previewed.Id)).Approvals.Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentApproveAndCancel_ExactlyOneWins()
    {
        Setup s = await SetupAsync();
        AccountDetail[] accounts = [.. await Task.WhenAll(Enumerable.Range(0, 20).Select(i => AccountAsync(s, "R" + i)))];
        for (int round = 0; round < accounts.Length; round++)
        {
            ChangePlanView previewed = await PreviewedAsync(s, accounts[round]);
            Task<SaResult<ChangePlanView>> approve = Task.Run(() => ApproveAsync(s, s.Verifier, previewed));
            Task<SaResult<ChangePlanView>> cancel = Task.Run(() => s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id,
                new CancelChangePlanRequest(previewed.Version, "Sentetik yarış iptali"), _token));
            SaResult<ChangePlanView>[] results = await Task.WhenAll(approve, cancel);

            results.Count(r => r.IsSuccess).Should().Be(1, $"round {round}: one command wins");
            results.Single(r => !r.IsSuccess).ErrorCode.Should().BeOneOf(SaErrors.ChangePlanState, SaErrors.Conflict);
            ChangePlanView final = Ok(await s.Fx.Service.ChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id, _token));
            final.Status.Should().Be(approve.Result.IsSuccess ? "Approved" : "Cancelled");
            final.Events[^1].ToStatus.Should().Be(final.Status, "the last event agrees with the plan");
            (await CountsAsync(s.Fx, previewed.Id)).Approvals.Should().Be(approve.Result.IsSuccess ? 1 : 0);
        }

        s.Fx.SqlDiagnostics.Should().NotContain(d => d.Contains("Number=1205"), "no deadlock victim");
        s.Fx.SqlDiagnostics.Should().BeEmpty();
    }

    [ServiceAccountSqlFact]
    public async Task TwoApproversAtOnce_OneApprovalRow()
    {
        Setup s = await SetupAsync();
        for (int round = 0; round < 5; round++)
        {
            ChangePlanView previewed = await PreviewedAsync(s, await AccountAsync(s, "D" + round));
            SaResult<ChangePlanView>[] results = await Task.WhenAll(Task.Run(() => ApproveAsync(s, s.Verifier, previewed)),
                Task.Run(() => ApproveAsync(s, s.Second, previewed)));

            results.Count(r => r.IsSuccess).Should().Be(1);
            results.Single(r => !r.IsSuccess).Should().Match<SaResult<ChangePlanView>>(r => r.ErrorCode == SaErrors.ChangePlanState && r.Field == "planNotPreviewed");
            (await CountsAsync(s.Fx, previewed.Id)).Approvals.Should().Be(1);
        }

        s.Fx.SqlDiagnostics.Should().BeEmpty();
    }

    [ServiceAccountSqlFact]
    public async Task CreatePlan_OutOfScopeAccount_RefusedPerAccount_NoName()
    {
        Setup s = await SetupAsync();
        AccountDetail mine = await AccountAsync(s, "T5A");
        Guid otherOrg = await s.Fx.OrganizationAsync("SYN PLAN BASKA ORG " + s.Fx.Suffix);
        SynUser otherCoordinator = await s.Fx.UserAsync(_all);
        await s.Fx.GrantAsync(otherCoordinator, ScopeKind.Organization, otherOrg);
        AccountDetail outside = Ok(await s.Fx.Service.CreateAccountAsync(otherCoordinator.Principal, s.Fx.Context,
            new CreateAccountRequest("SYN_PLAN_DISARI_" + s.Fx.Suffix, "SYN", otherOrg, "Sentetik kapsam dışı hesap"), _token));
        var missing = Guid.NewGuid();
        int plans = await PlanCountAsync(s.Fx);

        SaResult<ChangePlanView> refused = await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context, new CreateChangePlanRequest(
            "Sentetik kapsam planı", [Input(mine, "gmsaSynT5"), Input(outside, "gmsaSynT5x"), new ChangePlanAccountInput(missing, "gmsaSynT5y"),
                Input(mine, "gmsaSynT5z")]), _token);

        (refused.ErrorCode, refused.Field).Should().Be((SaErrors.ChangePlanAccountsRefused, "accounts"));
        IReadOnlyList<ChangePlanAccountResult> results = refused.Current.Should().BeAssignableTo<IReadOnlyList<ChangePlanAccountResult>>().Subject;
        results.Select(r => (r.AccountId, r.Outcome)).Should().Equal((mine.Summary.Id, "Accepted"), (outside.Summary.Id, "Unavailable"),
            (missing, "Unavailable"), (mine.Summary.Id, "Duplicate"));
        results[1].Should().BeEquivalentTo(results[2] with { AccountId = outside.Summary.Id }, "out of scope looks exactly like missing");
        results[1].AccountName.Should().BeNull();
        System.Text.Json.JsonSerializer.Serialize(results).Should().NotContain(outside.Summary.AccountName);
        (await PlanCountAsync(s.Fx)).Should().Be(plans, "one refused account refuses the whole request");
    }

    [ServiceAccountSqlFact]
    public async Task ParticipantBasis_CannotAddAccount()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "T5P");
        Guid team = await s.Fx.TeamAsync("SYN PLAN TAKIM " + s.Fx.Suffix, null);
        Ok(await s.Fx.Service.CreateRequestAsync(s.Planner.Principal, s.Fx.Context, account.Summary.Id,
            new CreateWorkRequest("GmsaConversion", TargetTeamId: team), _token));
        SynUser participant = await s.Fx.UserAsync(_all);
        await s.Fx.GrantAsync(participant, ScopeKind.Team, team: team);
        Ok(await s.Fx.Service.AccountAsync(participant.Principal, s.Fx.Context, account.Summary.Id, _token)).Permissions.Basis
            .Should().Be(ServiceAccountAccessBasis.Participant);

        SaResult<ChangePlanView> refused = await s.Fx.Service.CreateChangePlanAsync(participant.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik katılımcı planı", [Input(account, "gmsaSynT5p")]), _token);

        refused.ErrorCode.Should().Be(SaErrors.ChangePlanAccountsRefused);
        ((IReadOnlyList<ChangePlanAccountResult>)refused.Current!).Single().Should().Match<ChangePlanAccountResult>(r =>
            r.Outcome == "Unavailable" && r.AccountName == account.Summary.AccountName, "visible to a participant, but not theirs to plan");
    }

    [ServiceAccountSqlFact]
    public async Task ScopeShrinksAfterPlan_PreviewApproveUpdateAndCancel_Are404()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "T5S");
        ChangePlanView draft = await DraftAsync(s, account);
        ChangePlanView previewed = await PreviewedAsync(s, await AccountAsync(s, "T5T"));
        await RevokeAsync(s.Fx, s.Planner);
        await RevokeAsync(s.Fx, s.Verifier);
        Counts before = await CountsAsync(s.Fx, previewed.Id);

        (await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, draft.Id, new PreviewChangePlanRequest(draft.Version), _token))
            .ErrorCode.Should().Be(SaErrors.NotFound);
        (await s.Fx.Service.UpdateChangePlanAsync(s.Planner.Principal, s.Fx.Context, draft.Id,
            new UpdateChangePlanRequest(draft.Version, [Input(account, "gmsaSynOther")]), _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await ApproveAsync(s, s.Verifier, previewed)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id,
            new CancelChangePlanRequest(previewed.Version, "Sentetik"), _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, previewed.Id, 1, 50, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await CountsAsync(s.Fx, previewed.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task PartialScope_PlanInvisibleInListAndDetail()
    {
        Setup s = await SetupAsync();
        AccountDetail first = await AccountAsync(s, "T5V");
        Guid otherOrg = await s.Fx.OrganizationAsync("SYN PLAN IKINCI ORG " + s.Fx.Suffix);
        await s.Fx.GrantAsync(s.Planner, ScopeKind.Organization, otherOrg);
        AccountDetail second = Ok(await s.Fx.Service.CreateAccountAsync(s.Planner.Principal, s.Fx.Context,
            new CreateAccountRequest("SYN_PLAN_IKINCI_" + s.Fx.Suffix, "SYN", otherOrg, "Sentetik ikinci org hesabı"), _token));
        ChangePlanView plan = Ok(await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik iki org planı", [Input(first, "gmsaSynV1"), Input(second, "gmsaSynV2")]), _token));
        SynUser partial = await s.Fx.UserAsync(_all);
        await s.Fx.GrantAsync(partial, ScopeKind.Organization, s.Org);

        ChangePlanPage list = Ok(await s.Fx.Service.ChangePlansAsync(partial.Principal, s.Fx.Context, new ChangePlanListQuery(), _token));
        ChangePlanPage byAccount = Ok(await s.Fx.Service.ChangePlansAsync(partial.Principal, s.Fx.Context, new ChangePlanListQuery(AccountId: first.Summary.Id), _token));

        list.Items.Should().NotContain(p => p.Id == plan.Id, "one of its accounts is outside this caller's scope");
        byAccount.Items.Should().BeEmpty();
        Ok(await s.Fx.Service.ChangePlansAsync(s.Planner.Principal, s.Fx.Context, new ChangePlanListQuery(AccountId: first.Summary.Id), _token))
            .Items.Should().ContainSingle(p => p.Id == plan.Id && p.AccountCount == 2);
        await NotFoundLikeMissingAsync(s, partial, plan.Id);
    }

    [ServiceAccountSqlFact]
    public async Task List_AccountWithoutOrganizationOrOwner_CountsAsOutsideScope()
    {
        Setup s = await SetupAsync();
        SynUser everything = await s.Fx.UserAsync(_all);
        await s.Fx.GrantAsync(everything, ScopeKind.All);
        AccountDetail unplaced = Ok(await s.Fx.Service.CreateAccountAsync(everything.Principal, s.Fx.Context,
            new CreateAccountRequest("SYN_PLAN_YERSIZ_" + s.Fx.Suffix, "SYN", null, "Sentetik organizasyonsuz hesap"), _token));
        ChangePlanView plan = Ok(await s.Fx.Service.CreateChangePlanAsync(everything.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik yersiz plan", [Input(unplaced, "gmsaSynU")]), _token));
        SynUser teamOnly = await s.Fx.UserAsync(_all);
        await s.Fx.GrantAsync(teamOnly, ScopeKind.Team, team: await s.Fx.TeamAsync("SYN PLAN BOS TAKIM " + s.Fx.Suffix, null));

        // A NULL organization and owner must never satisfy a negated scope test (SQL three-valued logic).
        Ok(await s.Fx.Service.ChangePlansAsync(teamOnly.Principal, s.Fx.Context, new ChangePlanListQuery(PageSize: 100), _token)).Items
            .Should().NotContain(p => p.Id == plan.Id);
        Ok(await s.Fx.Service.ChangePlansAsync(s.Planner.Principal, s.Fx.Context, new ChangePlanListQuery(PageSize: 100), _token)).Items
            .Should().NotContain(p => p.Id == plan.Id);
        await NotFoundLikeMissingAsync(s, teamOnly, plan.Id);
        Ok(await s.Fx.Service.ChangePlansAsync(everything.Principal, s.Fx.Context, new ChangePlanListQuery(AccountId: unplaced.Summary.Id), _token)).Items
            .Should().ContainSingle(p => p.Id == plan.Id);
    }

    [ServiceAccountSqlFact]
    public async Task Get_PlanOutsideScope_Is404_SameBodyAsMissing()
    {
        Setup s = await SetupAsync();
        ChangePlanView plan = await DraftAsync(s, await AccountAsync(s, "T6G"));
        SynUser stranger = await s.Fx.UserAsync(_all);
        await s.Fx.GrantAsync(stranger, ScopeKind.Organization, await s.Fx.OrganizationAsync("SYN PLAN YABANCI " + s.Fx.Suffix));

        await NotFoundLikeMissingAsync(s, stranger, plan.Id);
    }

    [ServiceAccountSqlFact]
    public async Task Items_AreOnlyThePlansCurrentPreview()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "T6I");
        AccountDetail other = await AccountAsync(s, "T6J");
        await ScanAsync(s, account, ("SYN-APP01", "WindowsService", "SynSvc"));
        ChangePlanView first = await PreviewedAsync(s, account);
        ChangePlanView second = await PreviewedAsync(s, other);
        ChangePlanView renamed = Ok(await s.Fx.Service.UpdateChangePlanAsync(s.Planner.Principal, s.Fx.Context, first.Id,
            new UpdateChangePlanRequest(first.Version, [Input(account, "gmsaSynRenamed")]), _token));
        (await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, first.Id, 1, 50, _token)).Value!.Items
            .Should().BeEmpty("a Draft plan has no current preview");
        Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, first.Id, new PreviewChangePlanRequest(renamed.Version), _token));

        ChangePlanItemPage page = Ok(await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, first.Id, 1, 50, _token));

        page.PreviewVersion.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Should().Match<ChangePlanItemView>(i => i.TargetIdentity == "gmsaSynRenamed" && i.AccountId == account.Summary.Id);
        Ok(await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, second.Id, 1, 50, _token)).Items
            .Should().OnlyContain(i => i.AccountId == other.Summary.Id);
        (await s.Fx.Service.ChangePlanItemsAsync(s.Verifier.Principal, s.Fx.Context, first.Id, 0, 50, _token)).ErrorCode.Should().Be(SaErrors.Invalid);
    }

    [ServiceAccountSqlFact]
    public async Task AccountInOpenPlan_RefusedInSecondPlan_AcceptedAfterCancel()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "K6");
        ChangePlanView first = await DraftAsync(s, account);

        SaResult<ChangePlanView> second = await s.Fx.Service.CreateChangePlanAsync(s.Verifier.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik ikinci plan", [Input(account, "gmsaSynK6")]), _token);

        second.ErrorCode.Should().Be(SaErrors.ChangePlanAccountsRefused);
        ((IReadOnlyList<ChangePlanAccountResult>)second.Current!).Single().Outcome.Should().Be("InOpenPlan");
        Ok(await s.Fx.Service.CancelChangePlanAsync(s.Verifier.Principal, s.Fx.Context, first.Id, new CancelChangePlanRequest(first.Version, "Sentetik: yeniden planlanacak"), _token))
            .Status.Should().Be("Cancelled", "a verifier may cancel a plan they did not create");
        Ok(await s.Fx.Service.CreateChangePlanAsync(s.Verifier.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik ikinci plan", [Input(account, "gmsaSynK6")]), _token)).Status.Should().Be("Draft");
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentPlansForOneAccount_OnlyOneHoldsIt()
    {
        Setup s = await SetupAsync();
        for (int round = 0; round < 5; round++)
        {
            AccountDetail account = await AccountAsync(s, "K" + round);
            SaResult<ChangePlanView>[] results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(() =>
                s.Fx.Service.CreateChangePlanAsync(i == 0 ? s.Planner.Principal : s.Verifier.Principal, s.Fx.Context,
                    new CreateChangePlanRequest("Sentetik eşzamanlı plan " + i, [Input(account, "gmsaSynK")]), _token))));

            results.Count(r => r.IsSuccess).Should().Be(1, $"round {round}");
            (await s.Fx.CountAsync("SELECT COUNT(DISTINCT PlanId) FROM svcacct.ChangePlanAccounts WHERE AccountId = @a", new { a = account.Summary.Id })).Should().Be(1);
        }

        s.Fx.SqlDiagnostics.Should().BeEmpty();
    }

    [ServiceAccountSqlFact]
    public async Task InvalidInput_Is400_AndWritesNothing()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "V");
        ChangePlanView previewed = await PreviewedAsync(s, account);
        Counts before = await CountsAsync(s.Fx, previewed.Id);
        int plans = await PlanCountAsync(s.Fx);

        (await ApproveAsync(s, s.Verifier, previewed, oco: "   ")).Should().Match<SaResult<ChangePlanView>>(r => r.ErrorCode == SaErrors.Invalid && r.Field == "ocoNumber");
        (await ApproveAsync(s, s.Verifier, previewed, windowHours: -1)).Field.Should().Be("window");
        (await ApproveAsync(s, s.Verifier, previewed, reason: "")).Field.Should().Be("reason");
        (await ApproveAsync(s, s.Verifier, previewed, sha: "not-a-digest")).Field.Should().Be("sha256");
        (await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context, new CreateChangePlanRequest("", [Input(account, "gmsaSyn")]), _token))
            .Field.Should().Be("title");
        (await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context, new CreateChangePlanRequest("Sentetik", []), _token))
            .Field.Should().Be("accounts");
        SaResult<ChangePlanView> longName = await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik", [Input(await AccountAsync(s, "V2"), "gmsaSynNameTooLong16")]), _token);
        longName.ErrorCode.Should().Be(SaErrors.ChangePlanAccountsRefused);
        ((IReadOnlyList<ChangePlanAccountResult>)longName.Current!).Single().Outcome.Should().Be("InvalidName");
        (await s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id, new CancelChangePlanRequest("AAAAAAAAAAAAAAAA", "Sentetik"), _token))
            .ErrorCode.Should().Be(SaErrors.Conflict, "a stale expected version");
        SynUser worker = await s.Fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work);
        await s.Fx.GrantAsync(worker, ScopeKind.Organization, s.Org);
        (await s.Fx.Service.CancelChangePlanAsync(worker.Principal, s.Fx.Context, previewed.Id, new CancelChangePlanRequest(previewed.Version, "Sentetik"), _token))
            .Should().Match<SaResult<ChangePlanView>>(r => r.ErrorCode == SaErrors.Forbidden, "only the planner (Work) or a verifier cancels");

        (await CountsAsync(s.Fx, previewed.Id)).Should().Be(before);
        (await PlanCountAsync(s.Fx)).Should().Be(plans);
    }

    [ServiceAccountSqlFact]
    public async Task FailureBeforeCommit_LeavesNoPlanNoEventNoAudit()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "F");
        string trigger = "TR_SaTest_FailAudit_" + s.Fx.Suffix;
        int plans = await PlanCountAsync(s.Fx);
        await using SqlConnection connection = s.Fx.Connection();
        await connection.OpenAsync();
        // Test database only: the platform audit insert fails for this test's correlation id, i.e. after every plan row is written.
        await connection.ExecuteAsync($"""
            CREATE TRIGGER audit.{trigger} ON audit.AuditLog AFTER INSERT AS
            BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE CorrelationId = N'{s.Fx.Context.CorrelationId}') THROW 51399, 'Injected audit failure.', 1; END;
            """);
        SaResult<ChangePlanView> failed;
        try
        {
            failed = await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
                new CreateChangePlanRequest("Sentetik başarısız plan", [Input(account, "gmsaSynF")]), _token);
        }
        finally
        {
            await connection.ExecuteAsync($"DROP TRIGGER audit.{trigger};");
        }

        failed.ErrorCode.Should().Be(SaErrors.Unavailable);
        (await PlanCountAsync(s.Fx)).Should().Be(plans);
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangePlanAccounts WHERE AccountId = @a", new { a = account.Summary.Id })).Should().Be(0);
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE EntityType = 'ChangePlan' AND AccountId = @a", new { a = account.Summary.Id })).Should().Be(0);
        s.Fx.SqlDiagnostics.Should().ContainSingle().Which.Should().Contain("Number=51399");
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------

    private sealed record Setup(ServiceAccountSqlFixture Fx, SynUser Planner, SynUser Verifier, SynUser Second, Guid Org);

    private sealed record Counts(int Approvals, int Events, int History, int Audit);

    private static async Task<Setup> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN PLAN ORG " + fx.Suffix);
        SynUser planner = await fx.UserAsync(_all), verifier = await fx.UserAsync(_all), second = await fx.UserAsync(_all);
        foreach (SynUser user in new[] { planner, verifier, second })
        {
            await fx.GrantAsync(user, ScopeKind.Organization, org);
        }

        return new Setup(fx, planner, verifier, second, org);
    }

    private static async Task<AccountDetail> AccountAsync(Setup s, string label) =>
        Ok(await s.Fx.Service.CreateAccountAsync(s.Planner.Principal, s.Fx.Context,
            new CreateAccountRequest($"SYN_PLAN_{label}_{s.Fx.Suffix}", "SYN", s.Org, "Sentetik değişiklik planı hesabı"), _token));

    private static async Task ScanAsync(Setup s, AccountDetail account, params (string Server, string Type, string Component)[] components)
    {
        string searched = $"SYN\\{account.Summary.AccountName}";
        byte[] file = ServiceAccountUsageScanBatchSqlTests.Discovery([searched], [.. components.Select(c => (c.Server, c.Type, c.Component, searched))]);
        UsageScanBatchResult result = Ok(await s.Fx.Service.AttachUsageScanToAccountsAsync(s.Planner.Principal, s.Fx.Context, [account.Summary.Id],
            "plan-scan.json", file, _statement, _token));
        result.Results.Single().Outcome.Should().Be("Attached");
    }

    private static ChangePlanAccountInput Input(AccountDetail account, string name) => new(account.Summary.Id, name);

    private static async Task<ChangePlanView> DraftAsync(Setup s, AccountDetail account) =>
        Ok(await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik plan " + account.Summary.AccountName, [Input(account, "gmsaSynX")]), _token));

    private static async Task<ChangePlanView> PreviewedAsync(Setup s, AccountDetail account)
    {
        ChangePlanView draft = await DraftAsync(s, account);
        return Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, draft.Id, new PreviewChangePlanRequest(draft.Version), _token));
    }

    private static Task<SaResult<ChangePlanView>> ApproveAsync(Setup s, SynUser approver, ChangePlanView previewed, string? sha = null, string oco = "oco-syn-1001",
        int windowHours = 4, string reason = "Sentetik onay gerekçesi")
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(1);
        return s.Fx.Service.ApproveChangePlanAsync(approver.Principal, s.Fx.Context, previewed.Id, new ApproveChangePlanRequest(previewed.Preview!.Version,
            sha ?? previewed.Preview.Sha256, oco, start, start.AddHours(windowHours), reason), _token);
    }

    private static async Task<Counts> CountsAsync(ServiceAccountSqlFixture fx, Guid planId) => new(
        await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangePlanApprovals WHERE PlanId = @planId", new { planId }),
        await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangePlanEvents WHERE PlanId = @planId", new { planId }),
        await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE EntityType = 'ChangePlan' AND EntityId = @planId", new { planId }),
        await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE DetailsJson LIKE @id", new { id = "%" + planId.ToString("D") + "%" }));

    private static Task<int> PlanCountAsync(ServiceAccountSqlFixture fx) => fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangePlans");

    private static async Task RevokeAsync(ServiceAccountSqlFixture fx, SynUser user)
    {
        await using SqlConnection connection = fx.Connection();
        await connection.ExecuteAsync("""
            UPDATE svcacct.ScopeGrants SET RevokedAt = SYSUTCDATETIME(), RevokedBy = NEWID(), RevokeReason = N'Sentetik kapsam daralması'
            WHERE UserId = @id AND RevokedAt IS NULL;
            """, new { id = user.User.Id });
    }

    private static async Task<ChangePlanPreviewRow[]> StoredRowsAsync(ServiceAccountSqlFixture fx, Guid planId, int version)
    {
        await using SqlConnection connection = fx.Connection();
        return [.. (await connection.QueryAsync<(Guid AccountId, Guid? ScanLinkId, string? ServerName, string? ComponentType, string? ComponentName,
            string? CurrentIdentity, string TargetIdentity, string Flag, DateTimeOffset? ScanAt)>("""
            SELECT i.AccountId, i.ScanLinkId, i.ServerName, i.ComponentType, i.ComponentName, i.CurrentIdentity, i.TargetIdentity, i.Flag, i.ScanAt
            FROM svcacct.ChangePlanItems i JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId WHERE v.PlanId = @planId AND v.Version = @version;
            """, new { planId, version })).Select(r => new ChangePlanPreviewRow(r.AccountId, r.ScanLinkId, r.ServerName, r.ComponentType, r.ComponentName,
            r.CurrentIdentity, r.TargetIdentity, Enum.Parse<ChangePlanFlag>(r.Flag), r.ScanAt))];
    }

    private static async Task NotFoundLikeMissingAsync(Setup s, SynUser caller, Guid planId)
    {
        var missing = Guid.NewGuid();
        foreach (Guid id in new[] { planId, missing })
        {
            SaResult<ChangePlanView> detail = await s.Fx.Service.ChangePlanAsync(caller.Principal, s.Fx.Context, id, _token);
            (detail.ErrorCode, detail.Field, detail.Current).Should().Be((SaErrors.NotFound, (string?)null, (object?)null));
            SaResult<ChangePlanItemPage> items = await s.Fx.Service.ChangePlanItemsAsync(caller.Principal, s.Fx.Context, id, 1, 50, _token);
            (items.ErrorCode, items.Field, items.Current).Should().Be((SaErrors.NotFound, (string?)null, (object?)null));
        }
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
