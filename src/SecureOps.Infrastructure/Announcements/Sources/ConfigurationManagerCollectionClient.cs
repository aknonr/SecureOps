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
    public async Task<CollectionMembershipResult> GetDevicesAsync(string collectionId, CancellationToken cancellationToken)
    {
        AnnouncementSourceOptions settings = options.Value;
        if (!IsSiteCode(settings.SiteCode) || !IsProviderHost(settings.ProviderMachineName)
            || !IsCollectionId(collectionId))
        { throw new AnnouncementSourceException("AnnouncementSourceConfigurationUnavailable", false); }

        var state = InitialSessionState.CreateDefault2();
        state.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Restricted;
        using Runspace runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        using var shell = PowerShell.Create();
        shell.Runspace = runspace;
        shell.AddCommand("Import-Module").AddParameter("Name", "ConfigurationManager").AddParameter("ErrorAction", "Stop");
        shell.AddStatement().AddCommand("New-PSDrive").AddParameter("Name", settings.SiteCode)
            .AddParameter("PSProvider", "CMSite").AddParameter("Root", settings.ProviderMachineName)
            .AddParameter("Scope", "Private").AddParameter("ErrorAction", "Stop")
            .AddCommand("Out-Null");
        shell.AddStatement().AddCommand("Set-Location").AddParameter("LiteralPath", settings.SiteCode + ":\\")
            .AddParameter("ErrorAction", "Stop");
        shell.AddStatement().AddCommand("Get-CMDevice")
            .AddParameter("CollectionId", collectionId).AddParameter("ErrorAction", "Stop")
            .AddCommand("Select-Object").AddParameter("Property", "Name")
            .AddCommand("Select-Object").AddParameter("First", settings.MaxDevices + 1);

        PSDataCollection<PSObject> output;
        try
        { output = await shell.InvokeAsync().WaitAsync(cancellationToken); }
        catch (OperationCanceledException)
        { throw; }
        catch (Exception exception) when (exception is RuntimeException or CommandNotFoundException
            or PSInvalidOperationException or InvalidOperationException or IOException)
        {
            // Module, drive and provider failures are indistinguishable from absence; never guess membership.
            logger.LogWarning("SCCM collection membership read failed. FailureType: {FailureType}", exception.GetType().Name);
            throw new AnnouncementSourceException("AnnouncementSourceCollectionUnavailable", true);
        }
        if (shell.HadErrors && output.Count == 0)
        {
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
