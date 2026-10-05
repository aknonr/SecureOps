using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// Operator-run usage scans (ADR-0027, migration 030) on the real svcacct schema: a file is attached only by someone who may
/// work on the account, a password-like field refuses it with nothing stored, coverage stays honest, a usage is created only
/// by a person's decision, and no scan changes the account, its requests, actions or closure. Synthetic data only.
/// </summary>
public sealed class ServiceAccountUsageScanSqlTests
{
    private const string _statement = "Sentetik: kendi yönetici hesabımla SYN-JUMP01 üzerinden çalıştırdım";
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];

    [ServiceAccountSqlFact]
    public async Task Coordinator_AttachesAScan_SeesHonestCoverage_AndNothingElseChanges_AndReplayAddsNothing()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "COV");
        string name = account.Summary.AccountName;
        byte[] file = Example(name);

        AccountDetail attached = Ok(await fx.Service.AttachUsageScanAsync(coordinator.Principal, fx.Context, account.Summary.Id, "scan.json", file, _statement,
            null, _token));

        UsageScanView scan = attached.UsageScans.Should().ContainSingle().Subject;
        attached.UsageScanTotal.Should().Be(1);
        scan.Purpose.Should().Be("Discovery");
        scan.MatchedAccount.Should().Be($"SYN\\{name}");
        scan.RunStatement.Should().Be(_statement);
        scan.Gmsa.Should().BeNull();
        scan.Coverage.Should().Be(new UsageScanCoverageView(4, 2, 1, 0, 1, 0, 1, 1, 1, 1));
        scan.Servers.Select(s => $"{s.ServerName}:{s.Outcome}").Should().Equal("SYN-APP01:Found", "SYN-APP02:NotFound", "SYN-APP03:Uncertain",
            "SYN-APP04:NotCovered");
        scan.Servers.Single(s => s.Outcome == "NotFound").OutcomeLabel.Should().Contain("kullanılmıyor demek değildir");
        scan.Items.Should().HaveCount(3).And.OnlyContain(i => i.Role == "Former" && i.Decision == null);
        scan.Items.Single(i => i.ComponentType == "IisAppPool").SuggestedKind.Should().Be(nameof(UsageKind.IisAppPool));

        attached.Summary.LifecycleState.Should().Be("Active", "a scan never closes or frees an account");
        attached.Requests.Should().BeEmpty();
        attached.Actions.Should().BeEmpty();
        attached.Findings.Should().BeEmpty("scan matches are not open findings");
        attached.Usages.Should().BeEmpty("a match becomes a usage only by a person's decision");

        AccountDetail replay = Ok(await fx.Service.AttachUsageScanAsync(coordinator.Principal, fx.Context, account.Summary.Id, "again.json", file,
            "Sentetik: aynı dosya ikinci kez", null, _token));
        replay.UsageScans.Should().ContainSingle().Which.FileName.Should().Be("scan.json", "the same bytes are the same evidence");
        Guid id = account.Summary.Id;
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScans WHERE UploadedBy = @u", new { u = coordinator.User.Id })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanServers s JOIN svcacct.UsageScans u ON u.Id = s.ScanId WHERE u.UploadedBy = @u",
            new { u = coordinator.User.Id })).Should().Be(4, "every planned server is kept, the unreachable one included");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id AND Action = 'UsageScanAttached'", new { id })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action = 'ServiceAccount.UsageScanAttached' AND DetailsJson LIKE @id",
            new { id = "%" + id.ToString("D") + "%" })).Should().Be(1);

        await using SqlConnection connection = fx.Connection();
        foreach (string sql in new[]
        {
            "UPDATE svcacct.UsageScans SET RunStatement = N'x' WHERE UploadedBy = @u",
            "DELETE FROM svcacct.UsageScanItems WHERE ScanId IN (SELECT Id FROM svcacct.UsageScans WHERE UploadedBy = @u)",
            "DELETE FROM svcacct.UsageScanLinks WHERE AccountId = @id"
        })
        {
            Func<Task> change = () => connection.ExecuteAsync(sql, new { u = coordinator.User.Id, id });
            (await change.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(51307);
        }
    }

    [ServiceAccountSqlFact]
    public async Task PasswordLikeField_RefusesTheWholeFile_AndNothingFromItIsStored()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "SEC");
        const string secret = "SYN-NEVER-STORED-0c61";
        JsonNode leaked = JsonNode.Parse(Encoding.UTF8.GetString(Example(account.Summary.AccountName)))!;
        leaked["results"]![0]!["components"]![0]!["Password"] = secret;
        JsonNode embedded = JsonNode.Parse(Encoding.UTF8.GetString(Example(account.Summary.AccountName)))!;
        embedded["results"]![0]!["components"]![1]!["Detail"] = $"Server=syn-db;Password={secret}";

        SaResult<AccountDetail> field = await fx.Service.AttachUsageScanAsync(coordinator.Principal, fx.Context, account.Summary.Id, "leaked.json",
            Encoding.UTF8.GetBytes(leaked.ToJsonString()), _statement, null, _token);
        SaResult<AccountDetail> value = await fx.Service.AttachUsageScanAsync(coordinator.Principal, fx.Context, account.Summary.Id, "embedded.json",
            Encoding.UTF8.GetBytes(embedded.ToJsonString()), _statement, null, _token);

        (field.ErrorCode, field.Field).Should().Be((SaErrors.UsageScanFile, "scanSecretField"));
        (value.ErrorCode, value.Field).Should().Be((SaErrors.UsageScanFile, "scanSecretValue"));
        field.Current.Should().BeNull("the refusal carries no part of the file");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScans WHERE UploadedBy = @u", new { u = coordinator.User.Id })).Should().Be(0);
        Guid id = account.Summary.Id;
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id AND EntityType = 'UsageScan'", new { id })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE DetailsJson LIKE @s", new { s = "%" + secret + "%" })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE ChangesJson LIKE @s OR Reason LIKE @s", new { s = "%" + secret + "%" })).Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task OnlySomeoneWhoMayWorkOnTheAccount_Attaches_AndTheFileMustHaveSearchedIt()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        Guid ownerTeam = await fx.TeamAsync("SYN SCAN SAHIP " + fx.Suffix, org), executor = await fx.TeamAsync("SYN SCAN GMSA " + fx.Suffix, null);
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "AUTH");
        account = Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new OwnershipChangeRequest(account.Summary.Version, ownerTeam, null, "Confirm", "Sentetik sahiplik kararı"), _token));
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new CreateWorkRequest("GmsaHandover", TargetTeamId: executor), _token));
        Guid id = account.Summary.Id, request = account.Requests.Single().Id;
        byte[] file = Example(account.Summary.AccountName);

        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(viewer, ScopeKind.Organization, org);
        (await Attach(fx, viewer, id, file, null)).ErrorCode.Should().Be(SaErrors.Forbidden, "attaching evidence needs the Work capability");

        SynUser outsider = await fx.UserAsync(_all);
        await fx.GrantAsync(outsider, ScopeKind.Team, team: await fx.TeamAsync("SYN SCAN BASKA " + fx.Suffix, null));
        (await Attach(fx, outsider, id, file, null)).ErrorCode.Should().Be(SaErrors.NotFound, "out of scope and missing look the same");

        SynUser member = await fx.UserAsync(_all);
        await fx.GrantAsync(member, ScopeKind.Team, team: executor);
        SaResult<AccountDetail> accountWide = await Attach(fx, member, id, file, null);
        (accountWide.ErrorCode, accountWide.Field).Should().Be((SaErrors.Forbidden, "requestId"), "a participant attaches only through its own request");
        AccountDetail viaRequest = Ok(await Attach(fx, member, id, file, request));
        viaRequest.UsageScans.Should().ContainSingle().Which.RequestId.Should().Be(request);
        Guid item = viaRequest.UsageScans![0].Items[0].Id;
        (await fx.Service.RecordScanUsageAsync(member.Principal, fx.Context, id, item, new RecordScanUsageRequest("IisAppPool"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, "turning evidence into a usage is the responsible team's decision");
        Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, id, _token)).UsageScans.Should().ContainSingle("the responsible team sees it");

        (await Attach(fx, coordinator, id, Example("SYN_SCAN_OTHER" + fx.Suffix), null)).Field.Should().Be("accountNotInScan");
        (await Attach(fx, coordinator, id, Example(account.Summary.AccountName, domain: "OTHER"), null)).Field.Should().Be("accountNotInScan",
            "the same name in another domain is another account");
        (await fx.Service.AttachUsageScanAsync(coordinator.Principal, fx.Context, id, "scan.json", file, " ", null, _token)).Field.Should().Be("runStatement");
        (await Attach(fx, coordinator, id, Example(account.Summary.AccountName), Guid.NewGuid())).ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    [ServiceAccountSqlFact]
    public async Task Matches_BecomeUsagesOnlyByAPersonsDecision_OncePerItem()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "DEC");
        Guid id = account.Summary.Id;
        AccountDetail attached = Ok(await fx.Service.AttachUsageScanAsync(coordinator.Principal, fx.Context, id, "scan.json", Example(account.Summary.AccountName),
            _statement, null, _token));
        UsageScanItemView pool = attached.UsageScans![0].Items.Single(i => i.ComponentType == "IisAppPool");
        UsageScanItemView task = attached.UsageScans![0].Items.Single(i => i.ComponentType == "ScheduledTask");

        (await fx.Service.RecordScanUsageAsync(coordinator.Principal, fx.Context, id, pool.Id, new RecordScanUsageRequest("Printer"), _token))
            .Field.Should().Be("kind");
        (await fx.Service.RecordScanUsageAsync(coordinator.Principal, fx.Context, id, pool.Id, new RecordScanUsageRequest("IisAppPool", DatabaseEngine: "Oracle"), _token))
            .Field.Should().Be("databaseEngine");
        AccountDetail recorded = Ok(await fx.Service.RecordScanUsageAsync(coordinator.Principal, fx.Context, id, pool.Id,
            new RecordScanUsageRequest("IisAppPool", Notes: "Sentetik: taramadan"), _token));
        UsageView usage = recorded.Usages.Should().ContainSingle().Subject;
        (usage.Kind, usage.Server, usage.Component).Should().Be(("IisAppPool", "SYN-APP01", "IIS uygulama havuzu: SynPool"));
        recorded.UsageScans![0].Items.Single(i => i.Id == pool.Id).Should().Match<UsageScanItemView>(i => i.Decision == "UsageRecorded" && i.UsageId == usage.Id);
        recorded.Rule!.Items.Should().Contain(r => r.UsageId == usage.Id, "a recorded usage feeds the knowledge-base rules like a manual one");
        SaResult<AccountDetail> again = await fx.Service.RecordScanUsageAsync(coordinator.Principal, fx.Context, id, pool.Id, new RecordScanUsageRequest("IisAppPool"),
            _token);
        (again.ErrorCode, again.Field).Should().Be((SaErrors.AlreadyDecided, "alreadyDecided"), "a second decision is a 409 conflict, not invalid input");

        (await fx.Service.DismissScanItemAsync(coordinator.Principal, fx.Context, id, task.Id, new DismissScanItemRequest(" "), _token)).Field.Should().Be("reason");
        AccountDetail dismissed = Ok(await fx.Service.DismissScanItemAsync(coordinator.Principal, fx.Context, id, task.Id,
            new DismissScanItemRequest("Sentetik: test sunucusu, kullanım kaydına alınmadı"), _token));
        dismissed.UsageScans![0].Items.Single(i => i.Id == task.Id).Decision.Should().Be("Dismissed");
        dismissed.Usages.Should().ContainSingle("a dismissal creates nothing");
        (await fx.Service.RecordScanUsageAsync(coordinator.Principal, fx.Context, id, task.Id, new RecordScanUsageRequest("ScheduledTask"), _token))
            .ErrorCode.Should().Be(SaErrors.AlreadyDecided);

        AccountDetail other = await CreateAccountAsync(fx, coordinator, org, "DEC2");
        (await fx.Service.RecordScanUsageAsync(coordinator.Principal, fx.Context, other.Summary.Id, task.Id, new RecordScanUsageRequest("ScheduledTask"), _token))
            .ErrorCode.Should().Be(SaErrors.NotFound, "an item belongs only to the accounts its scan is attached to");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id AND Action IN ('UsageRecordedFromScan','UsageScanItemDismissed')",
            new { id })).Should().Be(2);
    }

    [ServiceAccountSqlFact]
    public async Task GmsaCheck_IsEvidenceThatWeakensWithEveryUncoveredServer_AndVerifiesNothing()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "GMSA");
        Guid id = account.Summary.Id;
        string name = account.Summary.AccountName;

        UsageScanGmsaView still = (await GmsaAsync(fx, coordinator, id, name, ("SYN-APP01", "former"), ("SYN-APP02", "gmsa"))).Gmsa!;
        still.Conclusion.Should().Be(nameof(ScanGmsaConclusion.StillFormer));
        (still.StillFormerServers, still.RunsAsGmsaServers).Should().Be((1, 1));

        UsageScanGmsaView incomplete = (await GmsaAsync(fx, coordinator, id, name, ("SYN-APP01", "gmsa"), ("SYN-APP02", "unreachable"))).Gmsa!;
        incomplete.Conclusion.Should().Be(nameof(ScanGmsaConclusion.Incomplete), "an unreachable server may still run the former account");
        incomplete.UnknownServers.Should().Be(1);

        UsageScanView done = await GmsaAsync(fx, coordinator, id, name, ("SYN-APP01", "gmsa"), ("SYN-APP02", "none"));
        done.Gmsa!.Conclusion.Should().Be(nameof(ScanGmsaConclusion.ConvertedOnCoveredServers));
        done.Gmsa.ConclusionLabel.Should().Contain("doğrulamayı doğrulayıcı yapar");
        done.Items.Should().ContainSingle(i => i.Role == "Expected").Which.ConfiguredIdentity.Should().Be("SYN\\gmsa_scan$");

        AccountDetail after = Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, id, _token));
        after.UsageScans.Should().HaveCount(3);
        after.UsageScans![0].ScanId.Should().Be(done.ScanId, "the newest scan comes first");
        after.Summary.LifecycleState.Should().Be("Active");
        after.Actions.Should().BeEmpty("evidence never reports or verifies a conversion");
        after.Transitions.Should().BeEmpty();
    }

    private static async Task<UsageScanView> GmsaAsync(ServiceAccountSqlFixture fx, SynUser user, Guid id, string name, params (string Server, string State)[] servers)
    {
        AccountDetail detail = Ok(await fx.Service.AttachUsageScanAsync(user.Principal, fx.Context, id, "gmsa.json", GmsaCheck(name, servers), _statement, null,
            _token));
        return detail.UsageScans![0];
    }

    private static Task<SaResult<AccountDetail>> Attach(ServiceAccountSqlFixture fx, SynUser user, Guid id, byte[] file, Guid? request) =>
        fx.Service.AttachUsageScanAsync(user.Principal, fx.Context, id, "scan.json", file, _statement, request, _token);

    /// <summary>The checked-in example with the synthetic account name (and optionally another domain).</summary>
    private static byte[] Example(string name, string domain = "SYN")
    {
        string text = File.ReadAllText(Path.Combine(Root(), "contracts", "examples", "service-account-usage-scan-example.json"))
            .Replace("SYN\\\\svc_synapp", $"{domain}\\\\{name}", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>A gMSA check: "former" = a task still runs as the account, "gmsa" = a service runs as the gMSA, "none", "unreachable".</summary>
    private static byte[] GmsaCheck(string name, (string Server, string State)[] servers)
    {
        string former = $"SYN\\{name}", gmsa = "SYN\\gmsa_scan$";
        JsonArray results = [], notReached = [];
        foreach ((string server, string state) in servers)
        {
            if (state == "unreachable")
            {
                notReached.Add(new JsonObject { ["serverName"] = server, ["reason"] = "Unreachable" });
                continue;
            }

            JsonNode? task = state == "former" ? Component("ScheduledTask", "\\SynNightly", former) : null;
            JsonNode? service = state == "gmsa" ? Component("WindowsService", "SynSvc", gmsa) : null;
            results.Add(new JsonObject
            {
                ["schema"] = "service-account-usage-v1",
                ["serverName"] = server,
                ["generatedAt"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("o"),
                ["durationMs"] = 700,
                ["accounts"] = new JsonArray(former),
                ["scanResult"] = "Success",
                ["sources"] = new JsonObject { ["WindowsServices"] = "Success", ["ScheduledTasks"] = "Success", ["Iis"] = "NotInstalled" },
                ["components"] = task is null ? new JsonArray() : new JsonArray(task.DeepClone()),
                ["verification"] = new JsonObject
                {
                    ["ExpectedAccount"] = gmsa,
                    ["Status"] = task is not null ? "NotConverted" : service is not null ? "Converted" : "NoComponents",
                    ["RunningAsGmsa"] = service is null ? new JsonArray() : new JsonArray(service),
                    ["StillFormerAccount"] = task is null ? new JsonArray() : new JsonArray(task)
                },
                ["warnings"] = new JsonArray()
            });
        }

        JsonObject bundle = new()
        {
            ["schema"] = "service-account-usage-scan-v1",
            ["generatedAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"),
            ["tool"] = "Combined",
            ["accounts"] = new JsonArray(former),
            ["expectedAccount"] = gmsa,
            ["plannedServers"] = new JsonArray([.. servers.Select(s => (JsonNode)JsonValue.Create(s.Server)!)]),
            ["results"] = results,
            ["notReached"] = notReached
        };
        // Distinct bytes per call: otherwise the same person uploading the same content is one scan.
        bundle["generatedAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).AddTicks(Random.Shared.Next(1, 1_000_000)).ToString("o");
        return Encoding.UTF8.GetBytes(bundle.ToJsonString());
    }

    private static JsonObject Component(string type, string componentName, string identity) => new()
    {
        ["ComponentType"] = type,
        ["ComponentName"] = componentName,
        ["Identity"] = identity,
        ["MatchedAccount"] = identity,
        ["State"] = "Running",
        ["Detail"] = null
    };

    private static async Task<(ServiceAccountSqlFixture, SynUser, Guid)> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN SCAN ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        return (fx, coordinator, org);
    }

    private static async Task<AccountDetail> CreateAccountAsync(ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, string label) =>
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN_SCAN_{label}_{fx.Suffix}", "SYN", org, "Sentetik tarama testi hesabı"), _token));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
