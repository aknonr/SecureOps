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
            .And.Contain(".Replace('{{RELEASE_NAME}}', $ReleaseName).Replace('{{BUILD_SHA}}', $sha)")
            .And.Contain("$runtime.runtimeOptions.frameworks")
            .And.Contain("manifestSha256=")
            .And.Contain("-p:ContinuousIntegrationBuild=true")
            .And.Contain("-p:PathMap=$repo=/_/")
            .And.NotContain("@{ Ready=$true;");
    }

    [Fact]
    public void CombinedDelivery_IncludesConsoleWorkerAndExactSchemaWithoutServiceInstallation()
    {
        string root = FindRepositoryRoot();
        string paired = File.ReadAllText(Path.Combine(root, "scripts", "release", "New-PairedTestRelease.ps1"));
        string managed = File.ReadAllText(Path.Combine(root, "scripts", "release", "New-UiDeploymentPackage.ps1"));
        string worker = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Worker", "Program.cs"));
        paired.Should().Contain("@('Api','Ui','Worker')").And.Contain("requiredSchema='001-018'")
            .And.Contain("Expected the exact complete 001-018 SQL chain.")
            .And.Contain("runtimePrepareSchema=$false").And.Contain("-Component Worker")
            .And.Contain("Foreground console only").And.NotContain("sc.exe");
        managed.Should().Contain("runtimeTargets").And.Contain("Missing Worker runtime asset")
            .And.Contain("Hangfire.SqlServer").And.Contain("Test-ApiReleasePayload.ps1");
        worker.Should().Contain("TryAddSecureOpsJobServer").And.Contain("await host.RunAsync()")
            .And.NotContain("AddWindowsService").And.NotContain("UseWindowsService");
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
            .And.Contain("runtimes/win/lib/net8.0/System.DirectoryServices.AccountManagement.dll")
            .And.NotContain("$relativePath:");
    }

    [Fact]
    public void ResolvedApiGraph_ContainsDirectoryServicesGenericAndWindowsRuntimeAssets()
    {
        string root = FindRepositoryRoot();
        string assetsPath = Path.Combine(root, "src", "SecureOps.Api", "obj", "project.assets.json");
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        JsonElement target = document.RootElement.GetProperty("targets").GetProperty("net8.0");
        JsonElement accountManagement = target.GetProperty("System.DirectoryServices.AccountManagement/8.0.1");

        accountManagement.GetProperty("runtime").TryGetProperty(
            "lib/net8.0/System.DirectoryServices.AccountManagement.dll",
            out _).Should().BeTrue();
        JsonElement windowsAsset = accountManagement.GetProperty("runtimeTargets")
            .GetProperty("runtimes/win/lib/net8.0/System.DirectoryServices.AccountManagement.dll");
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
