using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// The proposed read-only discovery module (ADR-0024), loaded into a real PowerShell runspace through the repository's
/// PowerShell SDK. Collectors are replaced inside the module scope with synthetic data, so no Windows server, service,
/// IIS or directory is touched. Synthetic names only.
/// </summary>
public sealed class ServiceAccountUsageModuleTests
{
    private const string _secret = "SYN-NEVER-RETURNED-9f2c";

    private static readonly string _config = $"""
        <configuration>
          <system.applicationHost>
            <applicationPools>
              <add name="SynPoolUser"><processModel identityType="SpecificUser" userName="SYN\svc_synapp" password="{_secret}" /></add>
              <add name="SynPoolGmsa"><processModel identityType="SpecificUser" userName="SYN\gmsa_synapp$" /></add>
              <add name="SynPoolBuiltIn"><processModel identityType="ApplicationPoolIdentity" /></add>
              <add name="SynPoolNoModel" />
            </applicationPools>
            <sites>
              <site name="SynSite">
                <application path="/">
                  <virtualDirectory path="/" physicalPath="D:\syn\site" userName="SYN\svc_synapp" password="{_secret}" />
                  <virtualDirectory path="/files" physicalPath="\\syn-fs\share" userName="svc_synapp@syn.example" password="{_secret}" />
                </application>
                <application path="/api" applicationPool="SynPoolUser">
                  <virtualDirectory path="/" physicalPath="D:\syn\api" userName="OTHER\svc_synapp" password="{_secret}" />
                </application>
                <application path="/plain">
                  <virtualDirectory path="/" physicalPath="D:\syn\plain" />
                </application>
              </site>
            </sites>
          </system.applicationHost>
        </configuration>
        """;

    [Theory]
    [InlineData("SYN\\svc_synapp", "SYN\\svc_synapp", true)]
    [InlineData("syn\\SVC_SYNAPP", "SYN\\svc_synapp", true)]
    [InlineData("svc_synapp", "SYN\\svc_synapp", true)]
    [InlineData("svc_synapp@syn.example", "SYN\\svc_synapp", true)]
    [InlineData("OTHER\\svc_synapp", "SYN\\svc_synapp", false)]
    [InlineData(".\\svc_synapp", "SYN\\svc_synapp", false)]
    [InlineData("SYN\\svc_synapp$", "SYN\\svc_synapp", false)]
    [InlineData("LocalSystem", "LocalSystem", false)]
    [InlineData("NT AUTHORITY\\NetworkService", "NetworkService", false)]
    [InlineData("", "SYN\\svc_synapp", false)]
    public void AccountMatching_FollowsWindowsIdentityForms(string configured, string wanted, bool expected)
    {
        using PowerShell shell = Shell();
        shell.AddCommand("Test-SoAccountMatch").AddParameter("Configured", configured).AddParameter("Wanted", wanted);
        ((bool)shell.Invoke().Single().BaseObject).Should().Be(expected);
    }

    [Fact]
    public void IisIdentities_AreReadWithoutEverReadingPasswords()
    {
        using PowerShell shell = Shell();
        shell.AddCommand("Read-SoIisIdentity").AddParameter("Configuration", _config);
        PSObject[] items = [.. shell.Invoke()];

        string json = Json(items);
        json.Should().NotContain(_secret, "password attributes are never selected or returned");
        items.Select(i => $"{i.Properties["ComponentType"].Value}|{i.Properties["ComponentName"].Value}|{i.Properties["Identity"].Value}")
            .Should().BeEquivalentTo(
            [
                "IisAppPool|SynPoolUser|SYN\\svc_synapp",
                "IisAppPool|SynPoolGmsa|SYN\\gmsa_synapp$",
                "IisSite|SynSite|SYN\\svc_synapp",
                "IisVirtualDirectory|SynSite/files|svc_synapp@syn.example",
                "IisApplication|SynSite/api|OTHER\\svc_synapp"
            ]);
        items.Single(i => (string)i.Properties["ComponentName"].Value == "SynSite/files").Properties["Detail"].Value.Should().Be("\\\\syn-fs\\share");
    }

    [Fact]
    public void Scan_ReportsMatchesPerSource_AndAFailedSourceMakesItPartialWithoutLeakingTheMessage()
    {
        using PowerShell shell = Shell();
        Stub(shell, """
            function script:Get-SoServiceIdentity {
                [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = 'SynSvc'; Identity = 'SYN\svc_synapp'; State = 'Running'; Detail = $null }
                [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = 'Spooler'; Identity = 'LocalSystem'; State = 'Running'; Detail = $null }
            }
            function script:Get-SoTaskIdentity { throw [System.UnauthorizedAccessException]::new('C:\secret\path SYN\person') }
            """);
        Stub(shell, "function script:Get-SoIisConfiguration { [xml]@'\n" + _config + "\n'@ }");

        shell.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { "SYN\\svc_synapp" });
        PSObject result = shell.Invoke().Single();

        result.Properties["schema"].Value.Should().Be("service-account-usage-v1");
        result.Properties["scanResult"].Value.Should().Be("Partial");
        var sources = (PSObject)result.Properties["sources"].Value;
        (sources.Properties["WindowsServices"].Value, sources.Properties["ScheduledTasks"].Value, sources.Properties["Iis"].Value)
            .Should().Be(("Success", "Failed", "Success"));
        string json = Json([result]);
        json.Should().NotContain(_secret).And.NotContain("secret\\\\path").And.NotContain("person");
        json.Should().Contain("ScheduledTasks: UnauthorizedAccessException");
        Components(result).Should().BeEquivalentTo(["WindowsService|SynSvc", "IisAppPool|SynPoolUser", "IisSite|SynSite", "IisVirtualDirectory|SynSite/files"],
            "OTHER\\svc_synapp, LocalSystem and the gMSA pool are not the wanted account");
    }

    [Fact]
    public void PostConversionCheck_SaysConvertedOnlyWhenNoFormerAccountIsLeft()
    {
        using PowerShell before = Shell();
        Stub(before, """
            function script:Get-SoServiceIdentity { [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = 'SynSvc'; Identity = 'SYN\gmsa_synapp$'; State = 'Running'; Detail = $null } }
            function script:Get-SoTaskIdentity { [pscustomobject]@{ ComponentType = 'ScheduledTask'; ComponentName = '\SynTask'; Identity = 'SYN\svc_synapp'; State = 'Ready'; Detail = 'LogonType=Password' } }
            function script:Get-SoIisConfiguration { $null }
            """);
        before.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { "SYN\\svc_synapp" }).AddParameter("ExpectedAccount", "SYN\\gmsa_synapp$");
        PSObject partly = before.Invoke().Single();
        var verification = (PSObject)partly.Properties["verification"].Value;
        verification.Properties["Status"].Value.Should().Be("NotConverted", "the scheduled task still runs as the former account");
        ((PSObject)partly.Properties["sources"].Value).Properties["Iis"].Value.Should().Be("NotInstalled");

        using PowerShell after = Shell();
        Stub(after, """
            function script:Get-SoServiceIdentity { [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = 'SynSvc'; Identity = 'SYN\gmsa_synapp$'; State = 'Running'; Detail = $null } }
            function script:Get-SoTaskIdentity { [pscustomobject]@{ ComponentType = 'ScheduledTask'; ComponentName = '\SynTask'; Identity = 'SYN\gmsa_synapp$'; State = 'Ready'; Detail = 'LogonType=Password' } }
            function script:Get-SoIisConfiguration { $null }
            """);
        after.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { "SYN\\svc_synapp" }).AddParameter("ExpectedAccount", "SYN\\gmsa_synapp$");
        PSObject done = after.Invoke().Single();
        ((PSObject)done.Properties["verification"].Value).Properties["Status"].Value.Should().Be("Converted");
        Components(done).Should().BeEmpty("nothing runs as the former account any more");
    }

    [Theory]
    [InlineData("svc*")]
    [InlineData("SYN\\svc synapp")]
    [InlineData("SYN\\svc';Stop-Service x;'")]
    [InlineData("a\\b\\c")]
    public void Accounts_RejectWildcardsAndInjection(string account)
    {
        using PowerShell shell = Shell();
        shell.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { account });
        Action run = () => shell.Invoke();
        run.Should().Throw<ParameterBindingException>();
    }

    [Fact]
    public void Limits_AtMostTwentyAccounts_AndTheExpectedAccountMustBeAGmsa()
    {
        using PowerShell many = Shell();
        many.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", Enumerable.Range(1, 21).Select(i => $"SYN\\svc{i}").ToArray());
        ((Action)(() => many.Invoke())).Should().Throw<ParameterBindingException>();

        using PowerShell notGmsa = Shell();
        notGmsa.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { "SYN\\svc_synapp" }).AddParameter("ExpectedAccount", "SYN\\svc_new");
        ((Action)(() => notGmsa.Invoke())).Should().Throw<ParameterBindingException>();
    }

    [Fact]
    public void ModuleRoleAndOperatorScript_ContainNoWriteCommands_AndTheRoleExposesOneFunction()
    {
        string module = File.ReadAllText(Path.Combine(ModuleFolder(), "SecureOps.ServiceAccountUsage.psm1"));
        string role = File.ReadAllText(Path.Combine(ModuleFolder(), "RoleCapabilities", "SecureOpsServiceAccountUsage.psrc"));
        string scan = File.ReadAllText(Path.Combine(Root(), "scripts", "powershell", "Invoke-ServiceAccountUsageScan.ps1"));
        string[] forbidden = ["Stop-Service", "Start-Service", "Restart-Service", "Set-Service", "Remove-Item", "Restart-WebAppPool", "Set-ItemProperty",
            "Set-WebConfiguration", "Set-ADAccountPassword", "Set-ScheduledTask", "schtasks", "secedit", "Invoke-Expression", "New-Item", ".Change(",
            "COMAdmin", "Translate("];
        foreach (string text in new[] { module, scan })
        {
            foreach (string command in forbidden)
            {
                text.Should().NotContain(command);
            }
        }

        module.Should().NotContain("GetAttribute('password')").And.NotContain("\"password\"");
        role.Should().Contain("VisibleFunctions = @('Get-SecureOpsAccountUsage')").And.Contain("VisibleCmdlets   = @()")
            .And.Contain("VisibleProviders = @()").And.Contain("VisibleExternalCommands = @()");
    }

    [Fact]
    public void Output_MatchesTheContract_AndTheContractRejectsAPasswordField()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(Root(), "contracts", "schemas", "service-account-usage.schema.json")));
        string example = File.ReadAllText(Path.Combine(Root(), "contracts", "examples", "service-account-usage-example.json"));
        schema.Evaluate(JsonNode.Parse(example)).IsValid.Should().BeTrue("the checked-in example follows the contract");

        using PowerShell shell = Shell();
        Stub(shell, """
            function script:Get-SoServiceIdentity { [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = 'SynSvc'; Identity = 'SYN\svc_synapp'; State = 'Running'; Detail = $null } }
            function script:Get-SoTaskIdentity { }
            function script:Get-SoIisConfiguration { $null }
            """);
        shell.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { "SYN\\svc_synapp" }).AddParameter("ExpectedAccount", "SYN\\gmsa_synapp$");
        string output = JsonOne(shell.Invoke().Single());
        schema.Evaluate(JsonNode.Parse(output)).IsValid.Should().BeTrue("the module emits the contract shape: " + output);

        JsonNode leaked = JsonNode.Parse(example)!;
        leaked["components"]![0]!["Password"] = _secret;
        schema.Evaluate(leaked).IsValid.Should().BeFalse("a password field is never allowed in a component");
    }

    private static PowerShell Shell()
    {
        var state = InitialSessionState.CreateDefault2();
        if (OperatingSystem.IsWindows())
        {
            state.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        }

        var shell = PowerShell.Create(state);
        shell.AddCommand("Import-Module").AddParameter("Name", Path.Combine(ModuleFolder(), "SecureOps.ServiceAccountUsage.psd1")).AddParameter("Force");
        shell.Invoke();
        shell.HadErrors.Should().BeFalse(string.Join("; ", shell.Streams.Error));
        shell.Commands.Clear();
        return shell;
    }

    private static void Stub(PowerShell shell, string definitions)
    {
        shell.AddScript("& (Get-Module SecureOps.ServiceAccountUsage) ([scriptblock]::Create($args[0]))").AddArgument(definitions);
        shell.Invoke();
        shell.HadErrors.Should().BeFalse(string.Join("; ", shell.Streams.Error));
        shell.Commands.Clear();
    }

    private static string[] Components(PSObject result) =>
        [.. (result.Properties["components"].Value is PSObject wrapped ? (object[])wrapped.BaseObject : (object[])result.Properties["components"].Value).Cast<PSObject>()
            .Select(c => $"{c.Properties["ComponentType"].Value}|{c.Properties["ComponentName"].Value}")];

    private static string Json(IEnumerable<PSObject> items)
    {
        using var shell = PowerShell.Create();
        shell.AddCommand("ConvertTo-Json").AddParameter("InputObject", items.ToArray()).AddParameter("Depth", 8);
        return string.Concat(shell.Invoke().Select(o => o.ToString()));
    }

    private static string JsonOne(PSObject item)
    {
        using var shell = PowerShell.Create();
        shell.AddCommand("ConvertTo-Json").AddParameter("InputObject", item).AddParameter("Depth", 8);
        return string.Concat(shell.Invoke().Select(o => o.ToString()));
    }

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
}
