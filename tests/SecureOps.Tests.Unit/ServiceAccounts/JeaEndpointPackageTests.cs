using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using SecureOps.Worker.Jea;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// The released JEA endpoint package (TARAMA-C B5) and the install scripts: configuration files are read as data with the repository's
/// PowerShell SDK, scripts are parsed (never run). Nothing is registered, installed or contacted.
/// </summary>
[Collection(nameof(ServiceAccountPowerShellCollection))]
public sealed class JeaEndpointPackageTests
{
    private static readonly string[] _writeCommands =
        ["Stop-Service", "Start-Service", "Restart-Service", "Set-Service", "Restart-WebAppPool", "Set-ItemProperty", "Set-WebConfiguration",
         "Set-ADAccountPassword", "Set-ScheduledTask", "schtasks", "secedit", "Invoke-Expression", "Invoke-Command", "New-PSSession", "Enter-PSSession",
         "Enable-PSRemoting", "Set-Item", "Add-LocalGroupMember", "Add-ADGroupMember", "Restart-Computer"];

    [Fact]
    public void RoleCapability_ExposesExactlyTheCatalogFunction_AndNothingElse()
    {
        Hashtable role = Data(Path.Combine(Module(), "RoleCapabilities", "SecureOpsServiceAccountUsage.psrc"));

        Strings(role["VisibleFunctions"]).Should().Equal(JeaEndpointCatalog.ServiceAccountUsage.FunctionName);
        foreach (string key in new[] { "VisibleCmdlets", "VisibleProviders", "VisibleExternalCommands", "VisibleAliases" })
        {
            Strings(role[key]).Should().BeEmpty($"{key} stays empty: the raw cmdlets are reachable only inside the function body");
        }

        Strings(role["ModulesToImport"]).Should().Equal("SecureOps.ServiceAccountUsage");
        role.Keys.Cast<string>().Should().NotContain(["FunctionDefinitions", "ScriptsToProcess", "AliasDefinitions", "VariableDefinitions", "EnvironmentVariables"]);
    }

    [Fact]
    public void SessionConfiguration_IsRestrictedNoLanguage_WithOneRoleForThePlaceholderReadGroup()
    {
        Hashtable session = Data(Path.Combine(Module(), "SecureOpsServiceAccountUsage.pssc"));

        session["SessionType"].Should().Be("RestrictedRemoteServer");
        session["LanguageMode"].Should().Be("NoLanguage");
        session["RunAsVirtualAccount"].Should().Be(true);
        session["TranscriptDirectory"].Should().Be("C:\\ProgramData\\SecureOps\\JEA\\Transcripts");
        var roles = (Hashtable)session["RoleDefinitions"]!;
        roles.Keys.Cast<string>().Should().Equal("CONTOSO\\SecureOps-SaRead");
        ((Hashtable)roles["CONTOSO\\SecureOps-SaRead"]!)["RoleCapabilities"].Should().Be("SecureOpsServiceAccountUsage");
        session.Keys.Cast<string>().Should().NotContain(["RunAsVirtualAccountGroups", "GroupManagedServiceAccount", "ScriptsToProcess", "VisibleCmdlets",
            "VisibleFunctions", "FunctionDefinitions", "MountUserDrive"]);
    }

    [Fact]
    public void Versions_AndNames_AgreeAcrossManifestCatalogAndInstaller()
    {
        Hashtable manifest = Data(Path.Combine(Module(), "SecureOps.ServiceAccountUsage.psd1"));
        Version.Parse((string)manifest["ModuleVersion"]!).Should().BeGreaterThanOrEqualTo(new Version(1, 0, 0));
        manifest["PowerShellVersion"].Should().Be("5.1");

        string install = File.ReadAllText(Script("jea", "Install-SecureOpsServiceAccountUsageEndpoint.ps1"));
        string uninstall = File.ReadAllText(Script("jea", "Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1"));
        string configurationName = JeaEndpointCatalog.ServiceAccountUsage.ConfigurationName;
        install.Should().Contain($"$configurationName = '{configurationName}'");
        uninstall.Should().Contain($"$configurationName = '{configurationName}'");
        File.Exists(Path.Combine(Module(), "RoleCapabilities", "SecureOpsServiceAccountUsage.psrc")).Should().BeTrue();
    }

    [Fact]
    public void AsJson_ReturnsOneJsonTextOfTheContract_WithoutPasswords()
    {
        var state = InitialSessionState.CreateDefault2();
        if (OperatingSystem.IsWindows())
        {
            state.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        }

        using var shell = PowerShell.Create(state);
        shell.AddCommand("Import-Module").AddParameter("Name", Path.Combine(Module(), "SecureOps.ServiceAccountUsage.psd1")).AddParameter("Force");
        shell.Invoke();
        shell.Commands.Clear();
        shell.AddScript("& (Get-Module SecureOps.ServiceAccountUsage) ([scriptblock]::Create($args[0]))").AddArgument("""
            function script:Get-SoServiceIdentity { [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = 'SynSvc'; Identity = 'SYN\svc_a'; State = 'Running'; Detail = $null } }
            function script:Get-SoTaskIdentity { }
            function script:Get-SoIisConfiguration { [xml]'<configuration><system.applicationHost><applicationPools><add name="SynPool"><processModel identityType="SpecificUser" userName="SYN\svc_a" password="SYN-NEVER-RETURNED" /></add></applicationPools></system.applicationHost></configuration>' }
            """);
        shell.Invoke();
        shell.HadErrors.Should().BeFalse(string.Join("; ", shell.Streams.Error));
        shell.Commands.Clear();

        shell.AddCommand("Get-SecureOpsAccountUsage").AddParameter("Account", new[] { "SYN\\svc_a" }).AddParameter("ExpectedAccount", "SYN\\gmsa_a$")
            .AddParameter("AsJson");
        PSObject output = shell.Invoke().Should().ContainSingle().Subject;

        string text = output.BaseObject.Should().BeOfType<string>().Subject;
        text.Should().NotContain("SYN-NEVER-RETURNED").And.NotContain("\n");
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(JeaRunnerRoot(), "contracts", "schemas", "service-account-usage.schema.json")));
        JsonNode document = JsonNode.Parse(text)!;
        schema.Evaluate(document).IsValid.Should().BeTrue(text);
        document["verification"]!["StillFormerAccount"]!.AsArray().Should().HaveCount(2, "nested objects survive as JSON, unlike remoting's shallow serialization");
        JeaServiceAccountUsageCollector.Document("syn-app-01", text, 512 * 1024).Should().NotBeNull();
    }

    [Fact]
    public void EndpointModule_AndRole_ContainNoWriteCommand()
    {
        foreach (string file in new[] { Path.Combine(Module(), "SecureOps.ServiceAccountUsage.psm1"), Path.Combine(Module(), "RoleCapabilities", "SecureOpsServiceAccountUsage.psrc") })
        {
            string text = File.ReadAllText(file);
            foreach (string command in _writeCommands.Concat(["Remove-Item", "New-Item", "Copy-Item", "Register-PSSessionConfiguration"]))
            {
                text.Should().NotContain(command, Path.GetFileName(file));
            }
        }
    }

    public static TheoryData<string, string> InstallScripts => new()
    {
        { "jea", "Install-SecureOpsServiceAccountUsageEndpoint.ps1" },
        { "jea", "Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1" },
        { "worker", "Install-SecureOpsWorkerService.ps1" },
        { "worker", "Uninstall-SecureOpsWorkerService.ps1" }
    };

    [Theory]
    [MemberData(nameof(InstallScripts))]
    public void InstallScripts_ParseCleanly_RequireAdministrator_SupportWhatIf_AndCarryNoSecretOrRemoting(string folder, string name)
    {
        string path = Script(folder, name);
        ScriptBlockAst ast = Parser.ParseFile(path, out _, out ParseError[] errors);

        errors.Should().BeEmpty();
        ast.ScriptRequirements.IsElevationRequired.Should().BeTrue("only an administrator of the server runs it, never the product");
        ast.ParamBlock!.Attributes.Should().Contain(a => a.TypeName.Name == "CmdletBinding" && a.Extent.Text.Contains("SupportsShouldProcess"));
        ast.ParamBlock.Parameters.Select(p => p.Name.VariablePath.UserPath).Should()
            .NotContain(p => p.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Contains("Credential", StringComparison.OrdinalIgnoreCase));
        string text = File.ReadAllText(path);
        foreach (string word in new[] { "Get-Credential", "ConvertTo-SecureString", "-Credential", "password=", "Invoke-Command", "New-PSSession",
            "Enter-PSSession", "Invoke-Expression", "Enable-PSRemoting", "Set-Item", "WSMan:", "Start-Service", "Restart-Service", "Set-Service",
            "Add-LocalGroupMember", "Add-ADGroupMember", "Set-Acl", "icacls" })
        {
            System.Text.RegularExpressions.Regex.IsMatch(text, System.Text.RegularExpressions.Regex.Escape(word) + "(?![A-Za-z])").Should()
                .BeFalse($"{name} must not use {word}: no secrets, remote sessions, service start or rights changes");
        }
    }

    [Fact]
    public void EndpointInstaller_UsesOnlyTheExpectedCommands_AndRefusesThePlaceholderGroup()
    {
        string path = Script("jea", "Install-SecureOpsServiceAccountUsageEndpoint.ps1");

        Commands(path).Should().BeSubsetOf(["Microsoft.PowerShell.Management\\Join-Path", "Microsoft.PowerShell.Utility\\Import-PowerShellDataFile",
            "Microsoft.PowerShell.Core\\Get-PSSessionConfiguration", "Microsoft.PowerShell.Management\\Test-Path", "Microsoft.PowerShell.Management\\New-Item",
            "Microsoft.PowerShell.Management\\Copy-Item", "Microsoft.PowerShell.Core\\New-PSSessionConfigurationFile",
            "Microsoft.PowerShell.Core\\Test-PSSessionConfigurationFile", "Microsoft.PowerShell.Core\\Register-PSSessionConfiguration"]);
        string text = File.ReadAllText(path);
        text.Should().Contain("$placeholderGroup = 'CONTOSO\\SecureOps-SaRead'").And.Contain("if ($ReadGroup -eq $placeholderGroup) { throw");
        text.Should().Contain("-LanguageMode $template.LanguageMode").And.Contain("-SessionType $template.SessionType").And.Contain("-RunAsVirtualAccount");
        Commands(Script("jea", "Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1")).Should().BeSubsetOf(["Microsoft.PowerShell.Management\\Join-Path",
            "Microsoft.PowerShell.Core\\Get-PSSessionConfiguration", "Microsoft.PowerShell.Core\\Unregister-PSSessionConfiguration",
            "Microsoft.PowerShell.Management\\Test-Path", "Microsoft.PowerShell.Management\\Remove-Item"]);
    }

    [Fact]
    public void WorkerServiceInstaller_UsesAGmsaWithoutPassword_AndNeverStartsTheService()
    {
        string path = Script("worker", "Install-SecureOpsWorkerService.ps1");
        string text = File.ReadAllText(path);

        text.Should().Contain("[string]$ServiceAccount = 'CONTOSO\\gmsa-placeholder$'")
            .And.Contain("if ($ServiceAccount -eq 'CONTOSO\\gmsa-placeholder$') { throw")
            .And.Contain("config $serviceName obj= $ServiceAccount |")
            .And.Contain("-StartupType Manual")
            .And.Contain("[Diagnostics.EventLog]::CreateEventSource($eventSource, 'Application')")
            .And.Contain($"$eventSource = '{SecureOps.Worker.WorkerEventLog.SourceName}'");
        Commands(path).Should().BeSubsetOf(["Microsoft.PowerShell.Management\\Test-Path", "Microsoft.PowerShell.Management\\Get-Service",
            "Microsoft.PowerShell.Core\\Get-Module", "ActiveDirectory\\Test-ADServiceAccount", "Microsoft.PowerShell.Management\\New-Service",
            "Microsoft.PowerShell.Core\\Out-Null", "\"$env:SystemRoot\\System32\\sc.exe\""]);
        text.Should().Contain("$serviceName = 'SecureOps.Worker'");
        Commands(Script("worker", "Uninstall-SecureOpsWorkerService.ps1")).Should().BeSubsetOf(["Microsoft.PowerShell.Management\\Get-Service",
            "Microsoft.PowerShell.Management\\Stop-Service", "Microsoft.PowerShell.Core\\Out-Null", "\"$env:SystemRoot\\System32\\sc.exe\""]);
    }

    private static string[] Commands(string path)
    {
        ScriptBlockAst ast = Parser.ParseFile(path, out _, out _);
        return [.. ast.FindAll(n => n is CommandAst, true).Cast<CommandAst>().Select(c => c.CommandElements[0].Extent.Text).Distinct()
            .Where(c => c != "Set-StrictMode")];
    }

    private static Hashtable Data(string path)
    {
        // Read as data only (a restricted-language hashtable literal), the way PowerShell reads these files.
        ScriptBlockAst ast = Parser.ParseFile(path, out _, out ParseError[] errors);
        errors.Should().BeEmpty();
        return (Hashtable)ast.Find(n => n is HashtableAst, false)!.SafeGetValue();
    }

    private static string[] Strings(object? value) => value switch
    {
        null => [],
        object[] items => [.. items.Select(i => (string)i)],
        string one => [one],
        _ => throw new InvalidOperationException("Unexpected value type.")
    };

    private static string Module() => Path.Combine(JeaRunnerRoot(), "scripts", "jea", "SecureOps.ServiceAccountUsage");

    private static string Script(string folder, string name) => Path.Combine(JeaRunnerRoot(), "scripts", folder, name);

    private static string JeaRunnerRoot() => SecureOps.Tests.Unit.Worker.JeaRunnerTests.Root();
}
