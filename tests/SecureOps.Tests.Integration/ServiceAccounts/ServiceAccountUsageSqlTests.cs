using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// Usages, knowledge-base rule evaluation, team roles and gMSA routing on the real svcacct schema (SA-001 + SA-002).
/// Team roles are module-wide, so every test that needs the executing team lives in this one (sequential) class.
/// </summary>
public sealed class ServiceAccountUsageSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [ServiceAccountSqlFact]
    public async Task Usages_DriveAnExplainedRule_ExceptionsNeedAVerifier_AndNothingIsHardDeleted()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN USAGE ORG " + fx.Suffix);
        Guid otherOrg = await fx.OrganizationAsync("SYN USAGE OTHER " + fx.Suffix);
        Guid otherTeam = await fx.TeamAsync("SYN USAGE OTHER TEAM " + fx.Suffix, otherOrg);
        SynUser coordinator = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work, ServiceAccountCapabilities.Assign);
        SynUser verifier = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Verify);
        SynUser outsider = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work, ServiceAccountCapabilities.Verify);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        await fx.GrantAsync(verifier, ScopeKind.Organization, org);
        await fx.GrantAsync(outsider, ScopeKind.Team, team: otherTeam);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest("SYN_USE_" + fx.Suffix, "SYN", org, "Sentetik test hesabı"), _token));
        Guid id = account.Summary.Id;
        account.Rule!.Conformance.Should().Be(nameof(RuleConformance.NotAssessed));
        account.Usages.Should().BeEmpty();

        (await fx.Service.CreateUsageAsync(coordinator.Principal, fx.Context, id, new CreateUsageRequest("FileShare", DatabaseEngine: "SqlServer"), _token))
            .Field.Should().Be("databaseEngine");
        (await fx.Service.CreateUsageAsync(coordinator.Principal, fx.Context, id, new CreateUsageRequest("Printer"), _token)).Field.Should().Be("kind");
        (await fx.Service.CreateUsageAsync(outsider.Principal, fx.Context, id, new CreateUsageRequest("FileShare"), _token))
            .ErrorCode.Should().Be(SaErrors.NotFound, "an out-of-scope account is indistinguishable from a missing one");

        AccountDetail withShare = Ok(await fx.Service.CreateUsageAsync(coordinator.Principal, fx.Context, id,
            new CreateUsageRequest("FileShare", Server: "SYNSRV01", Component: "\\\\SYNSRV01\\paylasim"), _token));
        withShare.Rule!.Path.Should().Be(nameof(RecommendedPath.SolutionTeamHandover));
        withShare.Rule.Conformance.Should().Be(nameof(RuleConformance.Unplanned));
        withShare.Rule.Items.Should().ContainSingle().Which.RuleCode.Should().Be("KB-DOSYA");

        AccountDetail withDatabase = Ok(await fx.Service.CreateUsageAsync(coordinator.Principal, fx.Context, id, new CreateUsageRequest("Database"), _token));
        UsageView database = withDatabase.Usages!.Single(u => u.Kind == "Database");
        database.DatabaseEngine.Should().Be("Unknown");
        withDatabase.Rule!.Path.Should().Be(nameof(RecommendedPath.NeedsInformation));

        AccountDetail oracle = Ok(await fx.Service.UpdateUsageAsync(coordinator.Principal, fx.Context, database.Id,
            new UpdateUsageRequest(database.Version, DatabaseEngine: "Oracle"), _token));
        oracle.Rule!.Path.Should().Be(nameof(RecommendedPath.SplitAccount), "solution team and removal are different targets");
        (await fx.Service.UpdateUsageAsync(coordinator.Principal, fx.Context, database.Id, new UpdateUsageRequest(database.Version, Notes: "eski sürüm"), _token))
            .ErrorCode.Should().Be(SaErrors.Conflict);

        UsageView share = oracle.Usages!.Single(u => u.Kind == "FileShare");
        (await fx.Service.SetUsageExceptionAsync(coordinator.Principal, fx.Context, share.Id, new UsageExceptionRequest(share.Version, "Sentetik gerekçe"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, "an exception is a verifier decision");
        AccountDetail excepted = Ok(await fx.Service.SetUsageExceptionAsync(verifier.Principal, fx.Context, share.Id,
            new UsageExceptionRequest(share.Version, "Sentetik gerekçe: paylaşım uygulama tarafından zorunlu"), _token));
        excepted.Rule!.Path.Should().Be(nameof(RecommendedPath.NoServiceAccountNeeded));
        excepted.Rule.Items.Should().Contain(i => i.RuleCode == "KB-DOSYA" && i.Excepted);

        UsageView oracleUsage = excepted.Usages!.Single(u => u.Kind == "Database");
        (await fx.Service.RemoveUsageAsync(coordinator.Principal, fx.Context, oracleUsage.Id, new RemoveUsageRequest(oracleUsage.Version, " "), _token))
            .Field.Should().Be("reason");
        AccountDetail removed = Ok(await fx.Service.RemoveUsageAsync(coordinator.Principal, fx.Context, oracleUsage.Id,
            new RemoveUsageRequest(oracleUsage.Version, "Sentetik: veritabanı erişimi kaldırıldı"), _token));
        removed.Usages!.Single(u => u.Id == oracleUsage.Id).Removed.Should().BeTrue();
        removed.Rule!.Conformance.Should().Be(nameof(RuleConformance.Exception), "only the excepted usage remains active");

        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id AND EntityType = 'Usage'", new { id })).Should().Be(5);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.Usage%' AND DetailsJson LIKE @id",
            new { id = "%" + id.ToString("D") + "%" })).Should().Be(5);
        await using SqlConnection connection = fx.Connection();
        Func<Task> delete = () => connection.ExecuteAsync("DELETE FROM svcacct.AccountUsages WHERE AccountId = @id", new { id });
        (await delete.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(51305);
    }

    [ServiceAccountSqlFact]
    public async Task SqlTeamAccounts_AreRoutedToTheExecutingTeamOnImport_OnceAndVisibly_AndTheReportsAgree()
    {
        ServiceAccountSqlFixture fx = new();
        string sqlName = "SYN SQL " + fx.Suffix, executorName = "SYN WASAS " + fx.Suffix, otherName = "SYN UYG " + fx.Suffix;
        Guid sqlTeam = await fx.TeamAsync(sqlName, null);
        Guid executor = await fx.TeamAsync(executorName, null);
        await fx.TeamAsync(otherName, null);
        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        SynUser coordinator = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Import, ServiceAccountCapabilities.Report,
            ServiceAccountCapabilities.Work);
        await fx.GrantAsync(admin, ScopeKind.All);
        await fx.GrantAsync(coordinator, ScopeKind.All);

        (await fx.Service.CreateTeamRoleAsync(coordinator.Principal, fx.Context, new CreateTeamRoleRequest(sqlTeam, ServiceAccountTeamRoles.SqlTeam, "Sentetik"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.Service.CreateTeamRoleAsync(admin.Principal, fx.Context, new CreateTeamRoleRequest(sqlTeam, "Owner", "Sentetik"), _token)).Field.Should().Be("role");
        await ReplaceExecutorAsync(fx, admin, executor);
        Ok(await fx.Service.CreateTeamRoleAsync(admin.Principal, fx.Context, new CreateTeamRoleRequest(sqlTeam, ServiceAccountTeamRoles.SqlTeam, "Sentetik SQL ekibi"), _token));
        (await fx.Service.CreateTeamRoleAsync(admin.Principal, fx.Context, new CreateTeamRoleRequest(sqlTeam, ServiceAccountTeamRoles.SqlTeam, "Tekrar"), _token))
            .Field.Should().Be("role", "an active duplicate is rejected");
        (await fx.Service.CreateTeamRoleAsync(admin.Principal, fx.Context,
            new CreateTeamRoleRequest(sqlTeam, ServiceAccountTeamRoles.GmsaExecutor, "İkinci yürütücü"), _token)).Field.Should().Be("role", "one executing team");

        string routed = "SYN_GM_" + fx.Suffix, plain = "SYN_PL_" + fx.Suffix;
        byte[] file = Dba((routed, sqlName), (plain, otherName));
        ImportBatchView staged = Ok(await fx.Service.StageImportAsync(coordinator.Principal, fx.Context, new StageImportRequest(ServiceAccountImportProfiles.DbaHandover,
            new DateOnly(2026, 9, 28), "Sentetik beyan", TargetTeam: executorName), "dba.xlsx", "application/octet-stream", file, _token));
        ImportRowPage rows = Ok(await fx.Service.ImportRowsAsync(coordinator.Principal, fx.Context, staged.Id, null, null, false, 1, 50, _token));
        rows.Items.Single(r => r.AccountLabel!.Contains(routed)).Diff.Should().Contain(d => d.Field == "gMSA yönlendirme (SQL-EKIP)" && d.Proposed == executorName);
        rows.Items.Single(r => r.AccountLabel!.Contains(plain)).Diff.Should().NotContain(d => d.Field.StartsWith("gMSA yönlendirme", StringComparison.Ordinal));
        Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, staged.Id, new ImportCommitRequest(staged.PreviewVersion, staged.DecisionVersion),
            "k-" + Guid.NewGuid().ToString("N"), _token));

        Guid routedId = await AccountIdAsync(fx, routed);
        Guid plainId = await AccountIdAsync(fx, plain);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @routedId AND ActionType = 'GmsaHandover' AND Status = 'Open' AND TargetTeamId = @executor",
            new { routedId, executor })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.IdentityTransitions WHERE AccountId = @routedId AND Suitability = 'Unknown'", new { routedId })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @plainId", new { plainId })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.Accounts WHERE Id = @routedId AND CurrentOwnerTeamId IS NULL", new { routedId }))
            .Should().Be(1, "routing never assigns ownership");

        // A later list with the same account does not open a second request.
        ImportBatchView again = Ok(await fx.Service.StageImportAsync(coordinator.Principal, fx.Context, new StageImportRequest(ServiceAccountImportProfiles.DbaHandover,
            new DateOnly(2026, 9, 30), "Sentetik beyan", TargetTeam: executorName), "dba2.xlsx", "application/octet-stream", Dba((routed, sqlName)), _token));
        Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, again.Id, new ImportCommitRequest(again.PreviewVersion, again.DecisionVersion),
            "k-" + Guid.NewGuid().ToString("N"), _token));
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @routedId", new { routedId })).Should().Be(1);

        AccountDetail detail = Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, routedId, _token));
        detail.Rule!.SqlTeamAccount.Should().BeTrue("the DBA list named an SQL team as source");
        detail.Rule.Path.Should().Be(nameof(RecommendedPath.GmsaEvaluation));
        detail.Rule.Conformance.Should().Be(nameof(RuleConformance.Planned));
        detail.Rule.GmsaExecutorTeam.Should().Be(executorName);
        detail.Rule.GmsaStage.Should().Be("Bilinmiyor");

        ServiceAccountReport report = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new DateOnly(2026, 9, 28)), _token));
        report.MetricDefinitionVersion.Should().Be(ServiceAccountMetrics.DefinitionVersion);
        report.Rules!.Lines.Should().NotContain(l => l.Account.Contains(routed));
        report.Funnel!.Population.Should().BeGreaterThanOrEqualTo(1);
        report.Trend!.Points.Should().HaveCount(ServiceAccountInsights.TrendWeeks);
        report.Directorate.Should().Contain(r => r.Team == executorName && r.OpenRequestsAsTarget >= 1);

        // Without an executing team nothing is routed.
        TeamRoleView role = Ok(await fx.Service.TeamRolesAsync(admin.Principal, fx.Context, _token)).Single(r => r.Role == ServiceAccountTeamRoles.GmsaExecutor);
        Ok(await fx.Service.RevokeTeamRoleAsync(admin.Principal, fx.Context, role.Id, new RevokeTeamRoleRequest("Sentetik geri alma"), _token));
        (await fx.Service.RevokeTeamRoleAsync(admin.Principal, fx.Context, role.Id, new RevokeTeamRoleRequest("Tekrar"), _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        string late = "SYN_LATE_" + fx.Suffix;
        ImportBatchView unrouted = Ok(await fx.Service.StageImportAsync(coordinator.Principal, fx.Context, new StageImportRequest(ServiceAccountImportProfiles.DbaHandover,
            new DateOnly(2026, 9, 30), "Sentetik beyan", TargetTeam: executorName), "dba3.xlsx", "application/octet-stream", Dba((late, sqlName)), _token));
        Ok(await fx.Service.CommitImportAsync(coordinator.Principal, fx.Context, unrouted.Id, new ImportCommitRequest(unrouted.PreviewVersion, unrouted.DecisionVersion),
            "k-" + Guid.NewGuid().ToString("N"), _token));
        Guid lateId = await AccountIdAsync(fx, late);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @lateId", new { lateId })).Should().Be(0);
    }

    /// <summary>Makes <paramref name="team"/> the only gMSA executing team (team roles are module-wide).</summary>
    private static async Task ReplaceExecutorAsync(ServiceAccountSqlFixture fx, SynUser admin, Guid team)
    {
        foreach (TeamRoleView existing in Ok(await fx.Service.TeamRolesAsync(admin.Principal, fx.Context, _token)).Where(r => r.Role == ServiceAccountTeamRoles.GmsaExecutor))
        {
            Ok(await fx.Service.RevokeTeamRoleAsync(admin.Principal, fx.Context, existing.Id, new RevokeTeamRoleRequest("Sentetik test: yürütücü değişimi"), _token));
        }

        Ok(await fx.Service.CreateTeamRoleAsync(admin.Principal, fx.Context, new CreateTeamRoleRequest(team, ServiceAccountTeamRoles.GmsaExecutor, "Sentetik yürütücü"), _token));
    }

    private static async Task<Guid> AccountIdAsync(ServiceAccountSqlFixture fx, string name)
    {
        await using SqlConnection connection = fx.Connection();
        return await connection.QuerySingleAsync<Guid>("SELECT Id FROM svcacct.Accounts WHERE AccountName = @name", new { name });
    }

    private static byte[] Dba(params (string Account, string Team)[] rows) =>
        SyntheticWorkbook.Create([("Sheet1", [
            (1, new SynCell?[] { "Kullanıcı Adı", "Ekip", "Kullanan_Ekip", "WASAS_Devir" }),
            .. rows.Select((r, i) => (i + 2, new SynCell?[] { r.Account, r.Team, "SYN TUKETEN", "OK" }))])]);

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
