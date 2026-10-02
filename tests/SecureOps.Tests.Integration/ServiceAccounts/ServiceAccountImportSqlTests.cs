using System.Text;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;
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

        ImportBatchView package = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.LegacyPackage, "paket.json", legacy.Package, new DateOnly(2026, 9, 14),
            Complete(org));
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
        ImportBatchView restaged = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.LegacyPackage, "paket.json", legacy.Package, new DateOnly(2026, 9, 14),
            Complete(org));
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
            new DateOnly(2026, 9, 14), Complete(org));
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

    /// <summary>
    /// Reverse direction of the write gate: while a module write holds the shared gate, an import commit waits on the
    /// application lock before taking any row lock, and completes once the write finishes.
    /// </summary>
    [ServiceAccountSqlFact]
    public async Task ImportCommit_WaitsForAnInFlightModuleWrite_OnTheGateNotOnRows()
    {
        ServiceAccountSqlFixture fx = new();
        string orgName = "SYN GATE ORG " + fx.Suffix;
        Guid org = await fx.OrganizationAsync(orgName);
        SynUser coordinator = await fx.UserAsync(_importer);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        ImportBatchView staged = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "gate.xlsx",
            List(orgName, ($"SYN{fx.Suffix}_G1", 45000)), new DateOnly(2026, 9, 14));

        await using SqlConnection writer = fx.Connection();
        await writer.OpenAsync();
        await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await writer.ExecuteAsync("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = 'svcacct:import-commit', @LockMode = 'Shared', @LockOwner = 'Transaction', @LockTimeout = 5000;
            IF @result < 0 THROW 51399, 'Synthetic writer gate unavailable.', 1;
            """, transaction: transaction);
        short writerSession = await writer.ExecuteScalarAsync<short>("SELECT CONVERT(smallint, @@SPID);", transaction: transaction);

        Task<SaResult<ImportBatchView>> commit = fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, staged.Id,
            new ImportCommitRequest(staged.PreviewVersion, staged.DecisionVersion), "gate-" + fx.Suffix, _token);
        int waitingOnGate = 0;
        for (int attempt = 0; attempt < 150 && waitingOnGate == 0; attempt++)
        {
            waitingOnGate = await fx.CountAsync("""
                SELECT COUNT(*) FROM sys.dm_os_waiting_tasks w JOIN sys.dm_tran_locks l ON l.lock_owner_address = w.resource_address
                WHERE w.blocking_session_id = @writerSession AND l.resource_type = 'APPLICATION' AND l.request_status = 'WAIT';
                """, new { writerSession });
            if (waitingOnGate == 0)
            { await Task.Delay(TimeSpan.FromMilliseconds(100)); }
        }

        waitingOnGate.Should().Be(1, "the commit must queue on the application lock while a module write is in flight");
        commit.IsCompleted.Should().BeFalse();
        await transaction.CommitAsync();
        ImportBatchView committed = Ok(await commit);
        committed.Result!.AccountsCreated.Should().Be(1);
    }

    /// <summary>
    /// The reporting chain end to end: two weekly imports (one repeated), the live weekly report, an immutable sent
    /// snapshot with a late entry, month and date-range reports and filtered XLSX export — with scope, duplicate-import,
    /// row-limit and actor/audit checks.
    /// </summary>
    [ServiceAccountSqlFact]
    public async Task WeeklyImports_Reports_SentSnapshot_Periods_AndExport_WorkTogetherWithinScope()
    {
        ServiceAccountSqlFixture fx = new();
        string mineName = "SYN CHAIN A " + fx.Suffix, theirName = "SYN CHAIN B " + fx.Suffix;
        Guid mine = await fx.OrganizationAsync(mineName), theirs = await fx.OrganizationAsync(theirName);
        string[] all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];
        SynUser coordinator = await fx.UserAsync(all), other = await fx.UserAsync(all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, mine);
        await fx.GrantAsync(other, ScopeKind.Organization, theirs);
        string a1 = $"SYN{fx.Suffix}_CH1", a2 = $"SYN{fx.Suffix}_CH2", a3 = $"SYN{fx.Suffix}_CH3";

        // Week 1 and its duplicate: the same file, period and declaration is a replay, not a second import.
        byte[] week1 = List(mineName, (a1, 46000), (a2, 46000), (a3, 46000));
        ImportBatchView first = await CommitStagedAsync(fx, coordinator,
            await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "hafta1.xlsx", week1, new DateOnly(2026, 9, 14), Complete(mine)));
        ImportBatchView repeated = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "hafta1-kopya.xlsx", week1,
            new DateOnly(2026, 9, 14), Complete(mine));
        repeated.Replay.Should().BeTrue();
        repeated.Id.Should().Be(first.Id);
        ImportBatchView recommitted = Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, first.Id,
            new ImportCommitRequest(first.PreviewVersion, first.DecisionVersion), "baska-anahtar-" + fx.Suffix, _token));
        recommitted.Result.Should().BeEquivalentTo(first.Result);
        ImportBatchView second = await CommitStagedAsync(fx, coordinator,
            await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "hafta2.xlsx", List(mineName, (a1, 46007), (a2, 46007)),
                new DateOnly(2026, 9, 21), Complete(mine)));
        second.Result!.NotSeenObservations.Should().Be(1);
        second.Result.AccountsCreated.Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE ReportOrganizationId = @mine", new { mine })).Should().Be(3);
        AccountDetail theirAccount = Ok(await fx.Service.CreateAccountAsync(other.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_CHX", null, theirs, "s"), _token));
        Ok(await fx.Service.ReportActionAsync(other.Principal, fx.Context, theirAccount.Summary.Id,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", ActualOn: new(2026, 9, 16), EvidenceNote: "başka kapsam"), _token));

        Guid id1 = await AccountIdAsync(fx, a1), id2 = await AccountIdAsync(fx, a2);
        Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, id1,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", ActualOn: new(2026, 9, 16), EvidenceNote: "hafta 1"), _token));
        Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, id2,
            new ReportActionRequest("Review", "Performed", "Intermediate", ActualOn: new(2026, 9, 23), EvidenceNote: "hafta 2"), _token));

        DateTimeOffset cutoff = new(2026, 9, 20, 23, 0, 0, TimeSpan.FromHours(3));
        ServiceAccountReport live = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 14), cutoff), _token));
        live.Summary.UniqueAccounts.Should().Be(3, "another organization's account is not in this scope");
        live.Weekly.Actions.InPeriod.Should().Be(1);
        SnapshotItem sent = Ok(await fx.Service.CreateSnapshotAsync(coordinator.Principal, fx.Context,
            new CreateSnapshotRequest(live.WeekStart, live.AsOf, null, null, "Manager", "Direktöre gönderilen"), _token));

        // A late entry for the sent week changes the live report only.
        Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, id1,
            new ReportActionRequest("Review", "Performed", "Intermediate", ActualOn: new(2026, 9, 17), EvidenceNote: "geç girildi"), _token));
        Ok(await fx.Service.SnapshotAsync(coordinator.Principal, fx.Context, sent.Id, _token)).Should().BeEquivalentTo(live, "a sent snapshot never changes");
        Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 14), cutoff), _token))
            .Weekly.Actions.InPeriod.Should().Be(2);
        (await fx.Service.SnapshotAsync(other.Principal, fx.Context, sent.Id, _token)).ErrorCode.Should().Be(SaErrors.NotFound);

        ServiceAccountReport month = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context,
            new WeeklyReportQuery(new(2026, 9, 5), Period: "Month"), _token));
        month.Weekly.Actions.InPeriod.Should().Be(3, "September holds the three in-scope actions, not the other organization's");
        ServiceAccountReport range = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context,
            new WeeklyReportQuery(new(2026, 9, 21), Period: "Custom", PeriodEnd: new(2026, 9, 27)), _token));
        range.Weekly.Actions.InPeriod.Should().Be(1);
        range.Weekly.Actions.Earlier.Should().Be(2);

        string[] exported = ExportedNames(Ok(await fx.Service.ExportAccountsAsync(coordinator.Principal, fx.Context, new AccountListQuery(), _token)));
        exported.Should().BeEquivalentTo([a1, a2, a3]);
        ExportedNames(Ok(await fx.Service.ExportAccountsAsync(coordinator.Principal, fx.Context, new AccountListQuery(Status: "notseen"), _token)))
            .Should().Equal(a3);
        ReportExport pdf = Ok(await fx.Service.ExportSnapshotAsync(coordinator.Principal, fx.Context, sent.Id, "pdf", _token));
        string creator;
        await using (SqlConnection connection = fx.Connection())
        {
            creator = await connection.ExecuteScalarAsync<string>("SELECT DisplayName FROM security.Users WHERE UserId = @id", new { id = coordinator.User.Id }) ?? "?";
        }

        ReportPdfWriter.ExtractLines(pdf.Content).Should().Contain(l => l.Contains("Oluşturan", StringComparison.Ordinal)
            && l.Contains(creator, StringComparison.Ordinal), "the sent copy names who created it");

        string actor = coordinator.User.Id.ToString("D");
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @actor AND Action = 'ServiceAccount.ImportCommitted'", new { actor }))
            .Should().Be(2, "the replayed commit is not audited as a second import");
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @actor AND Action = 'ServiceAccount.ReportSnapshotCreated'", new { actor })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @actor AND Action = 'ServiceAccount.AccountsExported'", new { actor })).Should().Be(2);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @actor AND Action = 'ServiceAccount.AccountsExported' AND JSON_VALUE(DetailsJson, '$.Rows') = '3'",
            new { actor })).Should().Be(1, "the export audit records the row count");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ReportSnapshots WHERE Id = @Id AND CreatedBy = @user", new { sent.Id, user = coordinator.User.Id })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ImportBatches WHERE UploadedBy = @user AND CommittedBy = @user AND Status = 'Committed' AND Id IN @ids",
            new { user = coordinator.User.Id, ids = new[] { first.Id, second.Id } })).Should().Be(2);
    }

    /// <summary>The export cap is exact: 5 000 rows export; one more is refused with the total, and nothing is written.</summary>
    [ServiceAccountSqlFact]
    public async Task AccountExport_RowLimitIsExact_AndRefusalIsNotAudited()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN LIMIT " + fx.Suffix);
        SynUser reporter = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Report);
        await fx.GrantAsync(reporter, ScopeKind.Organization, org);
        await SeedAccountsAsync(fx, org, 0, ServiceAccountService.MaxExportRows);
        ReportExport full = Ok(await fx.Service.ExportAccountsAsync(reporter.Principal, fx.Context, new AccountListQuery(), _token));
        ExportedNames(full).Should().HaveCount(ServiceAccountService.MaxExportRows);

        await SeedAccountsAsync(fx, org, ServiceAccountService.MaxExportRows, 1);
        SaResult<ReportExport> refused = await fx.Service.ExportAccountsAsync(reporter.Principal, fx.Context, new AccountListQuery(), _token);
        refused.ErrorCode.Should().Be(SaErrors.Invalid);
        refused.Field.Should().Be("tooManyRows");
        refused.Current.Should().Be(ServiceAccountService.MaxExportRows + 1);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a AND Action = 'ServiceAccount.AccountsExported'",
            new { a = reporter.User.Id.ToString("D") })).Should().Be(1, "only the delivered export is audited");
    }

    private static async Task SeedAccountsAsync(ServiceAccountSqlFixture fx, Guid org, int from, int count)
    {
        await using SqlConnection connection = fx.Connection();
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, IdentityKey, IdentityState, ReportOrganizationId, LifecycleState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            SELECT NEWID(), CONCAT(@prefix, n), UPPER(CONCAT(@prefix, n)), CONCAT('N:', UPPER(CONCAT(@prefix, n))), 'Provisional', @org, 'Active',
                SYSUTCDATETIME(), @by, SYSUTCDATETIME(), @by
            FROM (SELECT TOP (@count) @from + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) numbers;
            """, new { prefix = "SYNLIM" + fx.Suffix + "_", org, count, from, by = Guid.NewGuid() }, commandTimeout: 120);
    }

    private static async Task<Guid> AccountIdAsync(ServiceAccountSqlFixture fx, string name)
    {
        await using SqlConnection connection = fx.Connection();
        return await connection.ExecuteScalarAsync<Guid>("SELECT Id FROM svcacct.Accounts WHERE AccountName = @name", new { name });
    }

    private static string[] ExportedNames(ReportExport export) =>
        [.. SpreadsheetReader.Read(export.Content, new SpreadsheetLimits(), ["Hesaplar"], out _).Single().Rows
            .Select(r => r.Cells.TryGetValue("A", out SheetCell? cell) ? cell.Text : null).OfType<string>().Where(t => t.StartsWith("SYN", StringComparison.Ordinal))];

    [ServiceAccountSqlFact]
    public async Task Absence_IsInferredOnlyFromAValidatedCompleteList()
    {
        ServiceAccountSqlFixture fx = new();
        string xName = "SYN COV X " + fx.Suffix, yName = "SYN COV Y " + fx.Suffix, otherName = "SYN COV O " + fx.Suffix;
        Guid x = await fx.OrganizationAsync(xName), y = await fx.OrganizationAsync(yName), other = await fx.OrganizationAsync(otherName);
        SynUser coordinator = await fx.UserAsync(_importer);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, x);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, y);
        string a = $"SYN{fx.Suffix}_CA", b = $"SYN{fx.Suffix}_CB", c = $"SYN{fx.Suffix}_CC";
        await CommitAsync(fx, coordinator, List(xName, (a, 45000), (b, 45001), (c, 45002)), new DateOnly(2026, 9, 7));
        byte[] onlyA = List(xName, (a, 46000));
        DateOnly date = new(2026, 9, 14);

        foreach (string coverage in new[] { ServiceAccountImportCoverage.Unknown, ServiceAccountImportCoverage.Partial })
        {
            ImportBatchView partial = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "kismi.xlsx", onlyA, date, Declared(coverage));
            partial.Coverage.Should().Be(coverage);
            partial.Summary!.NotSeen.Should().Be(0, "a partial or unknown list never implies that an account is absent");
            partial.Warnings.Should().Contain("CoverageNotComplete:" + coverage);
        }

        (await TryStageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "t.xlsx", onlyA, date, Complete())).Field
            .Should().Be("coverageOrganizationIds", "a complete list must name its population");
        (await TryStageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "t.xlsx", onlyA, null, Complete(x))).Field
            .Should().Be("sourceReportDate", "absence needs a dated source");
        (await TryStageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "t.xlsx", onlyA, date, Complete(other))).Field
            .Should().Be("coverageOrganizationIds", "the population must be inside the importer's scope");
        (await TryStageAsync(fx, coordinator, ServiceAccountImportProfiles.DbaHandover, "t.xlsx", onlyA, date, Complete(x))).Field
            .Should().Be("coverage");
        (await TryStageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "t.xlsx", onlyA, date, new StageImportRequest(string.Empty, null,
            string.Empty, Coverage: ServiceAccountImportCoverage.Partial, CoverageOrganizationIds: [x]))).Field.Should().Be("coverageOrganizationIds");

        ImportBatchView complete = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "tam.xlsx", onlyA, date, Complete(x));
        complete.Coverage.Should().Be(ServiceAccountImportCoverage.Complete);
        complete.CoverageOrganizationIds.Should().Equal(x);
        complete.Summary!.CoveragePopulation.Should().Be(3);
        complete.Summary.NotSeen.Should().Be(2);

        byte[] mixed = SyntheticWorkbook.Create([("Sheet2", [
            (1, new SynCell?[] { "Kullanıcı Adı", "Son Parola Değişiklik Zamanı", "AD veya LDAP Son Oturum Açma Zamanı", "AD Son Oturum Açma Zamanı", "Organizasyon", "Grup Direktorlugu", "Yorum" }),
            (2, new SynCell?[] { a, 46000d, 46000d, 46000d, xName, "SYN GRUP", null }),
            (3, new SynCell?[] { $"SYN{fx.Suffix}_CY", 46000d, 46000d, 46000d, yName, "SYN GRUP", null })])]);
        ImportBatchView contradicted = await StageAsync(fx, coordinator, ServiceAccountImportProfiles.CoordinationList, "karma.xlsx", mixed, date, Complete(x));
        contradicted.Summary!.CoverageOutsideRows.Should().Be(1);
        contradicted.Summary.NotSeen.Should().Be(0, "a row outside the declared population contradicts the complete-list declaration");
        contradicted.Warnings.Should().Contain("CoverageContradicted:1");

        ImportBatchView committed = await CommitStagedAsync(fx, coordinator, complete);
        committed.Result!.NotSeenObservations.Should().Be(2);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE ReportOrganizationId = @x AND LastObservationPresence = 'NotPresent' AND LifecycleState = 'Active'",
            new { x })).Should().Be(2, "absence is an observation, never a closure");
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

    private static async Task<ImportBatchView> StageAsync(ServiceAccountSqlFixture fx, SynUser user, string profile, string file, byte[] bytes, DateOnly? date,
        StageImportRequest? coverage = null) =>
        Ok(await TryStageAsync(fx, user, profile, file, bytes, date, coverage));

    private static Task<SaResult<ImportBatchView>> TryStageAsync(ServiceAccountSqlFixture fx, SynUser user, string profile, string file, byte[] bytes, DateOnly? date,
        StageImportRequest? coverage = null) =>
        fx.Service.StageImportAsync(user.Principal, fx.Context, new StageImportRequest(profile, date, "Sentetik beyan: kaynak rapor tarihi test verisidir",
            Coverage: coverage?.Coverage, CoverageOrganizationIds: coverage?.CoverageOrganizationIds), file, "application/octet-stream", bytes, _token);

    /// <summary>Coverage declaration carrier (only Coverage and CoverageOrganizationIds are used).</summary>
    private static StageImportRequest Complete(params Guid[] organizations) =>
        new(string.Empty, null, string.Empty, Coverage: ServiceAccountImportCoverage.Complete, CoverageOrganizationIds: organizations);

    private static StageImportRequest Declared(string coverage) => new(string.Empty, null, string.Empty, Coverage: coverage);

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
