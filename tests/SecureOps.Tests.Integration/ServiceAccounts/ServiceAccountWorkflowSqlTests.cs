using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

public sealed class ServiceAccountWorkflowSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];

    [ServiceAccountSqlFact]
    public async Task MultipleRequests_ActionVerification_AndClosureRules()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "MULTI");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new CreateWorkRequest("PasswordChange", PlanStart: new(2026, 9, 30), PlanEnd: new(2026, 9, 30)), _token));
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("Deletion"), _token));
        account.Requests.Should().HaveCount(2).And.OnlyContain(r => r.Status == "Open");
        account.Summary.OpenRequests.Should().Be(2);
        Guid password = account.Requests.Single(r => r.ActionType == "PasswordChange").Id;
        Guid deletion = account.Requests.Single(r => r.ActionType == "Deletion").Id;

        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", password, ActualOn: new(2026, 9, 10), EvidenceNote: "Sentetik değişim kaydı"), _token));
        account.Requests.Should().OnlyContain(r => r.Status == "Open", "adding an action never closes a request");
        ActionView action = account.Actions.Single();
        (await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, action.Id, new VerifyActionRequest(action.Version, new(2026, 9, 9), VerificationNote: "kontrol"), _token))
            .Field.Should().Be(ServiceAccountRules.Errors.VerificationBeforeAction);
        account = Ok(await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, action.Id, new VerifyActionRequest(action.Version, new(2026, 9, 11), VerificationNote: "kontrol edildi"), _token));
        account.Actions.Should().ContainSingle("verification updates the same action identity").Which.Result.Should().Be("Verified");
        account.Actions.Single().VerifiedClosure.Should().BeFalse("an intermediate step is not a closure");
        account = Ok(await fx.Service.CloseRequestAsync(coordinator.Principal, fx.Context, password,
            new CloseWorkRequest(account.Requests.Single(r => r.Id == password).Version, "Completed"), _token));
        account.Requests.Single(r => r.Id == password).Status.Should().Be("Closed");
        account.Requests.Single(r => r.Id == deletion).Status.Should().Be("Open", "closing one request never closes another");

        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("Deletion", "Performed", "Closure", deletion, ActualOn: new(2026, 9, 12), EvidenceNote: "silme bildirimi"), _token));
        ActionView delete = account.Actions.Single(a => a.ActionType == "Deletion");
        (await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, delete.Id, new VerifyActionRequest(delete.Version, new(2026, 9, 13), VerificationNote: "kontrol"), _token))
            .Field.Should().Be(ServiceAccountRules.Errors.OrRequiredForDeletion);
        account = Ok(await fx.Service.UpdateActionAsync(coordinator.Principal, fx.Context, delete.Id,
            new UpdateActionRequest(delete.Version, AddReferences: [new SaExternalRef("OR", "OR-SYN-" + fx.Suffix)]), _token));
        delete = account.Actions.Single(a => a.ActionType == "Deletion");
        (await fx.Service.CloseRequestAsync(coordinator.Principal, fx.Context, deletion,
            new CloseWorkRequest(account.Requests.Single(r => r.Id == deletion).Version, "Completed"), _token)).Field.Should().Be(ServiceAccountRules.Errors.VerifiedClosureRequired);
        SaResult<AccountDetail> verified = await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, delete.Id,
            new VerifyActionRequest(delete.Version, new(2026, 9, 13), VerificationNote: "AD'de yok, OR kapalı"), _token);
        verified.ErrorCode.Should().BeNull($"safe SQL diagnostics: {string.Join("; ", fx.SqlDiagnostics)}");
        account = verified.Value!;
        account.Actions.Single(a => a.Id == delete.Id).VerifiedClosure.Should().BeTrue();
        account.Summary.LifecycleState.Should().Be("ClosureVerified");
        account.Actions.Should().HaveCount(2);
        Ok(await fx.Service.CloseRequestAsync(coordinator.Principal, fx.Context, deletion,
            new CloseWorkRequest(account.Requests.Single(r => r.Id == deletion).Version, "Completed"), _token)).Requests.Should().OnlyContain(r => r.Status == "Closed");
        account.History.Should().Contain(h => h.Action == "ActionVerified");
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentUpdates_OneWins_TheOtherGets409WithCurrentValues()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "RACE");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("Review"), _token));
        RequestView request = account.Requests.Single();
        SaResult<AccountDetail>[] results = await Task.WhenAll(
            fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id, new UpdateWorkRequest(request.Version, Notes: "ilk değişiklik"), _token),
            fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id, new UpdateWorkRequest(request.Version, Notes: "ikinci değişiklik"), _token));
        results.Count(r => r.IsSuccess).Should().Be(1);
        SaResult<AccountDetail> conflict = results.Single(r => !r.IsSuccess);
        conflict.ErrorCode.Should().Be(SaErrors.Conflict);
        string winner = results.Single(r => r.IsSuccess).Value!.Requests.Single().Notes!;
        ((AccountDetail)conflict.Current!).Requests.Single().Notes.Should().Be(winner, "the 409 carries the current value and the first change is not lost");

        AccountDetail refreshed = Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, account.Summary.Id, _token));
        refreshed = Ok(await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id, new UpdateWorkRequest(refreshed.Requests.Single().Version,
            PlanStart: new(2026, 10, 1), PlanEnd: new(2026, 10, 3)), _token));
        refreshed.Requests.Single().Notes.Should().Be(winner, "a blank field in a later update never erases the stored value");
        (await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id, new UpdateWorkRequest(refreshed.Requests.Single().Version, ClearFields: ["notes"]), _token))
            .Field.Should().Be("reason", "clearing is a separate reasoned action");
    }

    [ServiceAccountSqlFact]
    public async Task OtherTeam_CannotReadUpdateListOrDownload()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        Guid teamA = await fx.TeamAsync("SYN A " + fx.Suffix, null), teamB = await fx.TeamAsync("SYN B " + fx.Suffix, null);
        AccountDetail owned = await CreateAccountAsync(fx, coordinator, org, "OWNEDB");
        owned = Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, owned.Summary.Id,
            new OwnershipChangeRequest(owned.Summary.Version, teamB, null, "Confirm", "Sentetik sahiplik kararı"), _token));
        owned = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, owned.Summary.Id, new CreateWorkRequest("Review", TargetTeamId: teamB), _token));
        owned = Ok(await fx.Service.AddEvidenceAsync(coordinator.Principal, fx.Context, owned.Summary.Id, "Account", owned.Summary.Id, "kanit.txt", "text/plain",
            "sentetik kanıt"u8.ToArray(), "Sentetik", _token));
        Guid evidence = owned.Evidence.Single().Id;

        SynUser memberA = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work, ServiceAccountCapabilities.Report);
        await fx.GrantAsync(memberA, ScopeKind.Team, team: teamA);
        (await fx.Service.AccountAsync(memberA.Principal, fx.Context, owned.Summary.Id, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.UpdateRequestAsync(memberA.Principal, fx.Context, owned.Requests.Single().Id,
            new UpdateWorkRequest(owned.Requests.Single().Version, Notes: "izinsiz"), _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.EvidenceAsync(memberA.Principal, fx.Context, evidence, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        Ok(await fx.Service.AccountsAsync(memberA.Principal, fx.Context, new AccountListQuery(Search: owned.Summary.AccountName), _token)).Total.Should().Be(0);

        SynUser memberB = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work);
        await fx.GrantAsync(memberB, ScopeKind.Team, team: teamB);
        AccountDetail seen = Ok(await fx.Service.AccountAsync(memberB.Principal, fx.Context, owned.Summary.Id, _token));
        seen.Permissions.Work.Should().BeTrue();
        seen.Permissions.AssignTeam.Should().BeFalse();
        (await fx.Service.EvidenceAsync(memberB.Principal, fx.Context, evidence, _token)).Value!.Content.Should().Equal("sentetik kanıt"u8.ToArray());
        (await fx.Service.ChangeOwnershipAsync(memberB.Principal, fx.Context, owned.Summary.Id,
            new OwnershipChangeRequest(seen.Summary.Version, teamA, null, "Confirm", "başka ekibe taşıma"), _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a AND Action = 'ServiceAccount.EvidenceDownloaded'",
            new { a = memberB.User.Id.ToString("D") })).Should().Be(1);
    }

    [ServiceAccountSqlFact]
    public async Task OneMailManyAccounts_CountsOnce_ProviderIdDeduplicates_SameSubjectMailsAreKept()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        List<Guid> accounts = [];
        for (int i = 0; i < 10; i++)
        {
            accounts.Add((await CreateAccountAsync(fx, coordinator, org, "MAIL" + i)).Summary.Id);
        }

        string provider = "<syn-" + fx.Suffix + "@example.invalid>";
        CommunicationSaveResult first = Ok(await fx.Service.CreateCommunicationAsync(coordinator.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", accounts[..8], new(2026, 9, 17), Subject: "Linux hesapları", ProviderMessageId: provider), _token));
        CommunicationSaveResult again = Ok(await fx.Service.CreateCommunicationAsync(coordinator.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", accounts, new(2026, 9, 17), Subject: "Linux hesapları", ProviderMessageId: provider), _token));
        again.ExistingMessage.Should().BeTrue();
        again.Communication.Id.Should().Be(first.Communication.Id);
        again.AddedLinks.Should().Be(2);
        CommunicationSaveResult other = Ok(await fx.Service.CreateCommunicationAsync(coordinator.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", [accounts[0]], new(2026, 9, 17), Subject: "Linux hesapları"), _token));
        other.Communication.Id.Should().NotBe(first.Communication.Id, "a different real mail with the same subject is kept");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.CommunicationAccounts WHERE CommunicationId = @Id", new { first.Communication.Id })).Should().Be(10);
        (await fx.CountAsync("SELECT COUNT(DISTINCT ca.CommunicationId) FROM svcacct.CommunicationAccounts ca WHERE ca.AccountId IN @accounts", new { accounts })).Should().Be(2);
    }

    [ServiceAccountSqlFact]
    public async Task Identity_DomainsAreDistinct_AndSameNamedVerifiedPeopleAreAmbiguous()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        string name = $"SYN{fx.Suffix}_SVC";
        AccountDetail a = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest(name, "CORP-A.EXAMPLE", org, "sentetik"), _token));
        AccountDetail b = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest(name, "CORP-B.EXAMPLE", org, "sentetik"), _token));
        a.Summary.Id.Should().NotBe(b.Summary.Id);
        (await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest(name.ToLowerInvariant(), "corp-a.example", org, "tekrar"), _token))
            .Field.Should().Be("accountName");

        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work, ServiceAccountCapabilities.Administer);
        string person = "Ortak İsim " + fx.Suffix;
        Guid p1 = Ok(await fx.Service.CreatePersonAsync(admin.Principal, fx.Context, new CreatePersonRequest(person), _token));
        Guid p2 = Ok(await fx.Service.CreatePersonAsync(admin.Principal, fx.Context, new CreatePersonRequest(person.ToUpper(new System.Globalization.CultureInfo("tr-TR"))), _token));
        IReadOnlyList<PersonView> people = Ok(await fx.Service.PeopleAsync(admin.Principal, fx.Context, person, _token));
        Ok(await fx.Service.VerifyPersonAsync(admin.Principal, fx.Context, p1, new VerifyPersonRequest(people.Single(p => p.Id == p1).Version, $"p1.{fx.Suffix}@example.invalid", null, "Sentetik dizin kanıtı"), _token));
        Ok(await fx.Service.VerifyPersonAsync(admin.Principal, fx.Context, p2, new VerifyPersonRequest(people.Single(p => p.Id == p2).Version, $"p2.{fx.Suffix}@example.invalid", null, "Sentetik dizin kanıtı"), _token));

        byte[] workbook = SyntheticWorkbook.Create([("Hesap_Bilgileri", [
            (6, new SynCell?[] { "Servis Hesabı", "Sorumlu Ekip", "Sorumlu Kişi", "Rapor Organizasyonu" }),
            (7, [$"SYN{fx.Suffix}_AMB", null, person.ToUpperInvariant(), "SYN WF ORG " + fx.Suffix])])]);
        ImportBatchView staged = Ok(await fx.Service.StageImportAsync(coordinator.Principal, fx.Context,
            new StageImportRequest(ServiceAccountImportProfiles.LegacyWorkbook, null, "sentetik"), "k.xlsx", "x", workbook, _token));
        ImportRowView ownership = Ok(await fx.Service.ImportRowsAsync(coordinator.Principal, fx.Context, staged.Id, null, StagedKinds.Ownership, false, 1, 50, _token)).Items.Single();
        ownership.RequiresDecision.Should().BeTrue("two verified people share the name; a name never proves one identity");
        ownership.Candidates.Select(c => c.Id).Should().BeEquivalentTo([p1, p2]);
    }

    [ServiceAccountSqlFact]
    public async Task FindingsAndHandover_AreNotCompletedWork_AndAcceptanceDoesNotChangeOwner()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        Guid target = await fx.TeamAsync("SYN HEDEF " + fx.Suffix, org);
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "HAND");
        account = Ok(await fx.Service.CreateFindingAsync(coordinator.Principal, fx.Context, new CreateFindingRequest(account.Summary.Id, "Unreachable", "NoMatch",
            Server: "srv-syn-01", ComponentType: "Windows Service"), _token));
        account.Findings.Single().Gaps.Should().Contain(g => g.Contains("kullanılmadığı sonucu çıkarılamaz", StringComparison.Ordinal));
        account.Actions.Should().BeEmpty();
        account = Ok(await fx.Service.CreateHandoverAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new CreateHandoverRequest(target, ProposedOn: new(2026, 9, 15), TrackGmsa: true), _token));
        HandoverView handover = account.Handovers.Single();
        handover.Status.Should().Be("Proposed");
        account.Transitions.Single().Suitability.Should().Be("Unknown");
        (await fx.Service.DecideHandoverAsync(coordinator.Principal, fx.Context, handover.Id, new HandoverDecisionRequest(handover.Version, "Accept", new(2026, 9, 14), "erken"), _token))
            .Field.Should().Be("decidedOn", "acceptance cannot precede the proposal");
        account = Ok(await fx.Service.DecideHandoverAsync(coordinator.Principal, fx.Context, handover.Id,
            new HandoverDecisionRequest(handover.Version, "Accept", new(2026, 9, 16), "Hedef ekip kabul yazısı (sentetik)"), _token));
        account.Handovers.Single().Status.Should().Be("Accepted");
        account.Summary.OwnerTeam.Should().BeNull("acceptance does not change the owner team by itself");
        account.Transitions.Single().Suitability.Should().Be("Unknown", "acceptance is not gMSA suitability");
        (await fx.Service.UpdateTransitionAsync(coordinator.Principal, fx.Context, account.Transitions.Single().Id,
            new TransitionUpdateRequest(account.Transitions.Single().Version, "Eligible"), _token)).Field.Should().Be("decisionNote");
    }

    [ServiceAccountSqlFact]
    public async Task LinuxCohort_EightPlansAndTwoClosureReviews_DoNotCreateCompletedWork()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        DateOnly first = new(2026, 9, 30);
        for (int index = 0; index < 10; index++)
        {
            AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "LINUX" + index);
            CreateWorkRequest request = index < 8
                ? new("PasswordChange", PlanStart: first.AddDays(index * 13), PlanEnd: first.AddDays(index * 13), Notes: "Synthetic planned change")
                : new("Review", Notes: "Synthetic closure review; no deletion or verification evidence");
            account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, request, _token));
            account.Requests.Should().ContainSingle().Which.Status.Should().Be("Open");
            account.Actions.Should().BeEmpty();
            account.Summary.LifecycleState.Should().NotBe("ClosureVerified");
        }

        string prefix = "SYN" + fx.Suffix + "_LINUX%";
        (await fx.CountAsync("""
            SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN svcacct.Accounts a ON a.Id = r.AccountId
            WHERE a.AccountName LIKE @prefix AND r.ActionType = 'PasswordChange' AND r.Status = 'Open'
              AND r.PlanStart BETWEEN '2026-09-30' AND '2026-12-30';
            """, new { prefix })).Should().Be(8);
        (await fx.CountAsync("""
            SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN svcacct.Accounts a ON a.Id = r.AccountId
            WHERE a.AccountName LIKE @prefix AND r.ActionType = 'Review' AND r.Status = 'Open';
            """, new { prefix })).Should().Be(2);
        (await fx.CountAsync("""
            SELECT COUNT(*) FROM svcacct.ActionEvents e JOIN svcacct.Accounts a ON a.Id = e.AccountId
            WHERE a.AccountName LIKE @prefix AND e.Result IN ('Performed','Verified');
            """, new { prefix })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE AccountName LIKE @prefix AND LifecycleState = 'ClosureVerified';",
            new { prefix })).Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task ParticipantTeam_WorksOnlyOnItsOwnRequest_AndLosesAccessWhenItCloses()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        Guid ownerTeam = await fx.TeamAsync("SYN SAHIP " + fx.Suffix, org), helperTeam = await fx.TeamAsync("SYN YARDIM " + fx.Suffix, null);
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "PART");
        account = Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new OwnershipChangeRequest(account.Summary.Version, ownerTeam, null, "Confirm", "Sentetik sahiplik kararı"), _token));
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new CreateWorkRequest("PasswordChange", TargetTeamId: helperTeam), _token));
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new CreateWorkRequest("Review", TargetTeamId: ownerTeam), _token));
        Guid accountId = account.Summary.Id;
        Guid mine = account.Requests.Single(r => r.ActionType == "PasswordChange").Id, theirs = account.Requests.Single(r => r.ActionType == "Review").Id;

        SynUser member = await fx.UserAsync(_all);
        await fx.GrantAsync(member, ScopeKind.Team, team: helperTeam);
        AccountDetail seen = Ok(await fx.Service.AccountAsync(member.Principal, fx.Context, accountId, _token));
        seen.Permissions.Basis.Should().Be(ServiceAccountAccessBasis.Participant);
        seen.Permissions.ParticipantRequestIds.Should().Equal(mine);
        seen.Permissions.Work.Should().BeFalse("visibility through an assigned request is not account-wide authority");
        seen.Permissions.Verify.Should().BeFalse();
        seen.Permissions.AssignPerson.Should().BeFalse();
        seen.Permissions.UploadEvidence.Should().BeTrue();

        RequestView own = seen.Requests.Single(r => r.Id == mine);
        seen = Ok(await fx.Service.UpdateRequestAsync(member.Principal, fx.Context, mine,
            new UpdateWorkRequest(own.Version, Notes: "ekip notu", PlanStart: new(2026, 10, 1), PlanEnd: new(2026, 10, 2)), _token));
        own = seen.Requests.Single(r => r.Id == mine);
        own.Notes.Should().Be("ekip notu");
        (await fx.Service.UpdateRequestAsync(member.Principal, fx.Context, mine, new UpdateWorkRequest(own.Version, TargetTeamId: ownerTeam), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, "a participant cannot move its request to another team");
        (await fx.Service.UpdateRequestAsync(member.Principal, fx.Context, theirs,
            new UpdateWorkRequest(seen.Requests.Single(r => r.Id == theirs).Version, Notes: "izinsiz"), _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.Service.CloseRequestAsync(member.Principal, fx.Context, mine, new CloseWorkRequest(own.Version, "Cancelled", "izinsiz"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.Service.CreateRequestAsync(member.Principal, fx.Context, accountId, new CreateWorkRequest("Deletion"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, $"safe SQL diagnostics: {string.Join("; ", fx.SqlDiagnostics)}");
        (await fx.Service.UpdateAccountAsync(member.Principal, fx.Context, accountId, new UpdateAccountRequest(seen.Summary.Version, Notes: "izinsiz"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.Service.ChangeOwnershipAsync(member.Principal, fx.Context, accountId,
            new OwnershipChangeRequest(seen.Summary.Version, helperTeam, null, "Propose", "izinsiz"), _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.Service.CreateFindingAsync(member.Principal, fx.Context, new CreateFindingRequest(accountId, "Success", "Match"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.Service.ReportActionAsync(member.Principal, fx.Context, accountId,
            new ReportActionRequest("Review", "Performed", "Intermediate", theirs, ActualOn: new(2026, 9, 20), EvidenceNote: "izinsiz"), _token))
            .Field.Should().Be("requestId");
        (await fx.Service.ReportActionAsync(member.Principal, fx.Context, accountId,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", ActualOn: new(2026, 9, 20), EvidenceNote: "talepsiz"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, "a participant reports only against its own request");

        seen = Ok(await fx.Service.ReportActionAsync(member.Principal, fx.Context, accountId,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", mine, ActualOn: new(2026, 9, 20), EvidenceNote: "ekip bildirimi"), _token));
        ActionView performed = seen.Actions.Single();
        (await fx.Service.VerifyActionAsync(member.Principal, fx.Context, performed.Id,
            new VerifyActionRequest(performed.Version, new(2026, 9, 21), VerificationNote: "kendi doğrulaması"), _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.Service.VoidActionAsync(member.Principal, fx.Context, performed.Id, new VoidActionRequest(performed.Version, "izinsiz"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        seen = Ok(await fx.Service.AddEvidenceAsync(member.Principal, fx.Context, accountId, "Request", mine, "kanit.txt", "text/plain",
            "sentetik ekip kanıtı"u8.ToArray(), "Sentetik", _token));
        seen.Evidence.Should().ContainSingle();
        (await fx.Service.AddEvidenceAsync(member.Principal, fx.Context, accountId, "Account", accountId, "kanit.txt", "text/plain",
            "sentetik"u8.ToArray(), null, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);

        account = Ok(await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, performed.Id,
            new VerifyActionRequest(performed.Version, new(2026, 9, 21), VerificationNote: "sorumlu doğrulaması"), _token));
        account = Ok(await fx.Service.CloseRequestAsync(coordinator.Principal, fx.Context, mine,
            new CloseWorkRequest(account.Requests.Single(r => r.Id == mine).Version, "Completed"), _token));
        (await fx.Service.AccountAsync(member.Principal, fx.Context, accountId, _token)).ErrorCode
            .Should().Be(SaErrors.NotFound, "participation ends with the request");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @accountId AND ActorUserId = @user",
            new { accountId, user = member.User.Id })).Should().Be(3, "the participant's request update, action report and evidence are attributed to it");
    }

    [ServiceAccountSqlFact]
    public async Task ManualAccounts_StayProvisional_EvenWithATypedDomain()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail typed = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_DOM", "syn.example", org, "Sentetik test hesabı"), _token));
        typed.Summary.IdentityState.Should().Be("Provisional", "a typed name and domain are not a verified directory identity");
        AccountDetail bare = await CreateAccountAsync(fx, coordinator, org, "NODOM");
        bare = Ok(await fx.Service.UpdateAccountAsync(coordinator.Principal, fx.Context, bare.Summary.Id,
            new UpdateAccountRequest(bare.Summary.Version, Domain: "syn.example"), _token));
        bare.Summary.Domain.Should().Be("syn.example");
        bare.Summary.IdentityState.Should().Be("Provisional", "filling a missing domain does not verify the identity");
    }

    /// <summary>
    /// Deterministic reproduction of the recorded VerifyAction deadlock (error 1205 reported as persistence unavailable):
    /// an import commit holds its exclusive commit lock and serializable locks on an account row, a closure verification
    /// then updates the same account, and the commit afterwards updates that account too. Every module write must wait
    /// for the commit before taking row locks, so both sides complete.
    /// </summary>
    [ServiceAccountSqlFact]
    public async Task ClosureVerification_DuringImportCommit_WaitsInsteadOfDeadlocking()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "GATE");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("Deletion"), _token));
        Guid deletion = account.Requests.Single().Id;
        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("Deletion", "Performed", "Closure", deletion, ActualOn: new(2026, 9, 12), EvidenceNote: "silme bildirimi"), _token));
        ActionView delete = account.Actions.Single();
        account = Ok(await fx.Service.UpdateActionAsync(coordinator.Principal, fx.Context, delete.Id,
            new UpdateActionRequest(delete.Version, AddReferences: [new SaExternalRef("OR", "OR-GATE-" + fx.Suffix)]), _token));
        delete = account.Actions.Single();
        Guid accountId = account.Summary.Id;

        await using SqlConnection import = fx.Connection();
        await import.OpenAsync(_token);
        await using var transaction = (SqlTransaction)await import.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, _token);
        await import.ExecuteAsync("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = 'svcacct:import-commit', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @result < 0 THROW 51399, 'Synthetic commit lock unavailable.', 1;
            """, transaction: transaction);
        await import.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM svcacct.Accounts WHERE Id = @accountId;", new { accountId }, transaction);
        short importSession = await import.ExecuteScalarAsync<short>("SELECT CONVERT(smallint, @@SPID);", transaction: transaction);

        Task<SaResult<AccountDetail>> verify = fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, delete.Id,
            new VerifyActionRequest(delete.Version, new(2026, 9, 13), VerificationNote: "AD'de yok, OR kapalı"), _token);
        (await WaitUntilBlockedByAsync(fx, importSession)).Should().BeTrue("the verification must be waiting behind the synthetic commit");

        Func<Task> commitUpdate = () => import.ExecuteAsync("UPDATE svcacct.Accounts SET UpdatedAt = UpdatedAt WHERE Id = @accountId;", new { accountId }, transaction);
        await commitUpdate.Should().NotThrowAsync("the commit must not be chosen as a deadlock victim");
        await transaction.CommitAsync(_token);

        SaResult<AccountDetail> verified = await verify;
        verified.ErrorCode.Should().BeNull($"safe SQL diagnostics: {string.Join("; ", fx.SqlDiagnostics)}");
        verified.Value!.Summary.LifecycleState.Should().Be("ClosureVerified");
    }

    private static async Task<bool> WaitUntilBlockedByAsync(ServiceAccountSqlFixture fx, short session)
    {
        for (int attempt = 0; attempt < 150; attempt++)
        {
            if (await fx.CountAsync("SELECT COUNT(*) FROM sys.dm_os_waiting_tasks WHERE blocking_session_id = @session;", new { session }) > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return false;
    }

    private static async Task<(ServiceAccountSqlFixture Fx, SynUser Coordinator, Guid Org)> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN WF ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        return (fx, coordinator, org);
    }

    private static async Task<AccountDetail> CreateAccountAsync(ServiceAccountSqlFixture fx, SynUser user, Guid org, string name) =>
        Ok(await fx.Service.CreateAccountAsync(user.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_{name}", null, org, "Sentetik test hesabı"), _token));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
