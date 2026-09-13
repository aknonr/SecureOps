using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Deterministic local fixtures for isolated validation. These prove the implemented contract handling
/// only; they are not corporate evidence and carry no real device, service or collection identifier.
/// </summary>
public sealed class FixtureCollectionMembershipClient(IOptions<AnnouncementSourceOptions> options,
    TimeProvider time) : ICollectionMembershipClient
{
    /// <inheritdoc />
    public async Task<CollectionMembershipResult> GetDevicesAsync(string collectionId, CancellationToken cancellationToken)
    {
        AnnouncementSourceOptions settings = options.Value;
        FixtureCollection fixture = await FixtureFile.ReadAsync<FixtureCollection>(settings.FixtureDirectory,
            "collections", collectionId, cancellationToken) ?? throw new AnnouncementSourceException("AnnouncementSourceCollectionMissing", false);
        if (fixture.DelayMilliseconds > 0)
        { await Task.Delay(TimeSpan.FromMilliseconds(fixture.DelayMilliseconds), time, cancellationToken); }
        if (fixture.Fail)
        { throw new AnnouncementSourceException("AnnouncementSourceUnavailable", true); }
        List<SourceDevice> devices = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> warnings = [];
        int pages = 0, duplicates = 0, malformed = 0;
        DateTimeOffset retrieved = time.GetUtcNow();
        foreach (string[] page in fixture.Pages ?? [fixture.Devices ?? []])
        {
            cancellationToken.ThrowIfCancellationRequested();
            pages++;
            foreach (string name in page)
            {
                if (!SourceNames.IsDevice(name))
                { malformed++; continue; }
                if (!seen.Add(name.Trim()))
                { duplicates++; continue; }
                if (devices.Count >= settings.MaxDevices)
                { warnings.Add("DeviceCeilingReached"); return Result(devices, false, pages, warnings, duplicates, malformed); }
                devices.Add(new SourceDevice(name.Trim(), collectionId, retrieved));
            }
            if (pages >= 64)
            { warnings.Add("DevicePageCeilingReached"); return Result(devices, false, pages, warnings, duplicates, malformed); }
        }
        return Result(devices, fixture.Complete, pages, warnings, duplicates, malformed);
    }

    private static CollectionMembershipResult Result(List<SourceDevice> devices, bool complete, int pages,
        List<string> warnings, int duplicates, int malformed)
    {
        if (duplicates > 0)
        { warnings.Add($"DuplicateDevicesIgnored:{duplicates}"); }
        if (malformed > 0)
        { warnings.Add($"MalformedDeviceRowsSkipped:{malformed}"); }
        return new(devices, complete, pages, [.. warnings]);
    }

    private sealed record FixtureCollection(string[][]? Pages, string[]? Devices, bool Complete,
        bool Fail, int DelayMilliseconds);
}

/// <summary>Deterministic local service and OCO fixtures; absent files are explicit Missing results.</summary>
public sealed class FixtureAnnouncementServiceSourceClient(IOptions<AnnouncementSourceOptions> options,
    TimeProvider time) : IAnnouncementServiceSourceClient
{
    /// <inheritdoc />
    public async Task<ServiceLookupResult> GetDeviceServicesAsync(string device, CancellationToken cancellationToken)
    {
        FixtureService? fixture = await FixtureFile.ReadAsync<FixtureService>(options.Value.FixtureDirectory,
            "services", device, cancellationToken);
        if (fixture is null)
        { return new(device, [], "Missing"); }
        if (fixture.DelayMilliseconds > 0)
        { await Task.Delay(TimeSpan.FromMilliseconds(fixture.DelayMilliseconds), time, cancellationToken); }
        if (fixture.Fail)
        { throw new AnnouncementSourceException("AnnouncementSourceUnavailable", true); }
        string[] candidates = [.. (fixture.Candidates ?? []).Where(SourceNames.IsService)
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        return new(device, candidates, candidates.Length switch { 0 => "Missing", 1 => "Resolved", _ => "Ambiguous" });
    }

    /// <inheritdoc />
    public async Task<ChangeWindowResult> GetChangeWindowAsync(string ocoReference, CancellationToken cancellationToken)
    {
        FixtureChange? fixture = await FixtureFile.ReadAsync<FixtureChange>(options.Value.FixtureDirectory,
            "changes", ocoReference, cancellationToken);
        if (fixture is null)
        { return new(null, null, "Missing"); }
        if (fixture.Fail)
        { throw new AnnouncementSourceException("AnnouncementSourceUnavailable", true); }
        if (fixture.Rows > 1)
        { return new(null, null, "Ambiguous"); }
        return new(SourceNames.CleanWindow(fixture.StartText), SourceNames.CleanWindow(fixture.FinishText),
            fixture.StartText is null && fixture.FinishText is null ? "Missing" : "Resolved");
    }

    private sealed record FixtureService(string[]? Candidates, bool Fail, int DelayMilliseconds);
    private sealed record FixtureChange(string? StartText, string? FinishText, bool Fail, int Rows);
}

/// <summary>Path-safe fixture reader; only flat allowlisted names under the configured directory.</summary>
internal static class FixtureFile
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static async Task<T?> ReadAsync<T>(string directory, string area, string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)
            || !Regex.IsMatch(key, @"\A[A-Za-z0-9._-]{1,128}\z"))
        { throw new AnnouncementSourceException("AnnouncementSourceConfigurationUnavailable", false); }
        string path = Path.Combine(directory, area, key.ToUpperInvariant() + ".json");
        if (!File.Exists(path))
        { return default; }
        await using FileStream stream = File.OpenRead(path);
        if (stream.Length > 1_048_576)
        { throw new AnnouncementSourceException("AnnouncementSourceInvalidResponse", false); }
        try
        { return await JsonSerializer.DeserializeAsync<T>(stream, _json, cancellationToken); }
        catch (JsonException)
        { throw new AnnouncementSourceException("AnnouncementSourceInvalidResponse", false); }
    }
}

/// <summary>Bounded source value shapes shared by every adapter.</summary>
internal static class SourceNames
{
    public static bool IsDevice(string? value) => value is { Length: > 0 and <= 128 }
        && value.Trim().Length > 0 && !value.Any(char.IsControl);

    public static bool IsService(string? value) => value is { Length: > 0 and <= 256 }
        && value.Trim().Length > 0 && !value.Any(char.IsControl);

    // Source window values arrive without an offset; only fractional seconds and padding are trimmed.
    public static string? CleanWindow(string? value)
    {
        if (value is null)
        { return null; }
        string trimmed = value.Trim().Split('.')[0];
        return trimmed.Length is 0 or > 64 || trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
