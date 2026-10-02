using System.Text.Json;
using FluentAssertions;

namespace SecureOps.Tests.Unit.Release;

public sealed class ApiReleasePackagingContractTests
{
    [Fact]
    public void PairedDelivery_BindsRunbookAndRuntimeMetadata_WithoutGrantingInstallationReadiness()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "release", "New-PairedTestRelease.ps1"));
        script.Should().Contain("$metadata.readyForInstallation = $false")
            .And.Contain("$metadata.payloadValidated = $true")
            .And.Contain("evidence/validation.json")
            .And.Contain("Export-CompletionGuidance.ps1").And.Contain("operator/docs/post-rc626-continuation-tr.md")
            .And.Contain("operator/docs/integrated-test-activation.md").And.Contain("Paket derleme kaynagi: $sha")
            .And.NotContain("docs/integrated-activation-tr.md")
            .And.Contain("$runtime.runtimeOptions.frameworks")
            .And.Contain("manifestSha256=")
            .And.Contain("-p:ContinuousIntegrationBuild=true")
            .And.Contain("-p:PathMap=$repo=/_/")
            .And.NotContain("@{ Ready=$true;");
        string exporter = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "powershell", "Export-CompletionGuidance.ps1"));
        exporter.Should().Contain("docs/post-rc626-continuation-tr.md")
            .And.Contain("docs/integrated-test-activation.md").And.Contain("docs/continuation-b596058-evidence.md")
            .And.Contain("docs/rc626-mail-source-activation-tr.md")
            .And.Contain("scripts/diagnostics/InUseEvidence/operator-completion-tr.md")
            .And.Contain("Refusing to overwrite existing guidance.").And.Contain("Guidance copy hash mismatch")
            .And.NotContain("docs/integrated-activation-tr.md");
    }

    [Fact]
    public void CombinedDelivery_IncludesNativeWorkerAndExactSchemaWithoutInstallingService()
    {
        string root = FindRepositoryRoot();
        string paired = File.ReadAllText(Path.Combine(root, "scripts", "release", "New-PairedTestRelease.ps1"));
        string managed = File.ReadAllText(Path.Combine(root, "scripts", "release", "New-UiDeploymentPackage.ps1"));
        string worker = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Worker", "Program.cs"));
        string collector = File.ReadAllText(Path.Combine(root, "scripts", "release", "New-InUseEvidencePackage.ps1"));
        collector.Should().Contain("Symbol outside fresh collector staging.").And.Contain("Remove-Item -LiteralPath $symbol.FullName")
            .And.Contain("Test-ApiReleasePayload.ps1");
        // Schema selection moved to the reviewed 024/025 selector; the publisher must consume its result.
        string selector = File.ReadAllText(Path.Combine(root, "scripts", "release", "Get-ReleaseSqlPlan.ps1"));
        selector.Should().Contain("$last = if ($IncludeServiceAccounts) { 26 } else { 24 }")
            .And.Contain("Expected the exact complete 001-$last SQL chain; 025/026 must be explicitly reviewed.");
        paired.Should().Contain("@('Api','Ui','Worker')").And.Contain("requiredSchema=$sqlPlan.RequiredSchema")
            .And.Contain("Get-ReleaseSqlPlan.ps1").And.Contain("-IncludeServiceAccounts:$IncludeServiceAccounts")
            .And.Contain("upgradeFromVerified018='019-024'").And.Contain("database-delta")
            .And.Contain("[switch]$UpgradeFromRc622").And.Contain("apply only reviewed additive 022")
            .And.Contain("[switch]$UpgradeFromRc624").And.Contain("apply only reviewed additive 023")
            .And.Contain("[switch]$UpgradeFromRc626").And.Contain("apply only reviewed additive 024")
            .And.Contain("New-InUseEvidencePackage.ps1").And.Contain("diagnostics/payload.sha256")
            .And.Contain("InUseCompletionEnabled=$false").And.Contain("InUseAspectLookupEnabled=$false")
            .And.Contain("runtimePrepareSchema=$false").And.Contain("-Component Worker")
            .And.Contain("Native Windows Service or console").And.Contain("Symbol outside fresh staging.").And.NotContain("sc.exe");
        managed.Should().Contain("runtimeTargets").And.Contain("Missing Worker runtime asset")
            .And.Contain("Hangfire.SqlServer").And.Contain("Test-ApiReleasePayload.ps1")
            .And.Contain("Microsoft.PowerShell.SDK").And.Contain("Microsoft.PowerShell.Commands.Management")
            .And.Contain("Microsoft.PowerShell.Commands.Utility").And.Contain("Missing Worker PowerShell module manifest");
        worker.Should().Contain("TryAddSecureOpsJobServer").And.Contain("await host.RunAsync()")
            .And.Contain("AddWindowsService").And.Contain("WorkerHosting.CreateBuilder")
            .And.Contain("WorkerHosting.Acquire").And.Contain("args.Contains(\"--diagnostics\"");
    }

    [Fact]
    public void PackagingScript_PreservesRelativePathsAndRunsIntegrityGate()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "release", "New-ApiDeploymentPackage.ps1"));
        string validator = File.ReadAllText(Path.Combine(root, "scripts", "release", "Validate-ApiAdRuntimeDependencies.ps1"));

        script.Should().Contain("$relativePath")
            .And.Contain("CreateEntryFromFile")
            .And.Contain("Validate-ApiAdRuntimeDependencies.ps1")
            .And.NotContain("Compress-Archive -LiteralPath $files.FullName");
        validator.Should().Contain("ZIP has duplicate paths")
            .And.Contain("Publish payload is absent from the SHA256 manifest")
            .And.Contain("ZIP SHA256 does not match publish output")
            .And.Contain("runtimes/win/lib/net10.0/System.DirectoryServices.AccountManagement.dll")
            .And.NotContain("$relativePath:");
    }

    [Fact]
    public void ResolvedApiGraph_ContainsDirectoryServicesGenericAndWindowsRuntimeAssets()
    {
        string root = FindRepositoryRoot();
        string assetsPath = Path.Combine(root, "src", "SecureOps.Api", "obj", "project.assets.json");
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        JsonElement target = document.RootElement.GetProperty("targets").GetProperty("net10.0");
        JsonElement accountManagement = target.EnumerateObject()
            .Single(item => item.Name.StartsWith("System.DirectoryServices.AccountManagement/", StringComparison.Ordinal)).Value;

        accountManagement.GetProperty("runtime").TryGetProperty(
            "lib/net10.0/System.DirectoryServices.AccountManagement.dll",
            out _).Should().BeTrue();
        JsonElement windowsAsset = accountManagement.GetProperty("runtimeTargets")
            .GetProperty("runtimes/win/lib/net10.0/System.DirectoryServices.AccountManagement.dll");
        string? rid = windowsAsset.GetProperty("rid").GetString();
        rid.Should().Be("win");
        accountManagement.GetProperty("dependencies").EnumerateObject().Select(item => item.Name).Should().Contain([
            "System.Configuration.ConfigurationManager",
            "System.DirectoryServices",
            "System.DirectoryServices.Protocols"]);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
