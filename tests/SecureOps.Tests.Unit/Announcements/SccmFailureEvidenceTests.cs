using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using FluentAssertions;
using SecureOps.Infrastructure.Announcements.Sources;
using Xunit;

namespace SecureOps.Tests.Unit.Announcements;

public sealed class SccmFailureEvidenceTests
{
    [Theory]
    [InlineData("ImportModule", "Import-Module")]
    [InlineData("CreateSiteDrive", "New-PSDrive")]
    [InlineData("SelectSiteDrive", "Set-Location")]
    [InlineData("ReadCollection", "Get-CMDevice | Select-Object")]
    public void Evidence_PreservesStageAndCategoryWithoutRawErrorPayload(string stage, string command)
    {
        var exception = new InvalidOperationException("password=DO-NOT-EXPOSE", new IOException("PRIVATE-SOURCE-BODY"));
        var record = new ErrorRecord(exception, "private-target=DO-NOT-EXPOSE", ErrorCategory.PermissionDenied,
            new { Authorization = "DO-NOT-EXPOSE" });
        record.ErrorDetails = new ErrorDetails("PRIVATE-SOURCE-BODY");
        var evidence = SccmFailureEvidence.Capture(stage, exception, record);
        evidence.Command.Should().Be(command);
        evidence.Category.Should().Be("PermissionDenied");
        evidence.HResult.Should().Be(exception.HResult);
        evidence.InnerHResult.Should().Be(exception.InnerException!.HResult);
        evidence.ErrorId.Should().Be("Unclassified");
        evidence.ErrorIdHash.Should().HaveLength(64);
        JsonSerializer.Serialize(evidence).Should().NotContain("DO-NOT-EXPOSE").And.NotContain("PRIVATE-SOURCE-BODY");
    }

    [Fact]
    public void Evidence_PreservesKnownModuleErrorIdentifier()
    {
        var error = new ErrorRecord(new FileNotFoundException("private path"),
            "Modules_ModuleNotFound,Microsoft.PowerShell.Commands.ImportModuleCommand", ErrorCategory.ResourceUnavailable, null);
        SccmFailureEvidence.Capture("ImportModule", error.Exception, error).ErrorId.Should().Be("Modules_ModuleNotFound");
    }

    [Fact]
    public void StagedInvocations_PreservePrivateDriveAcrossCommandsInTheSameRunspace()
    {
        string root = Directory.CreateTempSubdirectory("secureops-drive-").FullName;
        try
        {
            var state = InitialSessionState.CreateDefault2();
            state.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Restricted;
            using Runspace runspace = RunspaceFactory.CreateRunspace(state);
            runspace.Open();
            using var shell = PowerShell.Create();
            shell.Runspace = runspace;
            shell.AddCommand("New-PSDrive").AddParameter("Name", "TST").AddParameter("PSProvider", "FileSystem")
                .AddParameter("Root", root).AddParameter("Scope", "Private").AddParameter("ErrorAction", "Stop");
            shell.Invoke();
            shell.Commands.Clear();
            shell.AddCommand("Set-Location").AddParameter("LiteralPath", "TST:\\").AddParameter("ErrorAction", "Stop");
            shell.Invoke();
            shell.Commands.Clear();
            shell.AddCommand("Get-Location");
            shell.Invoke().Single().BaseObject.Should().BeOfType<PathInfo>().Which.Drive.Name.Should().Be("TST");
            shell.Commands.Clear();
            shell.AddCommand("Get-Location").AddCommand("Select-Object").AddParameter("Property", "Path")
                .AddCommand("Select-Object").AddParameter("First", 1);
            shell.Invoke().Single().Properties["Path"].Value.Should().Be("TST:\\");
            shell.HadErrors.Should().BeFalse();
        }
        finally { Directory.Delete(root); }
    }
}
