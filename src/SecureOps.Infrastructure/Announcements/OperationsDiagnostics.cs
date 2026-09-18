using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Startup-bound allowlisted composition plus bounded read-only storage/file observations.</summary>
public sealed class OperationsDiagnostics
{
    private readonly IConfiguration _configuration;
    private readonly AnnouncementOptions _announcements;
    private readonly AnnouncementSourceOptions _source;
    private readonly HangfireOptions _hangfire;
    private readonly string? _connection;
    private readonly string _reportDirectory;
    private readonly EffectiveOperationSetting[] _settings;
    private readonly string _fingerprint;
    private readonly string[] _missingSourceKeys;
    private readonly DateTimeOffset _capturedAt = DateTimeOffset.UtcNow;

    /// <summary>Captures composed configuration, including environment overrides, once during registration.</summary>
    public OperationsDiagnostics(IConfiguration configuration)
    {
        _announcements = configuration.GetSection("Announcements").Get<AnnouncementOptions>() ?? new();
        _source = configuration.GetSection("AnnouncementSource").Get<AnnouncementSourceOptions>() ?? new();
        _hangfire = configuration.GetSection("Hangfire").Get<HangfireOptions>() ?? new();
        AnnouncementMailOptions mail = configuration.GetSection("AnnouncementMail").Get<AnnouncementMailOptions>() ?? new();
        _connection = configuration.GetConnectionString("SecureOpsDb");
        _reportDirectory = configuration["InUseReports:Directory"] ?? "";
        _missingSourceKeys = [.. new[] { "BaseUrl", "Authorization", "Username", "Password", "TenantId", "SessionLifetimeSeconds" }
            .Where(key => string.IsNullOrWhiteSpace(configuration["TuruncuHat:" + key])
                || key is "TenantId" or "SessionLifetimeSeconds" && (!int.TryParse(configuration["TuruncuHat:" + key], out int number) || number <= 0))
            .Select(key => "TuruncuHat:" + key)];
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["InUseReports:Directory"] = _reportDirectory }).Build();
        string[] keys = ["Announcements:Enabled", "Hangfire:Enabled", "Hangfire:SchemaName", "Hangfire:Queue", "Hangfire:PrepareSchema",
            "AnnouncementSource:Enabled", "AnnouncementSource:CollectionProvider", "AnnouncementSource:ServiceProvider",
            "AnnouncementSource:SiteCode", "AnnouncementSource:ProviderMachineName", "AnnouncementSource:ServiceInstanceBaseObject",
            "AnnouncementSource:ServiceNameSelect", "AnnouncementSource:ChangeBaseObject",
            "InUseCompletion:Enabled", "InUseCompletion:Provider", "InUseCompletion:TimeoutSeconds",
            "AnnouncementMail:Enabled", "AnnouncementMail:SelfTestEnabled", "AnnouncementMail:SendEnabled",
            "TuruncuHat:InUseAspectLookupEnabled"];
        var bound = new Dictionary<string, JsonElement>
        {
            ["Announcements"] = JsonSerializer.SerializeToElement(_announcements),
            ["Hangfire"] = JsonSerializer.SerializeToElement(_hangfire),
            ["AnnouncementMail"] = JsonSerializer.SerializeToElement(new { mail.Enabled, mail.SelfTestEnabled, mail.SendEnabled }),
            ["AnnouncementSource"] = JsonSerializer.SerializeToElement(_source),
            ["InUseCompletion"] = JsonSerializer.SerializeToElement(configuration.GetSection("InUseCompletion").Get<InUseCompletionOptions>() ?? new()),
            ["TuruncuHat"] = JsonSerializer.SerializeToElement(new { InUseAspectLookupEnabled = configuration.GetValue<bool>("TuruncuHat:InUseAspectLookupEnabled") })
        };
        _settings = [.. keys.Select(key => new EffectiveOperationSetting(key,
                bound[key.Split(':')[0]].GetProperty(key.Split(':')[1]).ToString(), Provider(configuration, key))),
            .. _source.Profiles.Where(p => MaintenanceProfiles.IsAllowed(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal).Take(5).SelectMany(p => new[] {
                new EffectiveOperationSetting($"AnnouncementSource:Profiles:{p.Key}:CollectionId", p.Value.CollectionId, Provider(configuration, $"AnnouncementSource:Profiles:{p.Key}:CollectionId")),
                new EffectiveOperationSetting($"AnnouncementSource:Profiles:{p.Key}:Fingerprint", MaintenanceProfileCatalog.Fingerprint(p.Value), "BoundProfile") }),
            new EffectiveOperationSetting("AnnouncementMail:PolicyFingerprint", new Mail.AnnouncementMailPolicy(Options.Create(mail), "Diagnostics").Fingerprint(), "BoundPolicyNoCredentials"),
            new EffectiveOperationSetting("ConnectionStrings:SecureOpsDb:TargetFingerprint", DatabaseFingerprint(_connection), Provider(configuration, "ConnectionStrings:SecureOpsDb")),
            new EffectiveOperationSetting("TuruncuHat:TargetFingerprint", Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { BaseUrl = configuration["TuruncuHat:BaseUrl"], TenantId = configuration["TuruncuHat:TenantId"] }))), "BoundConfiguration")];
        _fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(_settings.Select(s => new { s.Key, s.Value }))));
    }

    /// <summary>Nonsecret actual startup composition for Worker/API comparison; no filesystem or network probe.</summary>
    public object Composition() => new
    {
        ProcessId = Environment.ProcessId,
        RuntimeIdentity = RuntimeIdentity(),
        CapturedAt = _capturedAt,
        ConfigurationFingerprint = _fingerprint,
        Settings = _settings
    };

    /// <summary>Reads only this configured Hangfire schema, never provisions it or contacts a source provider.</summary>
    public async Task<AnnouncementSourceReadiness> SourceAsync(CancellationToken token)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!_announcements.Enabled || !_source.Enabled)
        { return new("Disabled", "configuration", [], "NotChecked", 0, null, now); }
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(_connection))
        { missing.Add("ConnectionStrings:SecureOpsDb"); }
        if (!_hangfire.Enabled)
        { missing.Add("Hangfire:Enabled"); }
        if (_hangfire.PrepareSchema)
        { missing.Add("Hangfire:PrepareSchema=false"); }
        if (!SafeName(_hangfire.SchemaName, 64, false))
        { missing.Add("Hangfire:SchemaName"); }
        if (!SafeName(_hangfire.Queue, 20, true))
        { missing.Add("Hangfire:Queue"); }
        if (_source.CollectionProvider is not ("Fixture" or "ConfigurationManager"))
        { missing.Add("AnnouncementSource:CollectionProvider"); }
        if (_source.ServiceProvider is not ("Fixture" or "TuruncuHat"))
        { missing.Add("AnnouncementSource:ServiceProvider"); }
        if (_source.CollectionProvider == "ConfigurationManager")
        {
            if (string.IsNullOrWhiteSpace(_source.SiteCode))
            { missing.Add("AnnouncementSource:SiteCode"); }
            if (string.IsNullOrWhiteSpace(_source.ProviderMachineName))
            { missing.Add("AnnouncementSource:ProviderMachineName"); }
        }
        if (_source.ServiceProvider == "TuruncuHat")
        {
            missing.AddRange(_missingSourceKeys);
            foreach ((string key, string value) in new[] { ("ServiceInstanceBaseObject", _source.ServiceInstanceBaseObject), ("ServiceNameSelect", _source.ServiceNameSelect), ("ChangeBaseObject", _source.ChangeBaseObject) })
            { if (string.IsNullOrWhiteSpace(value)) { missing.Add("AnnouncementSource:" + key); } }
        }
        var profiles = new MaintenanceProfileCatalog(Options.Create(_source));
        if (!profiles.Choices().Any(p => p.State == "Configured"))
        { missing.Add("AnnouncementSource:Profiles"); }
        if (missing.Count > 0)
        { return new("ConfigurationMissing", "configuration", [.. missing], "NotChecked", 0, null, now); }
        return await QueueAsync(token);
    }

    /// <summary>Reads configured Worker/queue health independently of any module being enabled.</summary>
    public async Task<AnnouncementSourceReadiness> QueueAsync(CancellationToken token)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!_hangfire.Enabled)
        { return new("Disabled", "queue", [], "NotChecked", 0, null, now); }
        if (_hangfire.PrepareSchema || !SafeName(_hangfire.SchemaName, 64, false) || !SafeName(_hangfire.Queue, 20, true)
            || string.IsNullOrWhiteSpace(_connection))
        { return new("ConfigurationMissing", "queue", [], "NotChecked", 0, null, now); }
        try
        {
            var sql = new SqlConnectionStringBuilder(_connection) { ConnectTimeout = 3 };
            await using var connection = new SqlConnection(sql.ConnectionString);
            await connection.OpenAsync(token);
            IEnumerable<Heartbeat> heartbeats = await connection.QueryAsync<Heartbeat>(new CommandDefinition(
                $"SELECT TOP (100) LastHeartbeat, Data FROM [{_hangfire.SchemaName}].[Server] ORDER BY LastHeartbeat DESC", commandTimeout: 3, cancellationToken: token));
            Heartbeat[] live = [.. heartbeats.Where(h => h.LastHeartbeat >= now.UtcDateTime.AddMinutes(-2))];
            Heartbeat[] matching = [.. live.Where(h => MatchesQueue(h.Data, _hangfire.Queue))];
            string state = matching.Length > 0 ? "Ready" : live.Length == 0 ? "NoWorker" : "WrongQueue";
            DateTimeOffset? last = matching.Length > 0 ? new DateTimeOffset(DateTime.SpecifyKind(matching.Max(h => h.LastHeartbeat), DateTimeKind.Utc)) : null;
            return new(state, "queue", [], state, matching.Length, last, now);
        }
        catch (Exception exception) when (exception is DbException or ArgumentException or InvalidOperationException or JsonException)
        { return new("Unknown", "storage", [], "Unknown", 0, null, now); }
    }

    /// <summary>Authorized file/configuration diagnosis. Report write access is explicitly not tested.</summary>
    public async Task<OperationsReadiness> InspectAsync(CancellationToken token)
    {
        var renderer = new AnnouncementRenderer(Options.Create(_announcements));
        List<OperationAssetReadiness> assets = [];
        foreach (string revision in _announcements.Banners.Keys.Order(StringComparer.Ordinal).Take(32))
        {
            try
            { (byte[] Bytes, string Type, string Hash) asset = await renderer.AssetAsync(revision, token); assets.Add(new(revision, "Validated", asset.Type, asset.Hash)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            { assets.Add(new(revision, e is UnauthorizedAccessException ? "PermissionDenied" : e is FileNotFoundException or DirectoryNotFoundException ? "Missing" : "Invalid", null, null)); }
        }
        string reportState;
        try
        { reportState = new InUseReportArchive(_configuration).InspectDirectory(); }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        { reportState = e is UnauthorizedAccessException ? "PermissionDenied" : "InvalidOrUnavailable"; }
        IReadOnlyList<AnnouncementBanner> bundles;
        try
        { bundles = renderer.Bundles(token); }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        { bundles = [new("configuration", "Görsel eşlemesi", "Invalid")]; }
        return new(RuntimeIdentity(), Environment.ProcessId, _capturedAt, _fingerprint, _settings,
            await SourceAsync(token), assets, _announcements.AssetDirectory, _reportDirectory, reportState)
        { Bundles = bundles };
    }

    private static bool SafeName(string value, int maximum, bool hyphen) => value.Length is > 0 && value.Length <= maximum
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || hyphen && c == '-');
    private static string RuntimeIdentity()
    {
        if (!OperatingSystem.IsWindows())
        { return Environment.UserName; }
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.Name;
    }
    private static string Provider(IConfiguration configuration, string key) => configuration is IConfigurationRoot root
        ? root.Providers.Reverse().FirstOrDefault(p => p.TryGet(key, out _))?.GetType().Name ?? "Default" : "BoundConfiguration";
    private static string DatabaseFingerprint(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
        { return "Missing"; }
        try
        { var sql = new SqlConnectionStringBuilder(connection); return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { sql.DataSource, sql.InitialCatalog }))); }
        catch (ArgumentException) { return "Invalid"; }
    }
    private static bool MatchesQueue(string data, string queue)
    {
        using var json = JsonDocument.Parse(data);
        return json.RootElement.TryGetProperty("Queues", out JsonElement queues) && queues.ValueKind == JsonValueKind.Array
            && queues.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), queue, StringComparison.OrdinalIgnoreCase));
    }
    private sealed record Heartbeat(DateTime LastHeartbeat, string Data);
}
