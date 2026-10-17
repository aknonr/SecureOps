using Dapper;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// Scope checks on shared records and stored batches, checked against the current grants inside write transactions, and
/// re-evaluated close rules. Two disjoint synthetic organizations; synthetic names only.
/// </summary>
public sealed class ServiceAccountScopeHardeningSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];
    private static readonly string[] _importer = [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Import];
    private static readonly DateOnly _on = new(2026, 10, 1);

    [ServiceAccountSqlFact]
    public async Task StoredMail_IsReachableOnlyThroughItsOwnScope()
    {
        ServiceAccountSqlFixture fx = new();
        (Guid orgA, SynUser coordA, Guid accA) = await OrganizationWithAccountAsync(fx, "A");
        (Guid orgB, SynUser coordB, Guid accB) = await OrganizationWithAccountAsync(fx, "B");
        Guid teamA = await fx.TeamAsync("SYN TEAM A " + fx.Suffix, orgA);
        string provider = "<syn-scope-" + fx.Suffix + "@example.invalid>";
        CommunicationSaveResult recorded = Ok(await fx.Service.CreateCommunicationAsync(coordB.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", [accB], _on, Subject: "SYN konu", Summary: "SYN özet", ProviderMessageId: provider), _token));

        SaResult<CommunicationSaveResult> viaAccount = await fx.Service.CreateCommunicationAsync(coordA.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", [accA], _on, Subject: "başka", ProviderMessageId: provider), _token);
        viaAccount.ErrorCode.Should().Be(SaErrors.ProviderMessageIdUnavailable);
        viaAccount.Value.Should().BeNull();
        SaResult<CommunicationSaveResult> viaTeam = await fx.Service.CreateCommunicationAsync(coordA.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", [], _on, ContactTeamId: teamA, Subject: "başka", ProviderMessageId: provider), _token);
        viaTeam.ErrorCode.Should().Be(SaErrors.ProviderMessageIdUnavailable);
        viaTeam.Value.Should().BeNull();
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.CommunicationAccounts WHERE AccountId = @accA", new { accA })).Should().Be(0);
        Ok(await fx.Service.AccountAsync(coordA.Principal, fx.Context, accA, _token)).Communications.Should().BeEmpty();

        // The same message sent to both organizations is linked by someone who covers the stored mail; each side lists only its own account.
        SynUser both = await fx.UserAsync(_all);
        await fx.GrantAsync(both, ScopeKind.Organization, orgA);
        await fx.GrantAsync(both, ScopeKind.Organization, orgB);
        CommunicationSaveResult shared = Ok(await fx.Service.CreateCommunicationAsync(both.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", [accA], _on, ProviderMessageId: provider), _token));
        shared.ExistingMessage.Should().BeTrue();
        shared.Communication.Id.Should().Be(recorded.Communication.Id);
        CommunicationView seenByA = Ok(await fx.Service.AccountAsync(coordA.Principal, fx.Context, accA, _token)).Communications.Should().ContainSingle().Subject;
        seenByA.Accounts.Select(a => a.Id).Should().Equal(accA);
        Ok(await fx.Service.AccountAsync(coordB.Principal, fx.Context, accB, _token)).Communications.Single().Accounts.Select(a => a.Id).Should().Equal(accB);
        Ok(await fx.Service.AccountAsync(both.Principal, fx.Context, accA, _token)).Communications.Single().Accounts.Select(a => a.Id).Should().BeEquivalentTo([accA, accB]);
    }

    [ServiceAccountSqlFact]
    public async Task StoredMail_EventPath_RespectsScope()
    {
        ServiceAccountSqlFixture fx = new();
        (_, SynUser coordA, Guid accA) = await OrganizationWithAccountAsync(fx, "A");
        (_, SynUser coordB, Guid accB) = await OrganizationWithAccountAsync(fx, "B");
        string provider = "<syn-event-" + fx.Suffix + "@example.invalid>";
        Ok(await fx.Service.CreateCommunicationAsync(coordB.Principal, fx.Context,
            new CreateCommunicationRequest("Incoming", "Reply", [accB], _on, Subject: "SYN konu", ProviderMessageId: provider), _token));

        AccountDetail account = Ok(await fx.Service.AccountAsync(coordA.Principal, fx.Context, accA, _token));
        EventDraft draft = new("OwnerReplied", _on, "DateOnly", "Mail", account.Summary.Version,
            Communication: new EventCommunication(Summary: "SYN özet", ProviderMessageId: provider));
        SaResult<EventPreview> preview = await fx.Service.PreviewEventAsync(coordA.Principal, fx.Context, accA, draft, _token);
        preview.ErrorCode.Should().Be(SaErrors.ProviderMessageIdUnavailable);
        preview.Field.Should().Be("communication.providerMessageIdUnavailable");

        // The service refuses at planning; a commit that gets past it (the mail stored after the preview) must still be refused by the
        // repository's own visibility check inside the write transaction. The write below carries a version the account really has.
        AccountEventWrite write = new(accA, AccountEventKind.OwnerReplied, _on, AccountEventChannel.Mail, Guid.NewGuid(), "syn-draft-" + fx.Suffix,
            account.Summary.Version, null, null,
            new EventCommunicationWrite(CommunicationDirection.Incoming, CommunicationKind.Reply,
                new CreateCommunicationRequest("Incoming", "Reply", [accA], _on, Summary: "SYN özet", ProviderMessageId: provider)),
            null, [], false, null, null, false);
        AccountEventOutcome outcome = await fx.Repository.CommitAccountEventAsync(write, new SaActor(coordA.User.Id, fx.Context.CorrelationId), _token);
        outcome.Status.Should().Be(AccountEventStatus.CommunicationRefused);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.CommunicationAccounts WHERE AccountId = @accA", new { accA })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountEvents WHERE AccountId = @accA", new { accA })).Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task ImportBatches_AreReachableOnlyThroughTheirOwnScope()
    {
        ServiceAccountSqlFixture fx = new();
        string nameA = "SYN IMP A " + fx.Suffix, nameB = "SYN IMP B " + fx.Suffix;
        Guid orgA = await fx.OrganizationAsync(nameA), orgB = await fx.OrganizationAsync(nameB);
        SynUser importerA = await fx.UserAsync(_importer), importerB = await fx.UserAsync(_importer);
        await fx.GrantAsync(importerA, ScopeKind.Organization, orgA);
        await fx.GrantAsync(importerB, ScopeKind.Organization, orgB);

        ImportBatchView batch = Ok(await fx.Service.StageImportAsync(importerB.Principal, fx.Context,
            new StageImportRequest(ServiceAccountImportProfiles.CoordinationList, new DateOnly(2026, 9, 14), "Sentetik beyan: kaynak rapor tarihi test verisidir"),
            "liste.xlsx", "application/octet-stream", List(nameB, $"SYN{fx.Suffix}_IMPB"), _token));

        (await fx.Service.ImportAsync(importerA.Principal, fx.Context, batch.Id, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.ImportRowsAsync(importerA.Principal, fx.Context, batch.Id, null, null, false, 1, 50, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        Ok(await fx.Service.ImportsAsync(importerA.Principal, fx.Context, _token)).Should().NotContain(i => i.Id == batch.Id);
        (await fx.Service.DecideImportAsync(importerA.Principal, fx.Context, batch.Id, new ImportDecisionsRequest(batch.DecisionVersion, []), _token))
            .ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.RefreshImportAsync(importerA.Principal, fx.Context, batch.Id, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.CommitImportAsync(importerA.Principal, fx.Context, batch.Id, new ImportCommitRequest(batch.PreviewVersion, batch.DecisionVersion),
            "k-" + Guid.NewGuid().ToString("N"), _token)).ErrorCode.Should().Be(SaErrors.NotFound);

        Ok(await fx.Service.ImportAsync(importerB.Principal, fx.Context, batch.Id, _token)).Id.Should().Be(batch.Id);
        Ok(await fx.Service.ImportRowsAsync(importerB.Principal, fx.Context, batch.Id, null, null, false, 1, 50, _token)).Total.Should().BeGreaterThan(0);
        Ok(await fx.Service.ImportsAsync(importerB.Principal, fx.Context, _token)).Should().Contain(i => i.Id == batch.Id);

        // A coordinator who covers the whole batch boundary reads it; one covering only part of it does not.
        SynUser wide = await fx.UserAsync(_importer);
        await fx.GrantAsync(wide, ScopeKind.All);
        Ok(await fx.Service.ImportAsync(wide.Principal, fx.Context, batch.Id, _token)).Id.Should().Be(batch.Id);
        SynUser both = await fx.UserAsync(_importer);
        await fx.GrantAsync(both, ScopeKind.Organization, orgA);
        await fx.GrantAsync(both, ScopeKind.Organization, orgB);
        Ok(await fx.Service.ImportRowsAsync(both.Principal, fx.Context, batch.Id, null, null, false, 1, 50, _token)).Total.Should().BeGreaterThan(0);
    }

    [ServiceAccountSqlFact]
    public async Task ImportBatches_ReplayAndListAreFilteredByScope()
    {
        ServiceAccountSqlFixture fx = new();
        string nameA = "SYN IMP A " + fx.Suffix, nameB = "SYN IMP B " + fx.Suffix;
        Guid orgA = await fx.OrganizationAsync(nameA), orgB = await fx.OrganizationAsync(nameB);
        SynUser importerA = await fx.UserAsync(_importer), importerB = await fx.UserAsync(_importer);
        await fx.GrantAsync(importerA, ScopeKind.Organization, orgA);
        await fx.GrantAsync(importerB, ScopeKind.Organization, orgB);
        StageImportRequest request = new(ServiceAccountImportProfiles.CoordinationList, new DateOnly(2026, 9, 14), "Sentetik beyan: kaynak rapor tarihi test verisidir");
        byte[] file = List(nameB, $"SYN{fx.Suffix}_REPLAY");

        ImportBatchView committed = Ok(await fx.Service.StageImportAsync(importerB.Principal, fx.Context, request, "liste.xlsx", "application/octet-stream", file, _token));
        Ok(await fx.Service.CommitImportAsync(importerB.Principal, fx.Context, committed.Id, new ImportCommitRequest(committed.PreviewVersion, committed.DecisionVersion),
            "k-" + Guid.NewGuid().ToString("N"), _token));

        // The same file and parameters from another scope is not answered with the committed batch of the first scope.
        ImportBatchView other = Ok(await fx.Service.StageImportAsync(importerA.Principal, fx.Context, request, "baska.xlsx", "application/octet-stream", file, _token));
        other.Id.Should().NotBe(committed.Id);
        other.Replay.Should().BeFalse();
        other.FileName.Should().Be("baska.xlsx");

        // The same scope still gets its replay.
        Ok(await fx.Service.StageImportAsync(importerB.Principal, fx.Context, request, "liste.xlsx", "application/octet-stream", file, _token))
            .Id.Should().Be(committed.Id);

        // The list is filtered before the limit: a newer batch of another scope cannot push this scope's batch out of a one-row page.
        ImportBatchView newer = Ok(await fx.Service.StageImportAsync(importerA.Principal, fx.Context, request with { SourceReportDate = new DateOnly(2026, 9, 15) },
            "yeni.xlsx", "application/octet-stream", List(nameA, $"SYN{fx.Suffix}_NEWER"), _token));
        IReadOnlyList<ImportBatchRecord> page = await fx.Repository.ListBatchesAsync(1, false, [orgB], importerB.User.Id, _token);
        page.Should().ContainSingle().Which.Id.Should().Be(committed.Id);
        page.Should().NotContain(b => b.Id == newer.Id);
    }

    [ServiceAccountSqlFact]
    public async Task Mutation_UsesTheGrantsThatAreCurrentInsideTheTransaction()
    {
        ServiceAccountSqlFixture fx = new();
        (_, SynUser worker, Guid account) = await OrganizationWithAccountAsync(fx, "A");
        SaActor actor = new(worker.User.Id, fx.Context.CorrelationId);
        CreateFindingRequest finding = new(account, "Success", "NoMatch");

        Ok(await fx.Repository.CreateFindingAsync(finding, actor, _token));
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Findings WHERE AccountId = @account", new { account })).Should().Be(1);

        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View);
        await using (Microsoft.Data.SqlClient.SqlConnection connection = fx.Connection())
        {
            await connection.ExecuteAsync("""
                UPDATE svcacct.ScopeGrants SET RevokedAt = SYSUTCDATETIME(), RevokedBy = @by, RevokeReason = N'Synthetic revocation'
                WHERE UserId = @UserId AND RevokedAt IS NULL;
                """, new { by = admin.User.Id, UserId = worker.User.Id });
        }

        (await fx.Repository.CreateFindingAsync(finding, actor, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Findings WHERE AccountId = @account", new { account })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @account AND Action = 'FindingRecorded'", new { account })).Should().Be(1);
    }

    [ServiceAccountSqlFact]
    public async Task RequestClose_ReevaluatesSupportingActionsInsideTheTransaction()
    {
        ServiceAccountSqlFixture fx = new();
        (_, SynUser coordinator, Guid accountId) = await OrganizationWithAccountAsync(fx, "A");
        AccountDetail account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, accountId, new CreateWorkRequest("Deletion"), _token));
        Guid request = account.Requests.Single().Id;
        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, accountId,
            new ReportActionRequest("Deletion", "Performed", "Closure", request, ActualOn: new(2026, 9, 12), EvidenceNote: "silme bildirimi"), _token));
        ActionView action = account.Actions.Single();
        account = Ok(await fx.Service.UpdateActionAsync(coordinator.Principal, fx.Context, action.Id,
            new UpdateActionRequest(action.Version, AddReferences: [new SaExternalRef("OR", "OR-SCOPE-" + fx.Suffix)]), _token));
        action = account.Actions.Single();
        account = Ok(await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, action.Id,
            new VerifyActionRequest(action.Version, new(2026, 9, 13), VerificationNote: "kontrol edildi"), _token));
        action = account.Actions.Single();
        string version = account.Requests.Single().Version;

        // The facts a caller validated earlier are no longer true: the supporting action was voided after that read.
        Ok(await fx.Service.VoidActionAsync(coordinator.Principal, fx.Context, action.Id, new VoidActionRequest(action.Version, "Sentetik geçersiz kılma"), _token));
        SaResult<Guid> closed = await fx.Repository.CloseRequestAsync(accountId, request, version, ServiceAccountCloseOutcome.Completed, null,
            new SaActor(coordinator.User.Id, fx.Context.CorrelationId), _token,
            (status, type, actions, owned) => ServiceAccountRules.ValidateClose(status, type, ServiceAccountCloseOutcome.Completed, null, actions, owned));
        closed.ErrorCode.Should().Be(SaErrors.Invalid);
        closed.Field.Should().NotBeNullOrEmpty("the close rule names what is missing");
        Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, accountId, _token)).Requests.Single().Status.Should().Be("Open");
    }

    private static async Task<(Guid Org, SynUser Coordinator, Guid Account)> OrganizationWithAccountAsync(ServiceAccountSqlFixture fx, string tag)
    {
        Guid org = await fx.OrganizationAsync($"SYN SCOPE {tag} {fx.Suffix}");
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_{tag}", null, org, "Sentetik test hesabı"), _token));
        return (org, coordinator, account.Summary.Id);
    }

    private static byte[] List(string organization, string name) =>
        SyntheticWorkbook.Create([("Sheet2", [
            (1, new SynCell?[] { "Kullanıcı Adı", "Son Parola Değişiklik Zamanı", "AD veya LDAP Son Oturum Açma Zamanı", "AD Son Oturum Açma Zamanı", "Organizasyon", "Grup Direktorlugu", "Yorum" }),
            (2, new SynCell?[] { name, 45000, 45000, 45000, organization, "SYN GRUP", null })])]);

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
