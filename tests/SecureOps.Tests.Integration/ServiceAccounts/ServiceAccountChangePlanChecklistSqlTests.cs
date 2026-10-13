using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// gMSA conversion change plans, PR 2 part (CHANGE-PLAN-DESIGN.md "Kontrol listesi, kanıt ve kapanış", threat rows T4b–T13
/// and the PR 2 cells of T5/T6): the person's checklist, plan evidence (migration 034) and completion on the real svcacct
/// schema. Synthetic data only; nothing connects to a server. Runs in the serial write-gate collection: the race tests hold
/// plan locks under load and one test injects an audit failure (test database only).
/// </summary>
[Collection(ServiceAccountWriteGateCollection.Name)]
public sealed class ServiceAccountChangePlanChecklistSqlTests
{
    private const string _statement = "Sentetik: kendi yönetici hesabımla SYN-JUMP01 üzerinden çalıştırdım";
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];
    private static readonly byte[] _textFile = "Sentetik plan kanıtı: değişiklik kaydı ekran özeti"u8.ToArray();

    [ServiceAccountSqlFact]
    public async Task Checklist_Evidence_Complete_EndToEnd_RecordsWithoutSecretFields()
    {
        Setup s = await SetupAsync();
        (AccountDetail scanned, AccountDetail unscanned) = (await AccountAsync(s, "E1"), await AccountAsync(s, "E2"));
        await ScanAsync(s, scanned, ("SYN-APP01", "WindowsService", "SynSvc"), ("SYN-APP02", "IisVirtualDirectory", "Default/syn"));
        ChangePlanView approved = await ApprovedAsync(s, [scanned, unscanned]);
        approved.Checklist.Should().Be(new ChangePlanChecklistView(3, 0, 0, 0, 0, 3, 0));
        approved.Permissions.Should().Match<ChangePlanPermissions>(p => p.Check && !p.Complete && p.UploadEvidence);
        ChangePlanItemView[] items = await ItemsAsync(s, approved.Id);

        ChangePlanView started = Ok(await CheckAsync(s, s.Planner, approved.Id, items[0], "Done"));
        (started.Status, started.Events[^1].Event, started.Events[^1].FromStatus).Should().Be(("InProgress", "Started", "Approved"));
        foreach (ChangePlanItemView item in items.Skip(1))
        {
            Ok(await CheckAsync(s, s.Second, approved.Id, item, item.AccountId == unscanned.Summary.Id ? "Failed" : "Done",
                item.AccountId == unscanned.Summary.Id ? "Sentetik: servis yeni kimlikle başlamadı" : null));
        }

        ChangePlanView checkedPlan = Ok(await s.Fx.Service.ChangePlanAsync(s.Verifier.Principal, s.Fx.Context, approved.Id, _token));
        checkedPlan.Checklist.Should().Be(new ChangePlanChecklistView(3, 2, 0, 1, 0, 0, 0));
        checkedPlan.Events.Select(e => e.Event).Should().Equal("Created", "Previewed", "Approved", "Started");
        checkedPlan.Permissions.Complete.Should().BeTrue();
        ChangePlanItemView done = (await ItemsAsync(s, approved.Id)).First(i => i.AccountId == scanned.Summary.Id);
        done.Check!.Should().Match<ChangePlanItemCheckView>(c => c.State == "Done" && c.StateLabel.Contains("beyan") && !c.OutsideWindow);

        ChangePlanView withEvidence = Ok(await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, approved.Id, "plan-kanit.txt",
            "text/plain", _textFile, "Sentetik değişiklik özeti", _token));
        EvidenceView evidence = withEvidence.Evidence!.Should().ContainSingle().Subject;
        (evidence.OwnerEntityType, evidence.OwnerEntityId).Should().Be(("ChangePlan", approved.Id));
        Ok(await s.Fx.Service.EvidenceAsync(s.Verifier.Principal, s.Fx.Context, evidence.Id, _token)).Content.Should().Equal(_textFile);

        ChangePlanView completed = Ok(await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest(withEvidence.Version, "Sentetik kapanış notu"), _token));

        (completed.Status, completed.Events[^1].Event).Should().Be(("Completed", "Completed"));
        ChangePlanAccountView performed = completed.Accounts.Single(a => a.AccountId == scanned.Summary.Id);
        (performed.Outcome, performed.ActionId).Should().Match<(string?, Guid?)>(x => x.Item1 == "Performed" && x.Item2 != null);
        completed.Accounts.Single(a => a.AccountId == unscanned.Summary.Id).Should().Match<ChangePlanAccountView>(a => a.Outcome == "NotPerformed" && a.ActionId == null);
        completed.Permissions.Should().Match<ChangePlanPermissions>(p => !p.Check && !p.Complete && !p.Cancel && p.UploadEvidence);
        ActionView action = Ok(await s.Fx.Service.AccountAsync(s.Planner.Principal, s.Fx.Context, scanned.Summary.Id, _token)).Actions.Single();
        action.Should().Match<ActionView>(a => a.Id == performed.ActionId && a.ActionType == "GmsaConversion" && a.Result == "Performed"
            && a.RecordKind == "Intermediate" && a.ActualAt != null && a.VerifiedOn == null && a.EvidenceNote!.Contains("beyan"));
        action.References.Should().ContainSingle(r => r.Type == "OCO" && r.Number == "OCO-SYN-1001");
        Ok(await s.Fx.Service.AccountAsync(s.Planner.Principal, s.Fx.Context, unscanned.Summary.Id, _token)).Actions.Should().BeEmpty();

        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE EntityType = 'ChangePlan' AND EntityId = @id", new { id = approved.Id }))
            .Should().Be(6 + 3 + 2 + 2, "create/preview/approve per account, one per mark, evidence and completion per account");
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.ChangePlan%' AND DetailsJson LIKE @id",
            new { id = "%" + approved.Id.ToString("D") + "%" })).Should().Be(3 + 3 + 1 + 1);
        string json = System.Text.Json.JsonSerializer.Serialize(new object[] { completed, await ItemsAsync(s, approved.Id) });
        json.Should().NotContainEquivalentOf("password").And.NotContainEquivalentOf("parola");
    }

    [ServiceAccountSqlFact]
    public async Task Check_BeforeApproval_Is409_WritesNothing()
    {
        Setup s = await SetupAsync();
        ChangePlanView previewed = await PreviewedAsync(s, [await AccountAsync(s, "T10A")]);
        ChangePlanItemView item = (await ItemsAsync(s, previewed.Id)).Single();
        Counts before = await CountsAsync(s.Fx, previewed.Id);

        SaResult<ChangePlanView> early = await CheckAsync(s, s.Planner, previewed.Id, item, "Done");
        SaResult<ChangePlanView> evidence = await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, previewed.Id, "k.txt", "text/plain",
            _textFile, null, _token);
        SaResult<ChangePlanView> complete = await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, previewed.Id,
            new CompleteChangePlanRequest(previewed.Version, null), _token);

        (early.ErrorCode, early.Field).Should().Be((SaErrors.ChangePlanState, "planNotApproved"));
        early.Current.Should().BeOfType<ChangePlanView>("a 409 carries only the view the caller may already read");
        (evidence.ErrorCode, evidence.Field).Should().Be((SaErrors.ChangePlanState, "planNotApproved"));
        (complete.ErrorCode, complete.Field).Should().Be((SaErrors.ChangePlanState, "planNotApproved"));
        (await CountsAsync(s.Fx, previewed.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task Check_ItemOfAnotherPlan_Is404_AndWritesNothing()
    {
        Setup s = await SetupAsync();
        ChangePlanView mine = await ApprovedAsync(s, [await AccountAsync(s, "T6A")]);
        ChangePlanView other = await ApprovedAsync(s, [await AccountAsync(s, "T6B")]);
        ChangePlanItemView foreign = (await ItemsAsync(s, other.Id)).Single();
        Counts before = await CountsAsync(s.Fx, mine.Id), beforeOther = await CountsAsync(s.Fx, other.Id);

        foreach (Guid itemId in new[] { foreign.Id, Guid.NewGuid() })
        {
            SaResult<ChangePlanView> result = await s.Fx.Service.CheckChangePlanItemAsync(s.Planner.Principal, s.Fx.Context, mine.Id, itemId,
                new CheckChangePlanItemRequest("Done", null, null), _token);
            (result.ErrorCode, result.Field, result.Current).Should().Be((SaErrors.NotFound, (string?)null, (object?)null), "foreign and missing look the same");
        }

        (await CountsAsync(s.Fx, mine.Id)).Should().Be(before);
        (await CountsAsync(s.Fx, other.Id)).Should().Be(beforeOther);
    }

    [ServiceAccountSqlFact]
    public async Task Check_ItemOfOlderPreview_Is404()
    {
        Setup s = await SetupAsync();
        ChangePlanView first = await PreviewedAsync(s, [await AccountAsync(s, "T6O")]);
        ChangePlanItemView old = (await ItemsAsync(s, first.Id)).Single();
        ChangePlanView second = Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, first.Id, new PreviewChangePlanRequest(first.Version), _token));
        second.Preview!.Version.Should().Be(2);
        Ok(await ApproveAsync(s, s.Verifier, second));
        Counts before = await CountsAsync(s.Fx, first.Id);

        SaResult<ChangePlanView> result = await CheckAsync(s, s.Planner, first.Id, old, "Done");
        SaResult<IReadOnlyList<ChangePlanItemCheckView>> history = await s.Fx.Service.ChangePlanItemChecksAsync(s.Planner.Principal, s.Fx.Context, first.Id, old.Id, _token);

        (result.ErrorCode, result.Current).Should().Be((SaErrors.NotFound, (object?)null), "a row of a preview that was not approved is not the plan's checklist");
        history.ErrorCode.Should().Be(SaErrors.NotFound);
        (await CountsAsync(s.Fx, first.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task CheckHistory_ItemOfAnotherPlan_Is404()
    {
        Setup s = await SetupAsync();
        ChangePlanView mine = await ApprovedAsync(s, [await AccountAsync(s, "T6H")]);
        ChangePlanView other = await ApprovedAsync(s, [await AccountAsync(s, "T6K")]);
        ChangePlanItemView own = (await ItemsAsync(s, mine.Id)).Single(), foreign = (await ItemsAsync(s, other.Id)).Single();
        Ok(await CheckAsync(s, s.Planner, other.Id, foreign, "Done"));

        SaResult<IReadOnlyList<ChangePlanItemCheckView>> leaked = await s.Fx.Service.ChangePlanItemChecksAsync(s.Verifier.Principal, s.Fx.Context, mine.Id, foreign.Id, _token);
        SaResult<IReadOnlyList<ChangePlanItemCheckView>> empty = await s.Fx.Service.ChangePlanItemChecksAsync(s.Verifier.Principal, s.Fx.Context, mine.Id, own.Id, _token);

        (leaked.ErrorCode, leaked.Field, leaked.Current).Should().Be((SaErrors.NotFound, (string?)null, (object?)null));
        Ok(empty).Should().BeEmpty();
        Ok(await s.Fx.Service.ChangePlanItemChecksAsync(s.Verifier.Principal, s.Fx.Context, other.Id, foreign.Id, _token)).Should().ContainSingle();
    }

    [ServiceAccountSqlFact]
    public async Task Check_LatestWins_HistoryKept_StaleExpectedIs409()
    {
        Setup s = await SetupAsync();
        ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "T4L")]);
        ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();
        Ok(await CheckAsync(s, s.Planner, approved.Id, item, "Failed", "Sentetik: ilk deneme başarısız"));
        ChangePlanItemView failed = (await ItemsAsync(s, approved.Id)).Single();

        SaResult<ChangePlanView> stale = await CheckAsync(s, s.Second, approved.Id, item, "Done");
        Ok(await CheckAsync(s, s.Second, approved.Id, failed, "Done"));

        (stale.ErrorCode, stale.Field).Should().Be((SaErrors.ChangePlanState, "checkStale"), "the person did not see the mark made in between");
        IReadOnlyList<ChangePlanItemCheckView> history = Ok(await s.Fx.Service.ChangePlanItemChecksAsync(s.Planner.Principal, s.Fx.Context, approved.Id, item.Id, _token));
        history.Select(c => c.State).Should().Equal("Done", "Failed");
        (await ItemsAsync(s, approved.Id)).Single().Check!.State.Should().Be("Done", "the latest mark counts");
    }

    [ServiceAccountSqlFact]
    public async Task RolledBack_WithoutChange_Is409()
    {
        Setup s = await SetupAsync();
        ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "T10R")]);
        ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();
        Counts before = await CountsAsync(s.Fx, approved.Id);

        SaResult<ChangePlanView> noNote = await CheckAsync(s, s.Planner, approved.Id, item, "Skipped");
        SaResult<ChangePlanView> bogus = await CheckAsync(s, s.Planner, approved.Id, item, "Verified", "Sentetik");
        SaResult<ChangePlanView> rollback = await CheckAsync(s, s.Planner, approved.Id, item, "RolledBack", "Sentetik geri alma");

        (noNote.ErrorCode, noNote.Field).Should().Be((SaErrors.Invalid, "note"), "every state except Done needs a reason");
        (bogus.ErrorCode, bogus.Field).Should().Be((SaErrors.Invalid, "state"), "a mark is never a verification");
        (rollback.ErrorCode, rollback.Field).Should().Be((SaErrors.ChangePlanState, "nothingToRollBack"));
        (await CountsAsync(s.Fx, approved.Id)).Should().Be(before);
        Ok(await CheckAsync(s, s.Planner, approved.Id, item, "Done"));
        Ok(await CheckAsync(s, s.Planner, approved.Id, (await ItemsAsync(s, approved.Id)).Single(), "RolledBack", "Sentetik: eski kimliğe dönüldü"))
            .Checklist!.RolledBack.Should().Be(1);
    }

    [ServiceAccountSqlFact]
    public async Task Check_OutsideWindow_IsAccepted_AndFlagged()
    {
        Setup s = await SetupAsync();
        ChangePlanView previewed = await PreviewedAsync(s, [await AccountAsync(s, "T11")]);
        ChangePlanView approved = Ok(await ApproveAsync(s, s.Verifier, previewed, startHours: 24));
        ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();

        ChangePlanView marked = Ok(await CheckAsync(s, s.Planner, approved.Id, item, "Done"));

        marked.Checklist!.OutsideWindow.Should().Be(1);
        ChangePlanItemCheckView check = (await ItemsAsync(s, approved.Id)).Single().Check!;
        check.OutsideWindow.Should().BeTrue("the window starts tomorrow; the mark is kept and labelled, never refused");
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangeItemChecks WHERE ItemId = @id AND OutsideWindow = 1", new { id = item.Id })).Should().Be(1);
    }

    [ServiceAccountSqlFact]
    public async Task Complete_WithUncheckedItem_Is409_WritesNothing()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "T10C");
        await ScanAsync(s, account, ("SYN-APP01", "WindowsService", "SynSvc"), ("SYN-APP02", "WindowsService", "SynSvc2"));
        ChangePlanView approved = await ApprovedAsync(s, [account]);
        ChangePlanItemView[] items = await ItemsAsync(s, approved.Id);
        items.Should().HaveCount(2, "one row per scanned component");
        ChangePlanView started = Ok(await CheckAsync(s, s.Planner, approved.Id, items[0], "Done"));
        Counts before = await CountsAsync(s.Fx, approved.Id);
        int actions = await ActionCountAsync(s.Fx, account.Summary.Id);

        SaResult<ChangePlanView> early = await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest(started.Version, null), _token);
        SaResult<ChangePlanView> stale = await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest(approved.Version, null), _token);

        (early.ErrorCode, early.Field).Should().Be((SaErrors.ChangePlanState, "checklistIncomplete"));
        stale.ErrorCode.Should().Be(SaErrors.Conflict, "every mark changes the plan version");
        (await CountsAsync(s.Fx, approved.Id)).Should().Be(before);
        (await ActionCountAsync(s.Fx, account.Summary.Id)).Should().Be(actions);
    }

    [ServiceAccountSqlFact]
    public async Task Complete_RecordsPerformedNotVerified_KeepsRequestOpen_SkipsFailedAccount()
    {
        Setup s = await SetupAsync();
        (AccountDetail converted, AccountDetail failed, AccountDetail skipped) = (await AccountAsync(s, "T9A"), await AccountAsync(s, "T9B"), await AccountAsync(s, "T9C"));
        AccountDetail withRequest = Ok(await s.Fx.Service.CreateRequestAsync(s.Planner.Principal, s.Fx.Context, converted.Summary.Id,
            new CreateWorkRequest("GmsaConversion"), _token));
        Guid requestId = withRequest.Requests.Single().Id;
        ChangePlanView previewed = Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            (await DraftAsync(s, [new ChangePlanAccountInput(converted.Summary.Id, "gmsaSynT9a", requestId), Input(failed, "gmsaSynT9b"), Input(skipped, "gmsaSynT9c")])).Id,
            new PreviewChangePlanRequest((await PlanAsync(s, null)).Version), _token));
        ChangePlanView approved = Ok(await ApproveAsync(s, s.Verifier, previewed));
        IReadOnlyList<TransitionView> transitionsBefore = Ok(await s.Fx.Service.AccountAsync(s.Planner.Principal, s.Fx.Context, converted.Summary.Id, _token)).Transitions;
        foreach (ChangePlanItemView item in await ItemsAsync(s, approved.Id))
        {
            (string state, string? note) = item.AccountId == converted.Summary.Id ? ("Done", (string?)null)
                : item.AccountId == failed.Summary.Id ? ("Failed", "Sentetik hata") : ("Skipped", "Sentetik: bu bakımda yapılmadı");
            Ok(await CheckAsync(s, s.Planner, approved.Id, item, state, note));
        }

        ChangePlanView completed = Ok(await s.Fx.Service.CompleteChangePlanAsync(s.Second.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest((await PlanAsync(s, approved.Id)).Version, null), _token));

        completed.Accounts.Select(a => (a.AccountId, a.Outcome)).Should().BeEquivalentTo(new[]
            { (converted.Summary.Id, "Performed"), (failed.Summary.Id, "NotPerformed"), (skipped.Summary.Id, "NotPerformed") });
        AccountDetail after = Ok(await s.Fx.Service.AccountAsync(s.Planner.Principal, s.Fx.Context, converted.Summary.Id, _token));
        after.Actions.Should().ContainSingle().Which.Should().Match<ActionView>(a => a.Result == "Performed" && a.RequestId == requestId && !a.VerifiedClosure);
        after.Requests.Single().Status.Should().Be("Open", "closing the plan never closes the linked request (owner decision 7)");
        after.Transitions.Should().BeEquivalentTo(transitionsBefore, "completion does not touch the gMSA transition record");
        (await ActionCountAsync(s.Fx, failed.Summary.Id) + await ActionCountAsync(s.Fx, skipped.Summary.Id)).Should().Be(0);
        Ok(await s.Fx.Service.CreateChangePlanAsync(s.Verifier.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik yeniden plan", [Input(failed, "gmsaSynT9b")]), _token)).Status.Should().Be("Draft", "a closed plan releases its accounts");
    }

    [ServiceAccountSqlFact]
    public async Task ScopeShrinks_CheckCompleteEvidenceAndHistory_Are404()
    {
        Setup s = await SetupAsync();
        ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "T5C")]);
        ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();
        await RevokeAsync(s.Fx, s.Planner);
        Counts before = await CountsAsync(s.Fx, approved.Id);

        (await CheckAsync(s, s.Planner, approved.Id, item, "Done")).ErrorCode.Should().Be(SaErrors.NotFound);
        (await s.Fx.Service.ChangePlanItemChecksAsync(s.Planner.Principal, s.Fx.Context, approved.Id, item.Id, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, approved.Id, "k.txt", "text/plain", _textFile, null, _token))
            .ErrorCode.Should().Be(SaErrors.NotFound);
        (await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id, new CompleteChangePlanRequest(approved.Version, null), _token))
            .ErrorCode.Should().Be(SaErrors.NotFound);
        (await CountsAsync(s.Fx, approved.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task PlanEvidence_DownloadNeedsWholePlanScope_AndStaysOutOfAccountDetail()
    {
        Setup s = await SetupAsync();
        AccountDetail first = await AccountAsync(s, "T6E");
        Guid otherOrg = await s.Fx.OrganizationAsync("SYN PLAN KANIT ORG " + s.Fx.Suffix);
        foreach (SynUser user in new[] { s.Planner, s.Verifier })
        {
            await s.Fx.GrantAsync(user, ScopeKind.Organization, otherOrg);
        }

        AccountDetail second = Ok(await s.Fx.Service.CreateAccountAsync(s.Planner.Principal, s.Fx.Context,
            new CreateAccountRequest("SYN_PLAN_KANIT_" + s.Fx.Suffix, "SYN", otherOrg, "Sentetik ikinci org hesabı"), _token));
        ChangePlanView approved = await ApprovedAsync(s, [first, second]);
        Guid evidenceId = Ok(await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, approved.Id, "plan.txt", "text/plain", _textFile,
            null, _token)).Evidence!.Single().Id;
        SynUser partial = await s.Fx.UserAsync(_all);
        await s.Fx.GrantAsync(partial, ScopeKind.Organization, s.Org);
        int downloads = await s.Fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action = 'ServiceAccount.EvidenceDownloaded' AND DetailsJson LIKE @id",
            new { id = "%" + evidenceId.ToString("D") + "%" });

        SaResult<EvidenceDownload> denied = await s.Fx.Service.EvidenceAsync(partial.Principal, s.Fx.Context, evidenceId, _token);
        SaResult<EvidenceDownload> missing = await s.Fx.Service.EvidenceAsync(partial.Principal, s.Fx.Context, Guid.NewGuid(), _token);

        (denied.ErrorCode, denied.Field, denied.Current).Should().Be((missing.ErrorCode, missing.Field, missing.Current), "outside the whole plan looks like missing");
        denied.ErrorCode.Should().Be(SaErrors.NotFound);
        foreach (AccountDetail account in new[] { first, second })
        {
            Ok(await s.Fx.Service.AccountAsync(s.Planner.Principal, s.Fx.Context, account.Summary.Id, _token)).Evidence
                .Should().NotContain(e => e.Id == evidenceId, "plan evidence is never listed on one account");
        }

        AccountDetail partialView = Ok(await s.Fx.Service.AccountAsync(partial.Principal, s.Fx.Context, first.Summary.Id, _token));
        System.Text.Json.JsonSerializer.Serialize(partialView).Should().NotContain("plan.txt", "plan evidence metadata stays with the plan, not in account history");
        partialView.History.Should().Contain(h => h.Action == "ChangePlanEvidenceAdded", "the account's own timeline still says evidence was added");

        Ok(await s.Fx.Service.EvidenceAsync(s.Verifier.Principal, s.Fx.Context, evidenceId, _token)).FileName.Should().Be("plan.txt");
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action = 'ServiceAccount.EvidenceDownloaded' AND DetailsJson LIKE @id",
            new { id = "%" + evidenceId.ToString("D") + "%" })).Should().Be(downloads + 1, "only the allowed download is audited as a download");
    }

    [ServiceAccountSqlFact]
    public async Task AccountEvidenceRoute_RefusesChangePlanOwner()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "T6R");
        ChangePlanView approved = await ApprovedAsync(s, [account]);
        int evidence = await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.Evidence WHERE OwnerEntityId = @id", new { id = approved.Id });

        SaResult<AccountDetail> refused = await s.Fx.Service.AddEvidenceAsync(s.Planner.Principal, s.Fx.Context, account.Summary.Id, "ChangePlan", approved.Id,
            "plan.txt", "text/plain", _textFile, null, _token);

        (refused.ErrorCode, refused.Field).Should().Be((SaErrors.Invalid, "ownerId"), "plan evidence goes only through the plan route");
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.Evidence WHERE OwnerEntityId = @id", new { id = approved.Id })).Should().Be(evidence);
    }

    [ServiceAccountSqlFact]
    public async Task PlanEvidence_StateRules_AndFileChecks()
    {
        Setup s = await SetupAsync();
        ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "T6S")]);
        ChangePlanView cancelledPlan = await ApprovedAsync(s, [await AccountAsync(s, "T6T")]);
        Ok(await s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, cancelledPlan.Id,
            new CancelChangePlanRequest(cancelledPlan.Version, "Sentetik iptal"), _token));

        (await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, cancelledPlan.Id, "k.txt", "text/plain", _textFile, null, _token))
            .Should().Match<SaResult<ChangePlanView>>(r => r.ErrorCode == SaErrors.ChangePlanState && r.Field == "planClosed");
        (await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, approved.Id, "k.pdf", "application/pdf", _textFile, null, _token))
            .Should().Match<SaResult<ChangePlanView>>(r => r.ErrorCode == SaErrors.Invalid && r.Field == "file", "the signature must match the type");
        (await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, approved.Id, "k.exe", "application/x-msdownload", _textFile, null, _token))
            .Field.Should().Be("file");
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.Evidence WHERE OwnerEntityId IN (@a, @b)", new { a = approved.Id, b = cancelledPlan.Id })).Should().Be(0);

        ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();
        Ok(await CheckAsync(s, s.Planner, approved.Id, item, "Done"));
        ChangePlanView completed = Ok(await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest((await PlanAsync(s, approved.Id)).Version, null), _token));
        Ok(await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, completed.Id, "dogrulama.txt", "text/plain", _textFile,
            "Sentetik doğrulama ekran özeti", _token)).Evidence.Should().ContainSingle("verification evidence may arrive after completion");
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentCompleteAndCancel_ExactlyOneWins()
    {
        Setup s = await SetupAsync();
        AccountDetail[] accounts = [.. await Task.WhenAll(Enumerable.Range(0, 10).Select(i => AccountAsync(s, "R" + i)))];
        foreach ((AccountDetail account, int round) in accounts.Select((a, i) => (a, i)))
        {
            ChangePlanView approved = await ApprovedAsync(s, [account]);
            ChangePlanView marked = Ok(await CheckAsync(s, s.Planner, approved.Id, (await ItemsAsync(s, approved.Id)).Single(), "Done"));
            Task<SaResult<ChangePlanView>> complete = Task.Run(() => s.Fx.Service.CompleteChangePlanAsync(s.Second.Principal, s.Fx.Context, approved.Id,
                new CompleteChangePlanRequest(marked.Version, null), _token));
            Task<SaResult<ChangePlanView>> cancel = Task.Run(() => s.Fx.Service.CancelChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
                new CancelChangePlanRequest(marked.Version, "Sentetik yarış iptali"), _token));
            SaResult<ChangePlanView>[] results = await Task.WhenAll(complete, cancel);

            results.Count(r => r.IsSuccess).Should().Be(1, $"round {round}: one command wins");
            results.Single(r => !r.IsSuccess).ErrorCode.Should().BeOneOf(SaErrors.ChangePlanState, SaErrors.Conflict);
            ChangePlanView final = await PlanAsync(s, approved.Id);
            final.Status.Should().Be(complete.Result.IsSuccess ? "Completed" : "Cancelled");
            final.Events[^1].ToStatus.Should().Be(final.Status);
            (await ActionCountAsync(s.Fx, account.Summary.Id)).Should().Be(complete.Result.IsSuccess ? 1 : 0, "a cancelled plan records no action");
        }

        s.Fx.SqlDiagnostics.Should().NotContain(d => d.Contains("Number=1205"), "no deadlock victim");
        s.Fx.SqlDiagnostics.Should().BeEmpty();
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentChecksOnOneItem_OneWins_OtherIsCheckStale()
    {
        Setup s = await SetupAsync();
        for (int round = 0; round < 5; round++)
        {
            ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "C" + round)]);
            ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();
            SaResult<ChangePlanView>[] results = await Task.WhenAll(Task.Run(() => CheckAsync(s, s.Planner, approved.Id, item, "Done")),
                Task.Run(() => CheckAsync(s, s.Second, approved.Id, item, "Failed", "Sentetik eşzamanlı işaret")));

            results.Count(r => r.IsSuccess).Should().Be(1, $"round {round}");
            results.Single(r => !r.IsSuccess).Should().Match<SaResult<ChangePlanView>>(r => r.ErrorCode == SaErrors.ChangePlanState && r.Field == "checkStale");
            (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangeItemChecks WHERE ItemId = @id", new { id = item.Id })).Should().Be(1);
            (await PlanAsync(s, approved.Id)).Events.Count(e => e.Event == "Started").Should().Be(1);
        }

        s.Fx.SqlDiagnostics.Should().BeEmpty();
    }

    [ServiceAccountSqlFact]
    public async Task CheckAfterComplete_IsPlanClosed()
    {
        Setup s = await SetupAsync();
        ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "T4C")]);
        ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();
        Ok(await CheckAsync(s, s.Planner, approved.Id, item, "Done"));
        Ok(await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest((await PlanAsync(s, approved.Id)).Version, null), _token));
        Counts before = await CountsAsync(s.Fx, approved.Id);

        SaResult<ChangePlanView> late = await CheckAsync(s, s.Planner, approved.Id, (await ItemsAsync(s, approved.Id)).Single(), "RolledBack", "Sentetik geç işaret");
        SaResult<ChangePlanView> again = await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest((await PlanAsync(s, approved.Id)).Version, null), _token);

        (late.ErrorCode, late.Field).Should().Be((SaErrors.ChangePlanState, "planClosed"));
        (again.ErrorCode, again.Field).Should().Be((SaErrors.ChangePlanState, "planClosed"), "completion is not replayed");
        (await CountsAsync(s.Fx, approved.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task InvalidInput_Is400_AndWritesNothing()
    {
        Setup s = await SetupAsync();
        ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "V2")]);
        ChangePlanItemView item = (await ItemsAsync(s, approved.Id)).Single();
        Counts before = await CountsAsync(s.Fx, approved.Id);

        (await CheckAsync(s, s.Planner, approved.Id, item, "Failed", new string('x', ChangePlanRules.MaxReason + 1))).Field.Should().Be("note");
        (await CheckAsync(s, s.Planner, approved.Id, item, "done", null)).Field.Should().Be("state", "codes are exact");
        (await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id,
            new CompleteChangePlanRequest(approved.Version, new string('x', ChangePlanRules.MaxReason + 1)), _token)).Field.Should().Be("note");
        SynUser viewer = await s.Fx.UserAsync(ServiceAccountCapabilities.View);
        await s.Fx.GrantAsync(viewer, ScopeKind.Organization, s.Org);
        (await CheckAsync(s, viewer, approved.Id, item, "Done")).ErrorCode.Should().Be(SaErrors.Forbidden);

        (await CountsAsync(s.Fx, approved.Id)).Should().Be(before);
    }

    [ServiceAccountSqlFact]
    public async Task Complete_FailureBeforeCommit_LeavesNoActionNoEvent()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "F2");
        ChangePlanView approved = await ApprovedAsync(s, [account]);
        ChangePlanView marked = Ok(await CheckAsync(s, s.Planner, approved.Id, (await ItemsAsync(s, approved.Id)).Single(), "Done"));
        Counts before = await CountsAsync(s.Fx, approved.Id);
        string trigger = "TR_SaTest_FailCompleteAudit_" + s.Fx.Suffix;
        await using SqlConnection connection = s.Fx.Connection();
        await connection.OpenAsync();
        // Test database only: the plan's completion audit (written last) fails, i.e. after the actions and the event are written.
        await connection.ExecuteAsync($"""
            CREATE TRIGGER audit.{trigger} ON audit.AuditLog AFTER INSERT AS
            BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Action = 'ServiceAccount.ChangePlanCompleted' AND DetailsJson LIKE N'%{approved.Id:D}%')
                THROW 51399, 'Injected audit failure.', 1; END;
            """);
        SaResult<ChangePlanView> failed;
        try
        {
            failed = await s.Fx.Service.CompleteChangePlanAsync(s.Planner.Principal, s.Fx.Context, approved.Id, new CompleteChangePlanRequest(marked.Version, null), _token);
        }
        finally
        {
            await connection.ExecuteAsync($"DROP TRIGGER audit.{trigger};");
        }

        failed.ErrorCode.Should().Be(SaErrors.Unavailable);
        (await PlanAsync(s, approved.Id)).Status.Should().Be("InProgress");
        (await CountsAsync(s.Fx, approved.Id)).Should().Be(before);
        (await ActionCountAsync(s.Fx, account.Summary.Id)).Should().Be(0, "no action survives a failed completion");
        s.Fx.SqlDiagnostics.Should().ContainSingle().Which.Should().Contain("Number=51399");
    }

    [ServiceAccountSqlFact]
    public async Task PlanEvidence_FollowsMigration034_503WhenMissing()
    {
        Setup s = await SetupAsync();
        ChangePlanView approved = await ApprovedAsync(s, [await AccountAsync(s, "M34")]);
        bool installed = await s.Fx.CountAsync("""
            SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner'
              AND definition LIKE N'%''ChangePlan''%'
            """) == 1;

        SaResult<ChangePlanView> result = await s.Fx.Service.AddChangePlanEvidenceAsync(s.Planner.Principal, s.Fx.Context, approved.Id, "k.txt", "text/plain",
            _textFile, null, _token);

        // Run against a harness copy through 034 (normal) and through 033 (-ThroughMigration 33) to see both answers.
        if (installed)
        {
            Ok(result).Evidence.Should().ContainSingle();
        }
        else
        {
            result.ErrorCode.Should().Be(SaErrors.ChangePlanEvidenceNotInstalled);
            (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.Evidence WHERE OwnerEntityId = @id", new { id = approved.Id })).Should().Be(0);
            s.Fx.SqlDiagnostics.Should().BeEmpty("a missing migration is a known state, not a persistence failure");
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------

    private sealed record Setup(ServiceAccountSqlFixture Fx, SynUser Planner, SynUser Verifier, SynUser Second, Guid Org)
    {
        public Guid? LastPlan { get; set; }
    }

    private sealed record Counts(int Checks, int Events, int History, int Audit, int Evidence, string Status);

    private static async Task<Setup> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN KONTROL ORG " + fx.Suffix);
        SynUser planner = await fx.UserAsync(_all), verifier = await fx.UserAsync(_all), second = await fx.UserAsync(_all);
        foreach (SynUser user in new[] { planner, verifier, second })
        {
            await fx.GrantAsync(user, ScopeKind.Organization, org);
        }

        return new Setup(fx, planner, verifier, second, org);
    }

    private static async Task<AccountDetail> AccountAsync(Setup s, string label) =>
        Ok(await s.Fx.Service.CreateAccountAsync(s.Planner.Principal, s.Fx.Context,
            new CreateAccountRequest($"SYN_KONTROL_{label}_{s.Fx.Suffix}", "SYN", s.Org, "Sentetik kontrol listesi hesabı"), _token));

    private static async Task ScanAsync(Setup s, AccountDetail account, params (string Server, string Type, string Component)[] components)
    {
        string searched = $"SYN\\{account.Summary.AccountName}";
        byte[] file = ServiceAccountUsageScanBatchSqlTests.Discovery([searched], [.. components.Select(c => (c.Server, c.Type, c.Component, searched))]);
        UsageScanBatchResult result = Ok(await s.Fx.Service.AttachUsageScanToAccountsAsync(s.Planner.Principal, s.Fx.Context, [account.Summary.Id],
            "plan-scan.json", file, _statement, _token));
        result.Results.Single().Outcome.Should().Be("Attached");
    }

    private static ChangePlanAccountInput Input(AccountDetail account, string name) => new(account.Summary.Id, name);

    private static async Task<ChangePlanView> DraftAsync(Setup s, IReadOnlyList<ChangePlanAccountInput> accounts)
    {
        ChangePlanView draft = Ok(await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik kontrol planı " + s.Fx.Suffix, accounts), _token));
        s.LastPlan = draft.Id;
        return draft;
    }

    private static async Task<ChangePlanView> PreviewedAsync(Setup s, AccountDetail[] accounts)
    {
        ChangePlanView draft = await DraftAsync(s, [.. accounts.Select((a, i) => Input(a, "gmsaSynK" + i))]);
        return Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, draft.Id, new PreviewChangePlanRequest(draft.Version), _token));
    }

    /// <summary>Planned and previewed by the planner, approved by the verifier with a window around now.</summary>
    private static async Task<ChangePlanView> ApprovedAsync(Setup s, AccountDetail[] accounts) =>
        Ok(await ApproveAsync(s, s.Verifier, await PreviewedAsync(s, accounts), startHours: -1));

    private static Task<SaResult<ChangePlanView>> ApproveAsync(Setup s, SynUser approver, ChangePlanView previewed, int startHours = 1)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(startHours);
        return s.Fx.Service.ApproveChangePlanAsync(approver.Principal, s.Fx.Context, previewed.Id, new ApproveChangePlanRequest(previewed.Preview!.Version,
            previewed.Preview.Sha256, "oco-syn-1001", start, start.AddHours(4), "Sentetik onay gerekçesi"), _token);
    }

    /// <summary>The plan (or the last plan this setup created) as the planner sees it.</summary>
    private static async Task<ChangePlanView> PlanAsync(Setup s, Guid? id) =>
        Ok(await s.Fx.Service.ChangePlanAsync(s.Planner.Principal, s.Fx.Context, id ?? s.LastPlan!.Value, _token));

    private static async Task<ChangePlanItemView[]> ItemsAsync(Setup s, Guid planId) =>
        [.. Ok(await s.Fx.Service.ChangePlanItemsAsync(s.Planner.Principal, s.Fx.Context, planId, 1, 200, _token)).Items];

    /// <summary>Marks a row expecting the latest mark the caller saw on <paramref name="item"/>.</summary>
    private static Task<SaResult<ChangePlanView>> CheckAsync(Setup s, SynUser user, Guid planId, ChangePlanItemView item, string state, string? note = null) =>
        s.Fx.Service.CheckChangePlanItemAsync(user.Principal, s.Fx.Context, planId, item.Id, new CheckChangePlanItemRequest(state, note, item.Check?.Id), _token);

    private static async Task<Counts> CountsAsync(ServiceAccountSqlFixture fx, Guid planId) => new(
        await fx.CountAsync("""
            SELECT COUNT(*) FROM svcacct.ChangeItemChecks c JOIN svcacct.ChangePlanItems i ON i.Id = c.ItemId
            JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId WHERE v.PlanId = @planId
            """, new { planId }),
        await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangePlanEvents WHERE PlanId = @planId", new { planId }),
        await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE EntityType = 'ChangePlan' AND EntityId = @planId", new { planId }),
        await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE DetailsJson LIKE @id", new { id = "%" + planId.ToString("D") + "%" }),
        await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Evidence WHERE OwnerEntityId = @planId", new { planId }),
        await StatusAsync(fx, planId));

    private static async Task<string> StatusAsync(ServiceAccountSqlFixture fx, Guid planId)
    {
        await using SqlConnection connection = fx.Connection();
        return await connection.ExecuteScalarAsync<string>("SELECT Status FROM svcacct.ChangePlans WHERE Id = @planId", new { planId }) ?? string.Empty;
    }

    private static Task<int> ActionCountAsync(ServiceAccountSqlFixture fx, Guid accountId) =>
        fx.CountAsync("SELECT COUNT(*) FROM svcacct.ActionEvents WHERE AccountId = @accountId", new { accountId });

    private static async Task RevokeAsync(ServiceAccountSqlFixture fx, SynUser user)
    {
        await using SqlConnection connection = fx.Connection();
        await connection.ExecuteAsync("""
            UPDATE svcacct.ScopeGrants SET RevokedAt = SYSUTCDATETIME(), RevokedBy = NEWID(), RevokeReason = N'Sentetik kapsam daralması'
            WHERE UserId = @id AND RevokedAt IS NULL;
            """, new { id = user.User.Id });
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
