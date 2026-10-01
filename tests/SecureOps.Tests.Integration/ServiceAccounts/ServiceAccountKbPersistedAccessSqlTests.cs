using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;
using static SecureOps.Tests.Integration.ServiceAccounts.ServiceAccountPersistedAccessSqlTests;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Team roles are module-wide; tests that configure the gMSA executing team run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServiceAccountTeamRoleCollection
{
    /// <summary>Collection name.</summary>
    public const string Name = "Service Accounts team roles";
}

/// <summary>
/// Knowledge-base rules, gMSA routing and report v2 through the production access composition (SQL access repository,
/// versioned role bundles, the real <see cref="ApplicationAccessService"/>) — the closest composition this Linux host can
/// run: the HTTP host refuses SQL logins for the access store (Integrated Security), so the allowed browser journey is a
/// Windows acceptance step. Synthetic data only.
/// </summary>
[Collection(ServiceAccountTeamRoleCollection.Name)]
public sealed class ServiceAccountKbPersistedAccessSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [ServiceAccountSqlFact]
    public async Task RulesRoutingScopeAndSnapshots_HoldUnderPersistedRoleBundles()
    {
        string connectionString = Environment.GetEnvironmentVariable(ServiceAccountSqlFactAttribute.Variable)!;
        new SqlConnectionStringBuilder(connectionString).InitialCatalog.Should().StartWith("SecureOps_Sa", "synthetic disposable databases only");
        await using ServiceProvider services = Compose(connectionString);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var access = (SqlAccessRepository)scope.ServiceProvider.GetRequiredService<IAccessRepository>();
        ServiceAccountService module = scope.ServiceProvider.GetRequiredService<ServiceAccountService>();
        string prefix = "sa-kb-" + Guid.NewGuid().ToString("N")[..10];
        AccessOperationContext context = new("synthetic", prefix, null);

        string platformAdmin = await PlatformAdminAsync(connectionString, prefix);
        string adminRole = await BundleAsync(access, platformAdmin, prefix + "-admin", [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer]);
        string coordRole = await BundleAsync(access, platformAdmin, prefix + "-coord", [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work,
            ServiceAccountCapabilities.Assign, ServiceAccountCapabilities.Import, ServiceAccountCapabilities.Report]);
        string verifierRole = await BundleAsync(access, platformAdmin, prefix + "-verif", [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Verify]);
        string memberRole = await BundleAsync(access, platformAdmin, prefix + "-member", [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work,
            ServiceAccountCapabilities.Report]);
        IReadOnlyList<AccessRoleDefinition> reviewed = await access.GetRoleDefinitionsAsync(_token);
        (string _, ClaimsPrincipal admin) = await ApprovedAsync(access, platformAdmin, prefix + "-u-admin", adminRole, reviewed);
        (string coordId, ClaimsPrincipal coord) = await ApprovedAsync(access, platformAdmin, prefix + "-u-coord", coordRole, reviewed);
        (string verifierId, ClaimsPrincipal verifier) = await ApprovedAsync(access, platformAdmin, prefix + "-u-verif", verifierRole, reviewed);
        (string memberId, ClaimsPrincipal member) = await ApprovedAsync(access, platformAdmin, prefix + "-u-member", memberRole, reviewed);

        // Dictionaries and scope through the module administrator only.
        Guid org = Ok(await module.SaveOrganizationAsync(admin, context, null, new SaveOrganizationRequest("SYN KB ORG " + prefix, "Department", null), _token));
        Guid otherOrg = Ok(await module.SaveOrganizationAsync(admin, context, null, new SaveOrganizationRequest("SYN KB DIGER " + prefix, "Department", null), _token));
        string sqlName = "SYN KB SQL " + prefix, executorName = "SYN KB WASAS " + prefix;
        Guid sqlTeam = Ok(await module.SaveTeamAsync(admin, context, null, new SaveTeamRequest(sqlName, org), _token));
        Guid executor = Ok(await module.SaveTeamAsync(admin, context, null, new SaveTeamRequest(executorName, org), _token));
        Guid memberTeam = Ok(await module.SaveTeamAsync(admin, context, null, new SaveTeamRequest("SYN KB UYG " + prefix, otherOrg), _token));
        Ok(await module.CreateGrantAsync(admin, context, new CreateScopeGrantRequest(coordId, "Organization", org, null, "Sentetik"), access, _token));
        Ok(await module.CreateGrantAsync(admin, context, new CreateScopeGrantRequest(verifierId, "Organization", org, null, "Sentetik"), access, _token));
        Ok(await module.CreateGrantAsync(admin, context, new CreateScopeGrantRequest(memberId, "Team", null, memberTeam, "Sentetik"), access, _token));

        // Team roles: administration only; a configuration change grants nothing.
        foreach (TeamRoleView old in Ok(await module.TeamRolesAsync(admin, context, _token)).Where(r => r.Role == ServiceAccountTeamRoles.GmsaExecutor))
        {
            Ok(await module.RevokeTeamRoleAsync(admin, context, old.Id, new RevokeTeamRoleRequest("Sentetik test: yürütücü değişimi"), _token));
        }

        (await module.CreateTeamRoleAsync(coord, context, new CreateTeamRoleRequest(sqlTeam, ServiceAccountTeamRoles.SqlTeam, "x"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        Ok(await module.CreateTeamRoleAsync(admin, context, new CreateTeamRoleRequest(sqlTeam, ServiceAccountTeamRoles.SqlTeam, "Sentetik SQL ekibi"), _token));

        // Missing executing team: the SQL-team list imports, nothing is routed and the preview says nothing about routing.
        string first = "SYN_KB_A_" + prefix[^6..], manual = "SYN_KB_M_" + prefix[^6..], later = "SYN_KB_L_" + prefix[^6..];
        foreach (string name in new[] { first, manual, later })
        {
            // Organization-scoped importers update existing accounts; creating accounts from a DBA list needs whole-module scope.
            Ok(await module.CreateAccountAsync(coord, context, new CreateAccountRequest(name, null, org, "Sentetik"), _token));
        }

        ImportBatchView noExecutor = await StageAsync(module, coord, context, Dba((first, sqlName), (manual, sqlName)), new DateOnly(2026, 9, 21), executorName);
        (await RowsAsync(module, coord, context, noExecutor.Id)).SelectMany(r => r.Diff).Should().NotContain(d => d.Field.StartsWith("gMSA yönlendirme", StringComparison.Ordinal));
        await CommitAsync(module, coord, context, noExecutor);
        Guid firstId = await IdAsync(connectionString, first), manualId = await IdAsync(connectionString, manual);
        (await CountAsync(connectionString, "SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId IN (@firstId, @manualId)", new { firstId, manualId })).Should().Be(0);

        // A person opens a gMSA request by hand (e.g. from the recommendation); a later routing must not duplicate it.
        Ok(await module.CreateRequestAsync(coord, context, manualId, new CreateWorkRequest("GmsaHandover", TargetTeamId: executor, Notes: "elle"), _token));
        Ok(await module.CreateTeamRoleAsync(admin, context, new CreateTeamRoleRequest(executor, ServiceAccountTeamRoles.GmsaExecutor, "Sentetik yürütücü"), _token));
        ImportBatchView routed = await StageAsync(module, coord, context, Dba((first, sqlName), (manual, sqlName), (later, sqlName)), new DateOnly(2026, 9, 28),
            executorName);
        IReadOnlyList<ImportRowView> rows = await RowsAsync(module, coord, context, routed.Id);
        rows.Where(r => r.Diff.Any(d => d.Field == "gMSA yönlendirme (SQL-EKIP)")).Select(r => r.AccountLabel).Should()
            .HaveCount(2).And.Contain(l => l!.Contains(first)).And.Contain(l => l!.Contains(later));
        await CommitAsync(module, coord, context, routed);
        Guid laterId = await IdAsync(connectionString, later);
        (await CountAsync(connectionString, "SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @manualId AND ActionType = 'GmsaHandover'", new { manualId }))
            .Should().Be(1, "the manual request already puts the account in the gMSA flow");
        foreach (Guid id in new[] { firstId, laterId })
        {
            (await CountAsync(connectionString, "SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @id AND ActionType = 'GmsaHandover' AND TargetTeamId = @executor",
                new { id, executor })).Should().Be(1);
        }

        ImportBatchView repeat = await StageAsync(module, coord, context, Dba((first, sqlName), (later, sqlName)), new DateOnly(2026, 9, 30), executorName);
        (await RowsAsync(module, coord, context, repeat.Id)).SelectMany(r => r.Diff).Should().NotContain(d => d.Field.StartsWith("gMSA yönlendirme", StringComparison.Ordinal));
        await CommitAsync(module, coord, context, repeat);
        (await CountAsync(connectionString, "SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId IN (@firstId, @laterId, @manualId)",
            new { firstId, laterId, manualId })).Should().Be(3, "one gMSA request per account, never a second");

        // Usage/rule selection, Oracle manual review and a verifier-only exception on an account in the coordinator's scope.
        AccountDetail account = Ok(await module.CreateAccountAsync(coord, context, new CreateAccountRequest("SYN_KB_U_" + prefix[^6..], "SYN", org, "Sentetik"), _token));
        Guid usageAccount = account.Summary.Id;
        account = Ok(await module.CreateUsageAsync(coord, context, usageAccount, new CreateUsageRequest("Database", "Oracle"), _token));
        account.Rule!.Path.Should().Be(nameof(RecommendedPath.ManualDecision));
        account.Rule.Conformance.Should().Be(nameof(RuleConformance.ManualReviewPending));
        account = Ok(await module.CreateUsageAsync(coord, context, usageAccount, new CreateUsageRequest("ScheduledTask", Server: "SYNSRV02"), _token));
        account.Rule!.Conformance.Should().Be(nameof(RuleConformance.Unplanned));
        UsageView task = account.Usages!.Single(u => u.Kind == "ScheduledTask");
        (await module.SetUsageExceptionAsync(coord, context, task.Id, new UsageExceptionRequest(task.Version, "yetkisiz"), _token)).ErrorCode
            .Should().Be(SaErrors.Forbidden, "the coordinator bundle has no Verify");
        ServiceAccountReport before = Ok(await module.WeeklyReportAsync(coord, context, new WeeklyReportQuery(new DateOnly(2026, 9, 28)), _token));
        SnapshotItem snapshotA = Ok(await module.CreateSnapshotAsync(coord, context, new CreateSnapshotRequest(before.WeekStart, before.AsOf, null, null, "Manager",
            "Sentetik A"), _token));

        account = Ok(await module.SetUsageExceptionAsync(verifier, context, task.Id, new UsageExceptionRequest(task.Version, "Sentetik: alternatif yok, onaylı"), _token));
        account.Rule!.Path.Should().Be(nameof(RecommendedPath.ManualDecision), "the excepted task leaves the Oracle manual review");

        // Scope: the member team sees neither the account, its usages nor out-of-scope report rows.
        (await module.AccountAsync(member, context, usageAccount, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await module.CreateUsageAsync(member, context, usageAccount, new CreateUsageRequest("FileShare"), _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        ServiceAccountReport memberReport = Ok(await module.WeeklyReportAsync(member, context, new WeeklyReportQuery(new DateOnly(2026, 9, 28)), _token));
        string tag = prefix[^6..];
        memberReport.Rules!.Lines.Should().NotContain(l => l.Account.Contains(tag));
        memberReport.Directorate!.Should().NotContain(r => r.Team == sqlName || r.Team == executorName);
        (await module.SnapshotAsync(member, context, snapshotA.Id, _token)).ErrorCode.Should().Be(SaErrors.NotFound, "a snapshot is visible only to a covering scope");

        // Snapshot B after the change; A is unchanged and both can be compared.
        ServiceAccountReport now = Ok(await module.WeeklyReportAsync(coord, context, new WeeklyReportQuery(new DateOnly(2026, 9, 28)), _token));
        SnapshotItem snapshotB = Ok(await module.CreateSnapshotAsync(coord, context, new CreateSnapshotRequest(now.WeekStart, now.AsOf, null, null, "Manager",
            "Sentetik B"), _token));
        ServiceAccountReport storedA = Ok(await module.SnapshotAsync(coord, context, snapshotA.Id, _token));
        ServiceAccountReport storedB = Ok(await module.SnapshotAsync(coord, context, snapshotB.Id, _token));
        storedA.Rules!.AgainstRule.Should().Be(before.Rules!.AgainstRule);
        storedB.Rules!.AgainstRule.Should().Be(storedA.Rules.AgainstRule - 1, "the exception removed one against-rule account");
        storedA.Funnel!.Population.Should().Be(before.Funnel!.Population);
        (await SnapshotRowAsync(connectionString, snapshotA.Id)).PayloadSha256.Should().Be(snapshotA.PayloadSha256);

        // A version 1 snapshot (stored before this change) stays readable and exportable without new sections.
        Guid v1 = await StoreVersionOneSnapshotAsync(connectionString, snapshotA.Id, before);
        ServiceAccountReport legacy = Ok(await module.SnapshotAsync(coord, context, v1, _token));
        legacy.MetricDefinitionVersion.Should().Be("sa-metrics-v1");
        legacy.Rules.Should().BeNull();
        legacy.Directorate.Should().BeNull();
        IReadOnlyList<string> legacySheets = Sheets(Ok(await module.ExportSnapshotAsync(coord, context, v1, "xlsx", _token)).Content);
        legacySheets.Should().NotContain(["Direktörlük görünümü", "gMSA hunisi", "İncelenecek hesaplar"]);

        // Exports come from the stored v2 payload.
        ReportExport xlsx = Ok(await module.ExportSnapshotAsync(coord, context, snapshotB.Id, "xlsx", _token));
        Sheets(xlsx.Content).Should().Contain(["Direktörlük görünümü", "Bilgi bankası kuralları", "İncelenecek hesaplar", "gMSA hunisi", "Trend (son haftalar)",
            "Risk adayları"]);
        ReportExport pdf = Ok(await module.ExportSnapshotAsync(coord, context, snapshotB.Id, "pdf", _token));
        Encoding.ASCII.GetString(pdf.Content, 0, 5).Should().Be("%PDF-");
        (await CountAsync(connectionString, "SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @prefix AND Action = 'ServiceAccount.ReportExported'",
            new { prefix })).Should().Be(3);
    }

    private static ServiceProvider Compose(string connectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SecureOpsDb"] = connectionString,
            ["Access:RepositoryProvider"] = "SqlServer",
            ["Audit:Provider"] = "SqlServer",
            ["Audit:Queue:Enabled"] = "false",
            ["OperationalRecords:SourceProvider"] = "Disabled",
            ["Jira:Provider"] = "Disabled",
            ["ServiceAccounts:Provider"] = "SqlServer"
        }).Build();
        ServiceCollection registrations = new();
        registrations.AddSingleton(configuration);
        registrations.AddLogging();
        registrations.AddSecureOpsInfrastructure(configuration);
        registrations.AddServiceAccounts(configuration);
        return registrations.BuildServiceProvider();
    }

    /// <summary>Stores a snapshot exactly as version 1 code wrote it: no version 2 properties in the payload.</summary>
    private static async Task<Guid> StoreVersionOneSnapshotAsync(string connectionString, Guid template, ServiceAccountReport report)
    {
        JsonObject payload = JsonSerializer.SerializeToNode(report with { MetricDefinitionVersion = "sa-metrics-v1" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            .AsObject();
        foreach (string property in new[] { "rules", "funnel", "trend", "risk", "directorate" })
        {
            payload.Remove(property);
        }

        string json = payload.ToJsonString();
        string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var id = Guid.NewGuid();
        await using SqlConnection connection = new(connectionString);
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.ReportSnapshots(Id, Kind, PeriodStart, PeriodEnd, AsOf, ScopeJson, ScopeHash, MetricDefinitionVersion, InputWatermark, PayloadJson,
                PayloadSha256, Label, CreatedAt, CreatedBy)
            SELECT @id, Kind, PeriodStart, PeriodEnd, AsOf, ScopeJson, ScopeHash, 'sa-metrics-v1', InputWatermark, @json, @sha, N'Sentetik v1', CreatedAt, CreatedBy
            FROM svcacct.ReportSnapshots WHERE Id = @template;
            """, new { id, json, sha, template });
        return id;
    }

    private static async Task<(string PayloadSha256, string PayloadJson)> SnapshotRowAsync(string connectionString, Guid id)
    {
        await using SqlConnection connection = new(connectionString);
        return await connection.QuerySingleAsync<(string, string)>("SELECT PayloadSha256, PayloadJson FROM svcacct.ReportSnapshots WHERE Id = @id", new { id });
    }

    private static IReadOnlyList<string> Sheets(byte[] xlsx)
    {
        using ZipArchive zip = new(new MemoryStream(xlsx));
        using StreamReader reader = new(zip.GetEntry("xl/workbook.xml")!.Open());
        return [.. System.Xml.Linq.XDocument.Parse(reader.ReadToEnd()).Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => (string)e.Attribute("name")!)];
    }

    private static async Task<ImportBatchView> StageAsync(ServiceAccountService module, ClaimsPrincipal user, AccessOperationContext context, byte[] file, DateOnly date,
        string target) =>
        Ok(await module.StageImportAsync(user, context, new StageImportRequest(ServiceAccountImportProfiles.DbaHandover, date, "Sentetik beyan", TargetTeam: target),
            "dba.xlsx", "application/octet-stream", file, _token));

    private static async Task<IReadOnlyList<ImportRowView>> RowsAsync(ServiceAccountService module, ClaimsPrincipal user, AccessOperationContext context, Guid id) =>
        Ok(await module.ImportRowsAsync(user, context, id, null, null, false, 1, 200, _token)).Items;

    private static async Task CommitAsync(ServiceAccountService module, ClaimsPrincipal user, AccessOperationContext context, ImportBatchView staged) =>
        Ok(await module.CommitImportAsync(user, context, staged.Id, new ImportCommitRequest(staged.PreviewVersion, staged.DecisionVersion),
            "k-" + Guid.NewGuid().ToString("N"), _token));

    private static async Task<Guid> IdAsync(string connectionString, string name)
    {
        await using SqlConnection connection = new(connectionString);
        return await connection.QuerySingleAsync<Guid>("SELECT Id FROM svcacct.Accounts WHERE AccountName = @name", new { name });
    }

    private static async Task<int> CountAsync(string connectionString, string sql, object parameters)
    {
        await using SqlConnection connection = new(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
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
