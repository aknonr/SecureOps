using System.Text;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

public sealed class ServiceAccountImportSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _importer = [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Import];

    [ServiceAccountSqlFact]
    public async Task LegacyPackageThenWorkbook_OverlapCreatesNoDuplicates_AndReplaysReturnTheStoredResult()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser coordinator = await fx.UserAsync([.. _importer, ServiceAccountCapabilities.Assign]);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        SyntheticLegacy legacy = new(fx.Suffix, orgName);

        ImportBatchView package = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.LegacyPackage, "paket.json", legacy.Package, new DateOnly(2026, 9, 14));
        package.Status.Should().Be("Previewed");
        package.Summary!.ByEntity[StagedKinds.Account].Should().Be(5);
        package.Summary.NotSeen.Should().Be(1, "the fifth account is not in the coordination list: an observation, never a closure");
        package.Summary.DecisionsRequired.Should().Be(1, "a doubled-letter spelling variant is a candidate, never merged automatically");
        package.Summary.CohortFlagged.Should().Be(1, "the flagged source row and the handover row of one account are one cohort member");
        ImportRowView variant = (await Rows(fx, coordinator, package.Id, decisionsOnly: true)).Items.Single();
        variant.Candidates.Should().BeEmpty("the variant only exists in the same batch, so it is created separately after an explicit decision");

        package = Ok(await fx.Service.DecideImportAsync(coordinator.Principal, fx.Context, package.Id,
            new ImportDecisionsRequest(package.DecisionVersion, [new ImportRowDecision(variant.RowKey, ImportDecisions.Create)]), _token));
        ImportBatchView committed = Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, package.Id,
            new ImportCommitRequest(package.PreviewVersion, package.DecisionVersion), "commit-" + fx.Suffix, _token));
        ImportResultView result = committed.Result!;
        result.AccountsCreated.Should().Be(5);
        result.RequestsCreated.Should().Be(3);
        result.ActionsCreated.Should().Be(2);
        result.CommunicationsCreated.Should().Be(3);
        result.CommunicationLinksAdded.Should().Be(4, "one team mail links three accounts and counts once");
        result.HandoversCreated.Should().Be(1);
        result.TransitionsCreated.Should().Be(1);
        result.OwnershipConfirmed.Should().Be(0, "source ownership stays a proposal unless an authorized decision confirms it");
        result.OwnershipProposed.Should().Be(3);
        result.ObservationsRecorded.Should().Be(6);
        result.NotSeenObservations.Should().Be(1);

        ImportBatchView again = Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, package.Id,
            new ImportCommitRequest(package.PreviewVersion, package.DecisionVersion), "another-key-" + fx.Suffix, _token));
        again.Result.Should().BeEquivalentTo(result);
        ImportBatchView restaged = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.LegacyPackage, "paket.json", legacy.Package, new DateOnly(2026, 9, 14));
        restaged.Replay.Should().BeTrue();
        restaged.Id.Should().Be(package.Id);

        ImportBatchView workbook = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.LegacyWorkbook, "takip.xlsx", legacy.Workbook, new DateOnly(2026, 9, 19));
        workbook.Summary!.New.Should().Be(0, "the workbook rows share the package's stable legacy references");
        workbook.Warnings.Should().Contain("ArchiveSheetNotImported:Kaynak_Orijinal");
        ImportBatchView second = Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, workbook.Id,
            new ImportCommitRequest(workbook.PreviewVersion, workbook.DecisionVersion), "wb-" + fx.Suffix, _token));
        second.Result!.RequestsCreated.Should().Be(0);
        second.Result.ActionsCreated.Should().Be(0);
        second.Result.CommunicationsCreated.Should().Be(0);
        second.Result.HandoversCreated.Should().Be(0);
        second.Result.AccountsCreated.Should().Be(0);

        const string scoped = "SELECT COUNT(*) FROM svcacct.{0} x JOIN svcacct.Accounts a ON a.Id = x.AccountId WHERE a.ReportOrganizationId = @org";
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE ReportOrganizationId = @org", new { org })).Should().Be(5);
        (await fx.CountAsync(string.Format(null, scoped, "WorkRequests"), new { org })).Should().Be(3);
        (await fx.CountAsync(string.Format(null, scoped, "ActionEvents"), new { org })).Should().Be(2);
        (await fx.CountAsync(string.Format(null, scoped, "Handovers"), new { org })).Should().Be(1);
        (await fx.CountAsync("""
            SELECT COUNT(DISTINCT c.Id) FROM svcacct.Communications c WHERE c.LegacyReference LIKE @prefix
            """, new { prefix = "legacy:" + legacy.WorkbookSha + ":%" })).Should().Be(3);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE ReportOrganizationId = @org AND CurrentOwnerPersonId IS NOT NULL", new { org }))
            .Should().Be(0, "a named source person is not a directory-verified confirmed owner");
        (await fx.CountAsync("""
            SELECT COUNT(*) FROM svcacct.ActionEvents e JOIN svcacct.Accounts a ON a.Id = e.AccountId
            WHERE a.ReportOrganizationId = @org AND e.VerifiedOn IS NULL AND e.SourceNote LIKE N'%doğrulama tarihi işlemden önce%'
            """, new { org })).Should().Be(1, "a legacy verification dated before the action is kept as a note, not as verification");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ExternalRecords r JOIN svcacct.ExternalRecordLinks l ON l.ExternalRecordId = r.Id JOIN svcacct.Accounts a ON a.Id = l.AccountId WHERE a.ReportOrganizationId = @org AND r.RecordType = 'JIRA'", new { org }))
            .Should().Be(1, "a Jira key is stored as Jira, never as OR/OCO");
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @actor AND Action = 'ServiceAccount.ImportCommitted'", new { actor = coordinator.User.Id.ToString("D") }))
            .Should().Be(2);
    }

    [ServiceAccountSqlFact]
    public async Task CoordinationList_NewPeriodObservations_AbsenceIsNotClosure_AndOlderPeriodNeverMovesLatestBack()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN LIST ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser coordinator = await fx.UserAsync(_importer);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        string a = $"SYN{fx.Suffix}_A", b = $"SYN{fx.Suffix}_B", c = $"SYN{fx.Suffix}_C";
        await CommitAsync(fx, coordinator, List(orgName, (a, 45000), (b, 45001), (c, 45002)), new DateOnly(2026, 9, 7));
        ImportBatchView second = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "liste.csv",
            Csv(["Yorum", "Organizasyon", "Kullanıcı Adı", "Son Parola Değişiklik Zamanı"], [["yeni dönem", orgName, a, "46000"], ["", orgName, b, "46001"]]),
            new DateOnly(2026, 9, 14));
        second.Summary!.ObservationUpdates.Should().Be(2);
        second.Summary.NotSeen.Should().Be(1);
        second.Summary.New.Should().Be(0);
        await CommitStagedAsync(fx, coordinator, second);
        await CommitAsync(fx, coordinator, List(orgName, (a, 44000)), new DateOnly(2026, 8, 1));

        await using SqlConnection connection = fx.Connection();
        var accounts = (await connection.QueryAsync<(string AccountName, DateTime LastObservedOn, string LastObservationPresence, string Lifecycle)>(
            "SELECT AccountName, LastObservedOn, LastObservationPresence, LifecycleState FROM svcacct.Accounts WHERE ReportOrganizationId = @org ORDER BY AccountName", new { org })).ToList();
        accounts.Should().HaveCount(3);
        accounts.Should().OnlyContain(x => x.LastObservedOn == new DateTime(2026, 9, 14) && x.Lifecycle == "Active");
        accounts.Single(x => x.AccountName == c).LastObservationPresence.Should().Be("NotPresent");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountObservations o JOIN svcacct.Accounts x ON x.Id = o.AccountId WHERE x.ReportOrganizationId = @org", new { org }))
            .Should().Be(3 + 3 + 1, "every period is kept as an append-only observation");
    }

    [ServiceAccountSqlFact]
    public async Task StalePreviewAndDecisionConflicts_Return409_AndRefreshRecovers()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN STALE ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser coordinator = await fx.UserAsync(_importer);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        string a = $"SYN{fx.Suffix}_S";
        await CommitAsync(fx, coordinator, List(orgName, (a, 45000)), new DateOnly(2026, 9, 7));
        ImportBatchView staged = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "liste2.xlsx", List(orgName, (a, 45500)), new DateOnly(2026, 9, 14));

        SaResult<ImportBatchView> first = await fx.Service.DecideImportAsync(coordinator.Principal, fx.Context, staged.Id, new ImportDecisionsRequest(staged.DecisionVersion, []), _token);
        SaResult<ImportBatchView> stale = await fx.Service.DecideImportAsync(coordinator.Principal, fx.Context, staged.Id, new ImportDecisionsRequest(staged.DecisionVersion, []), _token);
        first.IsSuccess.Should().BeTrue();
        stale.ErrorCode.Should().Be(SaErrors.Conflict);

        await using (SqlConnection connection = fx.Connection())
        {
            await connection.ExecuteAsync("UPDATE svcacct.Accounts SET Notes = N'eşzamanlı değişiklik' WHERE AccountName = @a", new { a });
        }

        SaResult<ImportBatchView> refused = await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, staged.Id,
            new ImportCommitRequest(first.Value!.PreviewVersion, first.Value.DecisionVersion), "stale-" + fx.Suffix, _token);
        refused.ErrorCode.Should().Be(SaErrors.PreviewStale);
        ImportBatchView refreshed = Ok(await fx.Service.RefreshImportAsync(coordinator.Principal, fx.Context, staged.Id, _token));
        Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, staged.Id,
            new ImportCommitRequest(refreshed.PreviewVersion, refreshed.DecisionVersion), "fresh-" + fx.Suffix, _token)).Status.Should().Be("Committed");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE AccountName = @a AND Notes = N'eşzamanlı değişiklik'", new { a })).Should().Be(1);
    }

    [ServiceAccountSqlFact]
    public async Task AuditFailure_RollsBackTheWholeCommit()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN ROLLBACK ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser coordinator = await fx.UserAsync(_importer);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        ImportBatchView staged = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "rb.xlsx",
            List(orgName, ($"SYN{fx.Suffix}_R1", 45000), ($"SYN{fx.Suffix}_R2", 45000)), new DateOnly(2026, 9, 14));
        string trigger = "TR_SaTest_" + Guid.NewGuid().ToString("N");
        await using SqlConnection connection = fx.Connection();
        // Only a new test-owned trigger in the disposable database is created and dropped.
        await connection.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Actor = '{coordinator.User.Id:D}' AND Action = 'ServiceAccount.ImportCommitted') THROW 51091, 'Synthetic audit failure.', 1; END;");
        try
        {
            SaResult<ImportBatchView> failed = await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, staged.Id,
                new ImportCommitRequest(staged.PreviewVersion, staged.DecisionVersion), "rb-" + fx.Suffix, _token);
            failed.ErrorCode.Should().Be(SaErrors.Unavailable);
            (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE ReportOrganizationId = @org", new { org })).Should().Be(0);
            (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ImportBatches WHERE Id = @Id AND Status = 'Previewed' AND ResultJson IS NULL", new { staged.Id })).Should().Be(1);
        }
        finally
        {
            await connection.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];");
        }
    }

    [ServiceAccountSqlFact]
    public async Task ScopeIsEnforced_TeamScopeCannotImport_AndOtherOrganizationAccountsAreOutOfScope()
    {
        ServiceAccountSqlFixture fx = new();
        string mine = "SYN MINE " + fx.Suffix, other = "SYN OTHER " + fx.Suffix;
        Guid myOrg = await fx.OrganizationAsync(mine);
        Guid otherOrg = await fx.OrganizationAsync(other);
        SynUser owner = await fx.UserAsync(_importer);
        await fx.GrantAsync(owner, ScopeKind.Organization, otherOrg);
        await CommitAsync(fx, owner, List(other, ($"SYN{fx.Suffix}_THEIRS", 45000)), new DateOnly(2026, 9, 7));

        SynUser teamMember = await fx.UserAsync(_importer);
        await fx.GrantAsync(teamMember, ScopeKind.Team, team: await fx.TeamAsync("SYN TEAM " + fx.Suffix, myOrg));
        SaResult<ImportBatchView> denied = await fx.Service.StageImportAsync(teamMember.Principal, fx.Context,
            new StageImportRequest(ServiceAccountImportProfiles.CoordinationList, new DateOnly(2026, 9, 14), "test"), "x.xlsx", "application/octet-stream",
            List(mine, ($"SYN{fx.Suffix}_X", 45000)), _token);
        denied.ErrorCode.Should().Be(SaErrors.Forbidden);

        SynUser coordinator = await fx.UserAsync(_importer);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, myOrg);
        ImportBatchView staged = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "scope.xlsx",
            List(mine, ($"SYN{fx.Suffix}_THEIRS", 45500), ($"SYN{fx.Suffix}_NEWONE", 45500)), new DateOnly(2026, 9, 14));
        staged.Summary!.OutOfScope.Should().Be(1, "knowing another team's account name does not reveal or update it");
        staged.Summary.New.Should().Be(1);
        ImportBatchView committed = await CommitStagedAsync(fx, coordinator, staged);
        committed.Result!.ObservationsRecorded.Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountObservations o JOIN svcacct.Accounts a ON a.Id = o.AccountId WHERE a.AccountName = @n",
            new { n = $"SYN{fx.Suffix}_THEIRS" })).Should().Be(1, "the out-of-scope row was not applied");

        SynUser noCapability = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(noCapability, ScopeKind.All);
        (await fx.Service.ImportAsync(noCapability.Principal, fx.Context, staged.Id, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    [ServiceAccountSqlFact]
    public async Task ConfirmingOwnershipInImport_RequiresAssignCapability()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN OWN ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser importer = await fx.UserAsync(_importer);
        await fx.GrantAsync(importer, ScopeKind.Organization, org);
        SyntheticLegacy legacy = new(fx.Suffix, orgName);
        ImportBatchView staged = await StageAsync(fx, importer, ServiceAccountImportProfiles.LegacyWorkbook, "takip.xlsx", legacy.Workbook, null);
        ImportRowView ownership = (await Rows(fx, importer, staged.Id, kind: StagedKinds.Ownership)).Items.First();
        ownership.AllowedDecisions.Should().Contain(ImportDecisions.Confirm);
        (await fx.Service.DecideImportAsync(importer.Principal, fx.Context, staged.Id,
            new ImportDecisionsRequest(staged.DecisionVersion, [new ImportRowDecision(ownership.RowKey, ImportDecisions.Confirm)]), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    private static async Task<ImportBatchView> StageAsync(ServiceAccountSqlFixture fx, SynUser user, string profile, string file, byte[] bytes, DateOnly? date) =>
        Ok(await fx.Service.StageImportAsync(user.Principal, fx.Context, new StageImportRequest(profile, date, "Sentetik beyan: kaynak rapor tarihi test verisidir"),
            file, "application/octet-stream", bytes, _token));

    private static async Task<ImportRowPage> Rows(ServiceAccountSqlFixture fx, SynUser user, Guid id, bool decisionsOnly = false, string? kind = null) =>
        Ok(await fx.Service.ImportRowsAsync(user.Principal, fx.Context, id, null, kind, decisionsOnly, 1, 200, _token));

    private static async Task<ImportBatchView> CommitAsync(ServiceAccountSqlFixture fx, SynUser user, byte[] file, DateOnly date) =>
        await CommitStagedAsync(fx, user, await StageAsync(fx, user, ServiceAccountImportProfiles.CoordinationList, "liste.xlsx", file, date));

    private static async Task<ImportBatchView> CommitStagedAsync(ServiceAccountSqlFixture fx, SynUser user, ImportBatchView staged) =>
        Ok(await fx.Service.CommitImportAsync(user.Principal, fx.Context, staged.Id, new ImportCommitRequest(staged.PreviewVersion, staged.DecisionVersion),
            "k-" + Guid.NewGuid().ToString("N"), _token));

    private static byte[] List(string organization, params (string Name, double Serial)[] rows) =>
        SyntheticWorkbook.Create([("Sheet2", [
            (1, new SynCell?[] { "Kullanıcı Adı", "Son Parola Değişiklik Zamanı", "AD veya LDAP Son Oturum Açma Zamanı", "AD Son Oturum Açma Zamanı", "Organizasyon", "Grup Direktorlugu", "Yorum" }),
            .. rows.Select((r, i) => (i + 2, new SynCell?[] { r.Name, r.Serial, r.Serial, r.Serial, organization, "SYN GRUP", null }))])]);

    private static byte[] Csv(string[] headers, string[][] rows) =>
        Encoding.UTF8.GetBytes(string.Join("\r\n", new[] { string.Join(';', headers) }.Concat(rows.Select(r => string.Join(';', r)))) + "\r\n");

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
