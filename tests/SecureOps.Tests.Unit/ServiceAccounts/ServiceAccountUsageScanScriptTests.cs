using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>Runs only where the collector cannot read Windows sources, so a test never enumerates the developer's own machine.</summary>
public sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "NOT RUN on Windows: running the collector would read this machine's services and tasks (read-only, but machine-dependent).";
        }
    }
}

/// <summary>
/// ADR-0027 operator tooling in a real PowerShell runspace: the self-contained collector stays a verbatim copy of the module
/// functions and prints one contract line; the combine step turns per-server documents and the planned list into an upload
/// that the module's parser accepts, keeps every planned server, and refuses password-like fields. Synthetic names only;
/// no server is contacted.
/// </summary>
[Collection(nameof(ServiceAccountPowerShellCollection))]
public sealed class ServiceAccountUsageScanScriptTests
{
    private const string _begin = "# >>> BEGIN SecureOps.ServiceAccountUsage functions (verbatim)";
    private const string _end = "# <<< END SecureOps.ServiceAccountUsage functions";

    [Fact]
    public void Collector_IsAVerbatimCopyOfTheModuleFunctions()
    {
        string module = Normalize(File.ReadAllText(Path.Combine(ModuleFolder(), "SecureOps.ServiceAccountUsage.psm1")));
        string collector = Normalize(File.ReadAllText(Collector()));
        int from = module.IndexOf("$script:SchemaName", StringComparison.Ordinal);
        int to = module.IndexOf("Export-ModuleMember", StringComparison.Ordinal);
        string copy = collector[(collector.IndexOf(_begin, StringComparison.Ordinal) + _begin.Length)..collector.IndexOf(_end, StringComparison.Ordinal)];

        copy.Trim().Should().Be(module[from..to].Trim(), "the collector and the proposed JEA module must read exactly the same way");
        collector.Should().NotContain("Export-ModuleMember");
    }

    [Fact]
    public void Scripts_OpenNoDefaultEndpointSession_AndTheCollectorWritesNothing()
    {
        string collector = File.ReadAllText(Collector());
        string combine = File.ReadAllText(Combine());

        foreach (string text in new[] { collector, combine })
        {
            text.Should().NotMatchRegex(@"New-PSSession\b").And.NotContain("Enter-PSSession").And.NotContain("Remove-Item").And.NotContain("Stop-Service")
                .And.NotContain("Restart-Service").And.NotContain("Set-Service").And.NotContain("Invoke-Expression");
            text.Should().MatchRegex(@"^[\x00-\x7F]*$", "Windows PowerShell 5.1 reads BOM-less scripts as ANSI");
        }

        collector.Should().NotContain("Invoke-Command").And.NotContain("Set-Content").And.NotContain("Out-File").And.NotContain("WriteAllText")
            .And.NotContain("Export-");
        string[] remoting = [.. combine.Split('\n').Where(l => l.Contains("Invoke-Command -", StringComparison.Ordinal))];
        remoting.Should().ContainSingle().Which.Should().Contain("-ConfigurationName $ConfigurationName", "only the dormant JEA mode connects");
        combine.Should().Contain("[switch]$UseJeaEndpoint");
    }

    [Fact]
    public void UploadExample_FollowsTheContract_AndTheContractIsClosed()
    {
        JsonNode example = JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "contracts", "examples", "service-account-usage-scan-example.json")))!;
        BundleSchema().Evaluate(example).IsValid.Should().BeTrue();

        JsonNode leaked = example.DeepClone();
        leaked["results"]![0]!["components"]![0]!["Password"] = "SYN-NEVER-UPLOADED";
        BundleSchema().Evaluate(leaked).IsValid.Should().BeFalse("no property outside the contract is allowed");
    }

    [NonWindowsFact]
    public void Collector_PrintsOneContractLine_AndAFailedSourceIsNeverNotUsed()
    {
        using PowerShell shell = Shell();
        shell.AddCommand(Collector()).AddParameter("Account", new[] { "SYN\\svc_synapp" });
        PSObject[] output = [.. shell.Invoke()];

        output.Should().ContainSingle();
        string line = output[0].ToString();
        line.Should().NotContain("\n");
        JsonNode document = JsonNode.Parse(line)!;
        UsageSchema().Evaluate(document).IsValid.Should().BeTrue(line);
        document["scanResult"]!.GetValue<string>().Should().Be("Failed", "no Windows source can be read on this host");
        document["components"]!.AsArray().Should().BeEmpty();
        document["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().OnlyContain(w => w.Contains(": ", StringComparison.Ordinal) && !w.Contains('\\'));
    }

    [Fact]
    public void Combine_BuildsAnUploadTheModuleAccepts_AndKeepsEveryPlannedServer()
    {
        using TemporaryFolder folder = new();
        string first = Document("SYN-APP01", match: true);
        string second = Document("SYN-APP02", match: false);
        File.WriteAllText(folder.File("results.jsonl"), first + "\n" + second + "\n", Encoding.Unicode);
        File.WriteAllText(folder.File("planned.txt"), "# synthetic plan\nSYN-APP01\nsyn-app02\nSYN-APP03\nSYN-APP04\n");
        string output = folder.File("scan.json");

        using PowerShell shell = Shell();
        shell.AddCommand(Combine()).AddParameter("CombinePath", folder.File("results.jsonl")).AddParameter("ComputerListPath", folder.File("planned.txt"))
            .AddParameter("UnreachableComputerName", new[] { "SYN-APP04" }).AddParameter("Account", new[] { "SYN\\svc_synapp" })
            .AddParameter("OutputPath", output);
        PSObject summary = shell.Invoke().Single();
        shell.HadErrors.Should().BeFalse(string.Join("; ", shell.Streams.Error));

        summary.Properties["Answered"].Value.Should().Be(2);
        summary.Properties["NotReached"].Value.Should().Be(2);
        byte[] bytes = File.ReadAllBytes(output);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        string text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain(first, "each server document is carried exactly as the collector printed it");
        BundleSchema().Evaluate(JsonNode.Parse(text)).IsValid.Should().BeTrue(text);

        ParsedUsageScan scan = UsageScanParser.Parse(bytes, 4 * 1024 * 1024, DateTimeOffset.UtcNow);
        scan.Servers.Select(s => $"{s.ServerName}:{s.Result}").Should().Equal("SYN-APP01:Success", "syn-app02:Success", "SYN-APP03:NoResult",
            "SYN-APP04:Unreachable");
        scan.Items.Should().ContainSingle().Which.ComponentName.Should().Be("SynSvc");
    }

    [Fact]
    public void Combine_RefusesPasswordLikeFields_ServersOutsideThePlan_AndOtherAccounts()
    {
        using TemporaryFolder folder = new();
        JsonNode leaked = JsonNode.Parse(Document("SYN-APP01", match: true))!;
        leaked["components"]![0]!["Password"] = "SYN-NEVER-UPLOADED";
        File.WriteAllText(folder.File("leaked.json"), leaked.ToJsonString());
        Failure(folder, "leaked.json", "SYN-APP01", "SYN\\svc_synapp").Should().Contain("password-like property");

        File.WriteAllText(folder.File("outside.json"), Document("SYN-OTHER09", match: false));
        Failure(folder, "outside.json", "SYN-APP01", "SYN\\svc_synapp").Should().Contain("not in the planned list");

        File.WriteAllText(folder.File("other.json"), Document("SYN-APP01", match: false));
        Failure(folder, "other.json", "SYN-APP01", "SYN\\svc_other").Should().Contain("searched other accounts");
        File.Exists(folder.File("scan.json")).Should().BeFalse("nothing is written when a document is refused");
    }

    // Review 2026-10-05: the combine step checked property names only; a value carrying a password assignment left the
    // workstation and was refused only by the module. It now applies the module's folded value rule before writing.
    [Theory]
    [InlineData("C:\\syn\\run.exe /password:SYN-NEVER-UPLOADED")]
    [InlineData("syn.exe \"Password\":\"SYN-NEVER-UPLOADED\"")]
    [InlineData("syn.exe token = SYN-NEVER-UPLOADED")]
    [InlineData("syn.exe \uFF30\uFF41\uFF53\uFF53\uFF57\uFF4F\uFF52\uFF44=SYN-NEVER-UPLOADED")]
    [InlineData("syn.exe pass\u200Bword=SYN-NEVER-UPLOADED")]
    [InlineData("syn.exe \u015Eifre=SYN-NEVER-UPLOADED")]
    public void Combine_RefusesAPasswordAssignmentInAValue_AsTheModuleDoes(string detail)
    {
        using TemporaryFolder folder = new();
        string document = Detail(Document("SYN-APP01", match: true), detail);
        File.WriteAllText(folder.File("leaked.json"), document);

        Failure(folder, "leaked.json", "SYN-APP01", "SYN\\svc_synapp").Should().Contain("password assignment").And.NotContain("SYN-NEVER-UPLOADED");
        File.Exists(folder.File("scan.json")).Should().BeFalse("nothing is written when a document is refused");
        FluentActions.Invoking(() => UsageScanParser.Parse(Encoding.UTF8.GetBytes(Bundle(document)), 4 * 1024 * 1024, DateTimeOffset.UtcNow))
            .Should().Throw<UsageScanFileException>().Which.Code.Should().Be(UsageScanFileCodes.SecretValue, "the script and the module refuse the same value");
    }

    [Fact]
    public void Combine_AcceptsOrdinaryWordsNearTheSecretWords_AndRefusesOverlongText()
    {
        using TemporaryFolder folder = new();
        File.WriteAllText(folder.File("ordinary.json"), Detail(Document("SYN-APP01", match: true), "LogonType=Password; Password Expiry Notification"));
        using (PowerShell shell = Shell())
        {
            shell.AddCommand(Combine()).AddParameter("CombinePath", folder.File("ordinary.json")).AddParameter("ComputerName", new[] { "SYN-APP01" })
                .AddParameter("Account", new[] { "SYN\\svc_synapp" }).AddParameter("OutputPath", folder.File("scan.json"));
            shell.Invoke();
            shell.HadErrors.Should().BeFalse(string.Join("; ", shell.Streams.Error));
        }

        UsageScanParser.Parse(File.ReadAllBytes(folder.File("scan.json")), 4 * 1024 * 1024, DateTimeOffset.UtcNow).Items.Should().ContainSingle();

        File.WriteAllText(folder.File("long.json"), Detail(Document("SYN-APP01", match: true), new string('a', 4097)));
        Failure(folder, "long.json", "SYN-APP01", "SYN\\svc_synapp").Should().Contain("longer than 4096");
    }

    /// <summary>The document with the first component's Detail replaced.</summary>
    private static string Detail(string document, string detail)
    {
        JsonNode node = JsonNode.Parse(document)!;
        node["components"]![0]!["Detail"] = detail;
        return node.ToJsonString();
    }

    /// <summary>A minimal upload around one document, only to ask the module's parser about the same value.</summary>
    private static string Bundle(string document)
    {
        string server = JsonNode.Parse(document)!["serverName"]!.GetValue<string>();
        return $$"""{"schema":"service-account-usage-scan-v1","generatedAt":"{{DateTimeOffset.UtcNow:O}}","tool":"Combined","accounts":["SYN\\svc_synapp"],"expectedAccount":null,"plannedServers":["{{server}}"],"notReached":[],"results":[{{document}}]}""";
    }

    private static string Failure(TemporaryFolder folder, string file, string planned, string account)
    {
        using PowerShell shell = Shell();
        shell.AddCommand(Combine()).AddParameter("CombinePath", folder.File(file)).AddParameter("ComputerName", new[] { planned })
            .AddParameter("Account", new[] { account }).AddParameter("OutputPath", folder.File("scan.json"));
        Action run = () => shell.Invoke();
        return run.Should().Throw<RuntimeException>().Which.Message;
    }

    /// <summary>A real module document (collectors replaced by synthetic data), as one JSON line with the server name set.</summary>
    private static string Document(string server, bool match)
    {
        using PowerShell shell = Shell();
        shell.AddCommand("Import-Module").AddParameter("Name", Path.Combine(ModuleFolder(), "SecureOps.ServiceAccountUsage.psd1")).AddParameter("Force");
        shell.Invoke();
        shell.Commands.Clear();
        string identity = match ? "SYN\\svc_synapp" : "LocalSystem";
        shell.AddScript("& (Get-Module SecureOps.ServiceAccountUsage) ([scriptblock]::Create($args[0]))").AddArgument($$"""
            function script:Get-SoServiceIdentity { [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = 'SynSvc'; Identity = '{{identity}}'; State = 'Running'; Detail = $null } }
            function script:Get-SoTaskIdentity { }
            function script:Get-SoIisConfiguration { $null }
            """);
        shell.Invoke();
        shell.Commands.Clear();
        shell.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { "SYN\\svc_synapp" }).AddCommand("ConvertTo-Json")
            .AddParameter("Depth", 8).AddParameter("Compress");
        JsonNode document = JsonNode.Parse(shell.Invoke().Single().ToString())!;
        document["serverName"] = server;
        return document.ToJsonString();
    }

    private static PowerShell Shell()
    {
        var state = InitialSessionState.CreateDefault2();
        if (OperatingSystem.IsWindows())
        {
            state.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        }

        return PowerShell.Create(state);
    }

    private static JsonSchema UsageSchema() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(Root(), "contracts", "schemas", "service-account-usage.schema.json")));

    private static readonly Lazy<JsonSchema> _bundleSchema = new(() =>
    {
        // The upload schema refers to the per-server schema by its $id.
        SchemaRegistry.Global.Register(UsageSchema());
        return JsonSchema.FromText(File.ReadAllText(Path.Combine(Root(), "contracts", "schemas", "service-account-usage-scan.schema.json")));
    });

    private static JsonSchema BundleSchema() => _bundleSchema.Value;

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Collector() => Path.Combine(Root(), "scripts", "powershell", "Get-ServiceAccountUsage.ps1");

    private static string Combine() => Path.Combine(Root(), "scripts", "powershell", "Invoke-ServiceAccountUsageScan.ps1");

    private static string ModuleFolder() => Path.Combine(Root(), "scripts", "jea", "proposed", "SecureOps.ServiceAccountUsage");

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("sa-usage-scan-").FullName;

        public string File(string name) => Path.Combine(_path, name);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
