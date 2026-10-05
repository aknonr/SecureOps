using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// The usage-scan upload parser (ADR-0027) on the checked-in synthetic example and mutations of it: the secret guard refuses
/// the whole file before anything is interpreted, the closed contract and its consistency rules fail closed, and the honest
/// per-server outcomes never read "not found" as "not used".
/// </summary>
public sealed class UsageScanParserTests
{
    private const int _max = 4 * 1024 * 1024;
    private const string _secret = "SYN-NEVER-STORED-71c3";
    private static readonly DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Example_IsReadWithEveryPlannedServerAndOnlyItsMatches()
    {
        ParsedUsageScan scan = UsageScanParser.Parse(Bytes(Example()), _max, _now);

        scan.Purpose.Should().Be("Discovery");
        scan.Tool.Should().Be("Combined");
        scan.Accounts.Should().Equal("SYN\\svc_synapp");
        scan.Servers.Select(s => $"{s.ServerName}:{s.Result}").Should().Equal("SYN-APP01:Success", "SYN-APP02:Success", "SYN-APP03:Partial", "SYN-APP04:Unreachable");
        scan.AnsweredServers.Should().Be(3);
        scan.Servers[2].Warnings.Should().Be("ScheduledTasks: UnauthorizedAccessException");
        scan.Servers[3].WindowsServices.Should().BeNull("an unreachable server has no source status");
        scan.Items.Should().HaveCount(3).And.OnlyContain(i => i.Role == "Former" && i.ServerName == "SYN-APP01" && i.MatchedAccount == "SYN\\svc_synapp");
        scan.Items.Single(i => i.ComponentType == "IisVirtualDirectory").Detail.Should().Be("\\\\syn-fs\\share");
        scan.FirstScannedAt.Should().Be(DateTimeOffset.Parse("2026-10-04T09:15:00+00:00"));
    }

    [Fact]
    public void ByteOrderMark_IsAccepted_ButUtf16AndNulAreNot()
    {
        byte[] json = Bytes(Example());
        UsageScanParser.Parse([0xEF, 0xBB, 0xBF, .. json], _max, _now).Servers.Should().HaveCount(4);
        Code(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Example().ToJsonString())).ToArray()).Should().Be(UsageScanFileCodes.Encoding);
        Code([.. json[..^1], 0x00, (byte)'}']).Should().Be(UsageScanFileCodes.Encoding);
    }

    [Theory]
    [InlineData("results/0/components/0", "Password")]
    [InlineData("results/0/components/0", "password")]
    [InlineData("results/0/sources", "AppPoolPassword")]
    [InlineData("", "parola")]
    [InlineData("notReached/0", "Şifre")]
    [InlineData("notReached/0", "ŞİFRE")]
    [InlineData("results/1", "connection_string")]
    [InlineData("results/0/components/1", "apiKey")]
    [InlineData("results/2", "Credential")]
    [InlineData("", "token")]
    [InlineData("results/0/components/2", "pwd")]
    public void PasswordLikeField_AnywhereRefusesTheWholeFile_WithoutEchoingIt(string path, string name)
    {
        JsonNode file = Example();
        ((JsonObject)Node(file, path)).Add(name, _secret);

        UsageScanFileException rejected = Rejected(Bytes(file));

        rejected.Code.Should().Be(UsageScanFileCodes.SecretField);
        rejected.Message.Should().Be(UsageScanFileCodes.SecretField).And.NotContain(_secret);
    }

    [Fact]
    public void PasswordLikeField_IsRefusedEvenInsideAGmsaCheck()
    {
        JsonNode file = GmsaCheck(former: false);
        ((JsonObject)file["results"]![0]!["verification"]!["RunningAsGmsa"]![0]!).Add("Password", _secret);

        Code(Bytes(file)).Should().Be(UsageScanFileCodes.SecretField);
    }

    [Theory]
    [InlineData("Server=syn-db;Password=x1")]
    [InlineData("pwd = x1")]
    [InlineData("Şifre: x1")]
    [InlineData("PAROLA=x1")]
    public void EmbeddedPasswordAssignment_InAValue_RefusesTheFile(string detail)
    {
        JsonNode file = Example();
        file["results"]![0]!["components"]![0]!["Detail"] = detail;

        Code(Bytes(file)).Should().Be(UsageScanFileCodes.SecretValue);
    }

    // Review 2026-10-05: spellings that slipped past the value guard (a quoted key, other secret words, width and invisible
    // characters). Each refuses the whole file in every free-text field the contract has.
    [Theory]
    [InlineData("{\"Password\":\"x1\"}")]
    [InlineData("'pwd' : 'x1'")]
    [InlineData("<add key=\"x\" password=\"x1\" />")]
    [InlineData("client_secret=x1")]
    [InlineData("Token: x1")]
    [InlineData("api-key=x1")]
    [InlineData("ApiKey=x1")]
    [InlineData("credential=x1")]
    [InlineData("ConnectionString=Server=syn-db")]
    [InlineData("\uFF50\uFF41\uFF53\uFF53\uFF57\uFF4F\uFF52\uFF44\uFF1Dx1")]
    [InlineData("pass\u200Bword=x1")]
    [InlineData("pass\u00ADword=x1")]
    [InlineData("pass\u2060word=x1")]
    public void EmbeddedSecret_InOtherSpellings_RefusesTheFileInEveryTextField(string text)
    {
        foreach (string field in new[] { "ComponentName", "Identity", "State", "Detail" })
        {
            JsonNode file = Example();
            file["results"]![0]!["components"]![0]![field] = text;
            Code(Bytes(file)).Should().Be(UsageScanFileCodes.SecretValue, field);
        }

        JsonNode warning = Example();
        warning["results"]![2]!["warnings"]![0] = text;
        Code(Bytes(warning)).Should().Be(UsageScanFileCodes.SecretValue, "warnings");
    }

    [Theory]
    [InlineData("LogonType=Password")]
    [InlineData("LogonType=InteractiveTokenOrPassword")]
    [InlineData("\\Syn\\Password Expiry Notification")]
    [InlineData("TokenBroker")]
    [InlineData("Syn Secret Server Agent")]
    [InlineData("D:\\syn\\credentials-ui")]
    public void OrdinaryNames_ThatOnlyMentionASecretWord_AreAccepted(string text)
    {
        JsonNode file = Example();
        file["results"]![0]!["components"]![0]!["Detail"] = text;

        UsageScanParser.Parse(Bytes(file), _max, _now).Items.Should().Contain(i => i.Detail == text);
    }

    [Fact]
    public void AValueFarLongerThanAnyContractField_IsRefusedQuickly_WithAStableCode()
    {
        JsonNode file = Example();
        file["results"]![0]!["components"]![0]!["Detail"] = string.Concat(Enumerable.Repeat("pwd pwd password ", 230_000));
        byte[] bytes = Bytes(file);
        bytes.Length.Should().BeLessThan(_max);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        Code(bytes).Should().Be(UsageScanFileCodes.Schema);
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    // Review 2026-10-05: in .NET "$" also matches before a final line feed, so a name ending in "\n" passed the patterns.
    [Fact]
    public void NamesEndingInALineFeed_AreNotTheContract()
    {
        JsonNode server = Example();
        server["plannedServers"]![0] = "SYN-APP01\n";
        server["results"]![0]!["serverName"] = "SYN-APP01\n";
        Code(Bytes(server)).Should().Be(UsageScanFileCodes.Schema);

        JsonNode account = Example();
        account["accounts"]![0] = "SYN\\svc_synapp\n";
        Code(Bytes(account)).Should().Be(UsageScanFileCodes.Schema);

        JsonNode matched = Example();
        matched["results"]![0]!["components"]![0]!["MatchedAccount"] = "SYN\\svc_synapp\n";
        Code(Bytes(matched)).Should().Be(UsageScanFileCodes.Schema);

        JsonNode date = Example();
        date["generatedAt"] = date["generatedAt"]!.GetValue<string>() + "\n";
        Code(Bytes(date)).Should().Be(UsageScanFileCodes.Schema);
    }

    [Fact]
    public void Contract_IsClosed_AndDuplicatesDepthAndSizeFailClosed()
    {
        JsonNode unknown = Example();
        unknown["results"]![0]!["components"]![0]!["Owner"] = "SYN person";
        Code(Bytes(unknown)).Should().Be(UsageScanFileCodes.Schema);

        string duplicate = Example().ToJsonString().Replace("\"tool\":\"Combined\"", "\"tool\":\"Combined\",\"Tool\":\"Jea\"", StringComparison.Ordinal);
        Code(Encoding.UTF8.GetBytes(duplicate)).Should().Be(UsageScanFileCodes.DuplicateField);

        JsonNode deep = Example();
        deep["results"]![0]!["components"]![0]!["Detail"] = JsonNode.Parse(string.Concat(Enumerable.Repeat("[", 14)) + string.Concat(Enumerable.Repeat("]", 14)));
        Code(Bytes(deep)).Should().Be(UsageScanFileCodes.Json);

        Code([]).Should().Be(UsageScanFileCodes.Empty);
        Code("   "u8.ToArray()).Should().Be(UsageScanFileCodes.Empty);
        Code("{\"schema\":"u8.ToArray()).Should().Be(UsageScanFileCodes.Json);
        Code("{\"schema\":\"x\" /* c */}"u8.ToArray()).Should().Be(UsageScanFileCodes.Json);
        UsageScanFileException tooLarge = Assert.Throws<UsageScanFileException>(() => UsageScanParser.Parse(Bytes(Example()), 100, _now));
        tooLarge.Code.Should().Be(UsageScanFileCodes.TooLarge);

        JsonNode wrongSchema = Example();
        wrongSchema["schema"] = "service-account-usage-scan-v2";
        Code(Bytes(wrongSchema)).Should().Be(UsageScanFileCodes.Schema);

        JsonNode control = Example();
        control["results"]![0]!["components"]![0]!["ComponentName"] = "Syn\u0007Pool";
        Code(Bytes(control)).Should().Be(UsageScanFileCodes.Schema);
    }

    [Fact]
    public void EveryPlannedServer_IsAnsweredOrListedOnce_AndNothingOutsideThePlanIsAccepted()
    {
        JsonNode missing = Example();
        ((JsonArray)missing["notReached"]!).Clear();
        Code(Bytes(missing)).Should().Be(UsageScanFileCodes.Servers, "a planned server may never silently disappear");

        JsonNode outside = Example();
        outside["results"]![1]!["serverName"] = "SYN-OTHER09";
        Code(Bytes(outside)).Should().Be(UsageScanFileCodes.Servers);

        JsonNode twice = Example();
        ((JsonArray)twice["results"]!).Add(twice["results"]![1]!.DeepClone());
        Code(Bytes(twice)).Should().Be(UsageScanFileCodes.Servers);

        JsonNode both = Example();
        ((JsonArray)both["notReached"]!).Add(new JsonObject { ["serverName"] = "syn-app01", ["reason"] = "NoResult" });
        Code(Bytes(both)).Should().Be(UsageScanFileCodes.Servers);

        JsonNode many = Example();
        many["plannedServers"] = new JsonArray([.. Enumerable.Range(1, 501).Select(i => (JsonNode)JsonValue.Create($"SYN-S{i:000}")!)]);
        Code(Bytes(many)).Should().Be(UsageScanFileCodes.Limits);
    }

    [Fact]
    public void Documents_MustAgreeWithTheBundleAndWithThemselves()
    {
        JsonNode lies = Example();
        lies["results"]![2]!["scanResult"] = "Success";
        Code(Bytes(lies)).Should().Be(UsageScanFileCodes.Inconsistent, "a failed source cannot be reported as a complete scan");

        JsonNode foreign = Example();
        foreign["results"]![0]!["components"]![0]!["MatchedAccount"] = "SYN\\svc_other";
        Code(Bytes(foreign)).Should().Be(UsageScanFileCodes.Inconsistent);

        JsonNode otherAccounts = Example();
        otherAccounts["results"]![1]!["accounts"] = new JsonArray("SYN\\svc_other");
        Code(Bytes(otherAccounts)).Should().Be(UsageScanFileCodes.Inconsistent);

        JsonNode failedWithMatches = Example();
        failedWithMatches["results"]![0]!["sources"] = new JsonObject { ["WindowsServices"] = "Failed", ["ScheduledTasks"] = "Failed", ["Iis"] = "Failed" };
        failedWithMatches["results"]![0]!["scanResult"] = "Failed";
        Code(Bytes(failedWithMatches)).Should().Be(UsageScanFileCodes.Inconsistent);

        JsonNode verificationWithoutCheck = GmsaCheck(former: false);
        verificationWithoutCheck["expectedAccount"] = null;
        Code(Bytes(verificationWithoutCheck)).Should().Be(UsageScanFileCodes.Inconsistent);
    }

    [Fact]
    public void Timestamps_NeedAnOffset_AndAreNeverInTheFuture()
    {
        JsonNode future = Example();
        future["results"]![0]!["generatedAt"] = "2026-10-04T12:30:00+00:00";
        Code(Bytes(future)).Should().Be(UsageScanFileCodes.FutureDate);

        JsonNode afterCombine = Example();
        afterCombine["results"]![0]!["generatedAt"] = "2026-10-04T11:00:00+00:00";
        Code(Bytes(afterCombine)).Should().Be(UsageScanFileCodes.Inconsistent, "a server cannot be scanned after the file was combined");

        JsonNode local = Example();
        local["generatedAt"] = "2026-10-04T09:40:00";
        Code(Bytes(local)).Should().Be(UsageScanFileCodes.Schema);
    }

    [Fact]
    public void GmsaCheck_StoresGmsaItems_AndItsStatusMustMatchTheComponents()
    {
        ParsedUsageScan notConverted = UsageScanParser.Parse(Bytes(GmsaCheck(former: true)), _max, _now);
        notConverted.Purpose.Should().Be("GmsaCheck");
        notConverted.ExpectedAccount.Should().Be("SYN\\gmsa_synapp$");
        notConverted.Items.Select(i => $"{i.Role}|{i.ComponentType}|{i.MatchedAccount}").Should().BeEquivalentTo(
            ["Former|ScheduledTask|SYN\\svc_synapp", "Expected|WindowsService|SYN\\gmsa_synapp$"]);

        UsageScanParser.Parse(Bytes(GmsaCheck(former: false)), _max, _now).Items.Should().ContainSingle().Which.Role.Should().Be("Expected");

        JsonNode claimsConverted = GmsaCheck(former: true);
        claimsConverted["results"]![0]!["verification"]!["Status"] = "Converted";
        Code(Bytes(claimsConverted)).Should().Be(UsageScanFileCodes.Inconsistent, "a former account still configured is never 'converted'");

        JsonNode hidden = GmsaCheck(former: true);
        ((JsonArray)hidden["results"]![0]!["verification"]!["StillFormerAccount"]!).Clear();
        hidden["results"]![0]!["verification"]!["Status"] = "Converted";
        Code(Bytes(hidden)).Should().Be(UsageScanFileCodes.Inconsistent);

        JsonNode notGmsa = GmsaCheck(former: false);
        notGmsa["expectedAccount"] = "SYN\\svc_synapp";
        Code(Bytes(notGmsa)).Should().Be(UsageScanFileCodes.Schema);
    }

    [Theory]
    [InlineData(ScanServerResult.Success, 2, ScanAccountOutcome.Found)]
    [InlineData(ScanServerResult.Success, 0, ScanAccountOutcome.NotFound)]
    [InlineData(ScanServerResult.Partial, 1, ScanAccountOutcome.Found)]
    [InlineData(ScanServerResult.Partial, 0, ScanAccountOutcome.Uncertain)]
    [InlineData(ScanServerResult.Failed, 0, ScanAccountOutcome.NotCovered)]
    [InlineData(ScanServerResult.Unreachable, 0, ScanAccountOutcome.NotCovered)]
    [InlineData(ScanServerResult.NoResult, 0, ScanAccountOutcome.NotCovered)]
    public void Outcome_NotFoundOnlyAfterACompleteScan(ScanServerResult result, int matches, ScanAccountOutcome expected) =>
        UsageScanOutcomes.Outcome(result, matches).Should().Be(expected);

    [Fact]
    public void Labels_NeverSayNotUsed_AndTheGmsaConclusionIsOnlyEvidence()
    {
        UsageScanOutcomes.OutcomeLabel(ScanAccountOutcome.NotFound).Should().Contain("kullanılmıyor demek değildir");
        UsageScanOutcomes.OutcomeLabel(ScanAccountOutcome.NotCovered).Should().Contain("Bilgi yok");
        foreach (ScanAccountOutcome outcome in Enum.GetValues<ScanAccountOutcome>())
        {
            UsageScanOutcomes.OutcomeLabel(outcome).Should().NotContainEquivalentOf("silinebilir").And.NotContainEquivalentOf("kullanılmıyor.");
        }

        UsageScanOutcomes.ConclusionLabel(ScanGmsaConclusion.ConvertedOnCoveredServers).Should().Contain("kanıt").And.Contain("doğrulamayı doğrulayıcı yapar");
        foreach (ScanGmsaConclusion conclusion in Enum.GetValues<ScanGmsaConclusion>())
        {
            UsageScanOutcomes.ConclusionLabel(conclusion).Should().NotContainEquivalentOf("doğrulandı");
        }
    }

    [Fact]
    public void GmsaConclusion_IsWeakenedByAnyServerThatIsNotFullyCovered()
    {
        UsageScanOutcomes.GmsaState(ScanServerResult.Success, 0, 1).Should().Be(ScanGmsaServerState.RunsAsGmsa);
        UsageScanOutcomes.GmsaState(ScanServerResult.Partial, 0, 1).Should().Be(ScanGmsaServerState.Unknown, "a failed source may still hold the former account");
        UsageScanOutcomes.GmsaState(ScanServerResult.Partial, 1, 1).Should().Be(ScanGmsaServerState.StillFormer);
        UsageScanOutcomes.GmsaState(ScanServerResult.Unreachable, 0, 0).Should().Be(ScanGmsaServerState.Unknown);

        UsageScanOutcomes.Conclusion([ScanGmsaServerState.RunsAsGmsa, ScanGmsaServerState.NoComponents]).Should().Be(ScanGmsaConclusion.ConvertedOnCoveredServers);
        UsageScanOutcomes.Conclusion([ScanGmsaServerState.RunsAsGmsa, ScanGmsaServerState.Unknown]).Should().Be(ScanGmsaConclusion.Incomplete);
        UsageScanOutcomes.Conclusion([ScanGmsaServerState.Unknown, ScanGmsaServerState.StillFormer]).Should().Be(ScanGmsaConclusion.StillFormer);
        UsageScanOutcomes.Conclusion([ScanGmsaServerState.NoComponents]).Should().Be(ScanGmsaConclusion.NoComponents);
        UsageScanOutcomes.Conclusion([]).Should().Be(ScanGmsaConclusion.Incomplete);
    }

    // Review 2026-10-05: the searched name decides which matches the account shows; choosing one that hides matches of
    // another searched name would turn a server with a match into "not found".
    [Theory]
    [InlineData("SYN\\svc_synapp|svc_synapp", "SYN", "SYN\\svc_synapp")]
    [InlineData("svc_synapp|SYN\\svc_synapp", "SYN", "SYN\\svc_synapp")]
    [InlineData("OTHER\\svc_synapp|svc_synapp", "SYN", "svc_synapp")]
    [InlineData("SYN\\svc_synapp", null, "SYN\\svc_synapp")]
    [InlineData("SYN\\svc_synapp|svc_synapp", null, "svc_synapp")]
    [InlineData("svc_other|svc_synapp", null, "svc_synapp")]
    [InlineData("svc_other", "SYN", null)]
    public void SearchedName_PrefersTheAccountsOwnDomain_AndNeverHidesMatchesWhenTheDomainIsUnknown(string file, string? domain, string? expected) =>
        UsageScanOutcomes.SearchedName(file.Split('|'), "svc_synapp", domain).Should().Be((expected, false));

    [Fact]
    public void SearchedName_WithTwoDomainsAndNoDomainOnTheAccount_IsAmbiguous() =>
        UsageScanOutcomes.SearchedName(["SYN\\svc_synapp", "OTHER\\svc_synapp"], "svc_synapp", null).Should().Be(((string?)null, true));

    [Theory]
    [InlineData("SYN\\svc_synapp", "svc_synapp", "SYN", true)]
    [InlineData("syn\\SVC_SYNAPP", "svc_synapp", "SYN", true)]
    [InlineData("svc_synapp", "svc_synapp", "SYN", true)]
    [InlineData("SYN\\svc_synapp", "svc_synapp", null, true)]
    [InlineData("OTHER\\svc_synapp", "svc_synapp", "SYN", false)]
    [InlineData("SYN\\svc_synapp2", "svc_synapp", "SYN", false)]
    [InlineData("SYN\\gmsa_synapp$", "gmsa_synapp", "SYN", false)]
    public void NameMatching_UsesTheAccountKey_AndADomainOnlyWhenBothSidesHaveOne(string file, string name, string? domain, bool expected) =>
        UsageScanOutcomes.NameMatches(file, name, domain).Should().Be(expected);

    [Theory]
    [InlineData("WindowsService", null, UsageKind.WindowsService)]
    [InlineData("ScheduledTask", "LogonType=Password", UsageKind.ScheduledTask)]
    [InlineData("IisAppPool", null, UsageKind.IisAppPool)]
    [InlineData("IisVirtualDirectory", "\\\\syn-fs\\share", UsageKind.IisVirtualDirectory)]
    [InlineData("IisSite", "\\\\syn-fs\\site", UsageKind.UncApplication)]
    [InlineData("IisApplication", "D:\\syn\\api", UsageKind.Other)]
    public void SuggestedKind_IsOnlyASuggestion(string type, string? detail, UsageKind expected) =>
        UsageScanOutcomes.SuggestedKind(type, detail).Should().Be(expected);

    internal static JsonNode Example() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "contracts", "examples", "service-account-usage-scan-example.json")))!;

    /// <summary>A one-server gMSA check: the service runs as the gMSA; with <paramref name="former"/> a task still runs as the old account.</summary>
    internal static JsonNode GmsaCheck(bool former)
    {
        JsonObject gmsa = Component("WindowsService", "SynSvc", "SYN\\gmsa_synapp$", "SYN\\gmsa_synapp$");
        JsonObject task = Component("ScheduledTask", "\\SynNightly", "SYN\\svc_synapp", "SYN\\svc_synapp");
        return new JsonObject
        {
            ["schema"] = "service-account-usage-scan-v1",
            ["generatedAt"] = "2026-10-04T10:05:00.0000000+00:00",
            ["tool"] = "Combined",
            ["accounts"] = new JsonArray("SYN\\svc_synapp"),
            ["expectedAccount"] = "SYN\\gmsa_synapp$",
            ["plannedServers"] = new JsonArray("SYN-APP01"),
            ["results"] = new JsonArray(new JsonObject
            {
                ["schema"] = "service-account-usage-v1",
                ["serverName"] = "SYN-APP01",
                ["generatedAt"] = "2026-10-04T10:00:00.0000000+00:00",
                ["durationMs"] = 900,
                ["accounts"] = new JsonArray("SYN\\svc_synapp"),
                ["scanResult"] = "Success",
                ["sources"] = new JsonObject { ["WindowsServices"] = "Success", ["ScheduledTasks"] = "Success", ["Iis"] = "NotInstalled" },
                ["components"] = former ? new JsonArray(task.DeepClone()) : new JsonArray(),
                ["verification"] = new JsonObject
                {
                    ["ExpectedAccount"] = "SYN\\gmsa_synapp$",
                    ["Status"] = former ? "NotConverted" : "Converted",
                    ["RunningAsGmsa"] = new JsonArray(gmsa),
                    ["StillFormerAccount"] = former ? new JsonArray(task) : new JsonArray()
                },
                ["warnings"] = new JsonArray()
            }),
            ["notReached"] = new JsonArray()
        };
    }

    private static JsonObject Component(string type, string name, string identity, string matched) => new()
    {
        ["ComponentType"] = type,
        ["ComponentName"] = name,
        ["Identity"] = identity,
        ["MatchedAccount"] = matched,
        ["State"] = "Running",
        ["Detail"] = null
    };

    internal static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    private static JsonNode Node(JsonNode root, string path) =>
        path.Length == 0 ? root : path.Split('/').Aggregate(root, (node, part) => int.TryParse(part, out int index) ? node[index]! : node[part]!);

    private static UsageScanFileException Rejected(byte[] bytes) => Assert.Throws<UsageScanFileException>(() => UsageScanParser.Parse(bytes, _max, _now));

    private static string Code(byte[] bytes) => Rejected(bytes).Code;

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
