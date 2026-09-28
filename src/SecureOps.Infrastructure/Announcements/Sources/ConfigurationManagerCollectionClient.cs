using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Read-only SCCM collection membership through the ConfigurationManager provider. Only Get-CMDevice
/// and the site drive are used; no write cmdlet, no device action and no credential is ever supplied.
/// The reviewed contract exposes no stable membership pagination token, so a read that hits the
/// configured ceiling is reported incomplete instead of being stitched from inconsistent windows.
/// </summary>
public sealed class ConfigurationManagerCollectionClient(IOptions<AnnouncementSourceOptions> options,
    TimeProvider time, ILogger<ConfigurationManagerCollectionClient> logger) : ICollectionMembershipClient
{
    /// <inheritdoc />
    public Task<CollectionMembershipResult> GetDevicesAsync(string collectionId, CancellationToken cancellationToken) =>
        ReadAsync(collectionId, null, cancellationToken);

    /// <summary>Runs the same bounded read once, without job dispatch, service queries or source payload output.</summary>
    public async Task<SccmCollectionDiagnostic> DiagnoseAsync(string collectionId, CancellationToken cancellationToken)
    {
        SccmFailureEvidence? failure = null;
        CollectionMembershipResult? result = null;
        try
        { result = await ReadAsync(collectionId, evidence => failure = evidence, cancellationToken); }
        catch (AnnouncementSourceException) { }
        string? ui = Environment.GetEnvironmentVariable("SMS_ADMIN_UI_PATH");
        return new(PSVersionInfo.PSVersion.ToString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(typeof(PowerShell).Assembly.Location, cancellationToken))),
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), !string.IsNullOrWhiteSpace(ui),
            !string.IsNullOrWhiteSpace(ui) && File.Exists(Path.Combine(ui, "..", "ConfigurationManager.psd1")),
            result is null ? "Failed" : result.Complete ? "Complete" : "Partial", result?.Devices.Count, result?.Complete, failure);
    }

    private async Task<CollectionMembershipResult> ReadAsync(string collectionId,
        Action<SccmFailureEvidence>? observe, CancellationToken cancellationToken)
    {
        AnnouncementSourceOptions settings = options.Value;
        if (!IsSiteCode(settings.SiteCode) || !IsProviderHost(settings.ProviderMachineName)
            || !IsCollectionId(collectionId))
        { throw new AnnouncementSourceException("AnnouncementSourceConfigurationUnavailable", false); }

        var state = InitialSessionState.CreateDefault2();
        state.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Restricted;
        using Runspace runspace = RunspaceFactory.CreateRunspace(state);
        using var shell = PowerShell.Create();
        shell.Runspace = runspace;
        string stage = "OpenRunspace";
        PSDataCollection<PSObject> output;
        try
        {
            runspace.Open();
            stage = "ImportModule";
            shell.AddCommand("Import-Module").AddParameter("Name", "ConfigurationManager").AddParameter("ErrorAction", "Stop");
            await shell.InvokeAsync().WaitAsync(cancellationToken);
            shell.Commands.Clear();
            stage = "CreateSiteDrive";
            shell.AddCommand("New-PSDrive").AddParameter("Name", settings.SiteCode)
                .AddParameter("PSProvider", "CMSite").AddParameter("Root", settings.ProviderMachineName)
                .AddParameter("Scope", "Private").AddParameter("ErrorAction", "Stop")
                .AddCommand("Out-Null");
            await shell.InvokeAsync().WaitAsync(cancellationToken);
            shell.Commands.Clear();
            stage = "SelectSiteDrive";
            shell.AddCommand("Set-Location").AddParameter("LiteralPath", settings.SiteCode + ":\\")
                .AddParameter("ErrorAction", "Stop");
            await shell.InvokeAsync().WaitAsync(cancellationToken);
            shell.Commands.Clear();
            stage = "ReadCollection";
            shell.AddCommand("Get-CMDevice")
                .AddParameter("CollectionId", collectionId).AddParameter("ErrorAction", "Stop")
                .AddCommand("Select-Object").AddParameter("Property", "Name")
                .AddCommand("Select-Object").AddParameter("First", settings.MaxDevices + 1);

            output = await shell.InvokeAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        { throw; }
        catch (Exception exception) when (exception is RuntimeException or CommandNotFoundException
            or PSInvalidOperationException or InvalidOperationException or IOException)
        {
            ErrorRecord? record = (exception as RuntimeException)?.ErrorRecord ?? shell.Streams.Error.LastOrDefault();
            var evidence = SccmFailureEvidence.Capture(stage, exception, record);
            observe?.Invoke(evidence);
            logger.LogWarning("SCCM collection membership read failed. FailureType: {FailureType}. Evidence: {Evidence}",
                exception.GetType().Name, System.Text.Json.JsonSerializer.Serialize(evidence));
            throw new AnnouncementSourceException("AnnouncementSourceCollectionUnavailable", true);
        }
        if (shell.HadErrors && output.Count == 0)
        {
            ErrorRecord? record = shell.Streams.Error.LastOrDefault();
            var evidence = SccmFailureEvidence.Capture(stage,
                record?.Exception ?? new InvalidOperationException(), record);
            observe?.Invoke(evidence);
            logger.LogWarning("SCCM collection membership read reported errors. ErrorCount: {ErrorCount}", shell.Streams.Error.Count);
            throw new AnnouncementSourceException("AnnouncementSourceCollectionUnavailable", true);
        }

        List<SourceDevice> devices = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> warnings = [];
        int duplicates = 0, malformed = 0;
        DateTimeOffset retrieved = time.GetUtcNow();
        foreach (PSObject item in output)
        {
            string? name = item?.Properties["Name"]?.Value as string;
            if (!SourceNames.IsDevice(name))
            { malformed++; continue; }
            if (!seen.Add(name!.Trim()))
            { duplicates++; continue; }
            if (devices.Count >= settings.MaxDevices)
            { warnings.Add("DeviceCeilingReached"); break; }
            devices.Add(new SourceDevice(name.Trim(), collectionId, retrieved));
        }
        bool complete = !warnings.Contains("DeviceCeilingReached") && malformed == 0;
        if (output.Count > settings.MaxDevices)
        { complete = false; warnings.Add("DeviceCeilingReached"); }
        if (shell.HadErrors)
        { warnings.Add("PartialCollectionErrors"); complete = false; }
        if (duplicates > 0)
        { warnings.Add($"DuplicateDevicesIgnored:{duplicates}"); }
        if (malformed > 0)
        { warnings.Add($"MalformedDeviceRowsSkipped:{malformed}"); }
        logger.LogInformation("SCCM collection membership read completed. DeviceCount: {DeviceCount}. Complete: {Complete}.",
            devices.Count, complete);
        return new(devices, complete, 1, [.. warnings]);
    }

    private static bool IsSiteCode(string? value) => value is { Length: 3 } && value.All(char.IsAsciiLetterOrDigit);
    private static bool IsCollectionId(string? value) => value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool IsProviderHost(string? value) => value is { Length: > 0 and <= 253 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');
}
