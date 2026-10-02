using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SecureOps.Tests.Integration.Sql;
using SkiaSharp;

namespace SecureOps.Tests.Integration.Api;

internal sealed class AnnouncementSourceHosts : IAsyncDisposable
{
    private readonly List<Process> _processes = [];
    private readonly Dictionary<int, StringBuilder> _logs = [];
    public SourceTestDatabase Database { get; } = new();
    public string Root { get; }
    public Uri Address { get; }
    public string Queue { get; } = "source-" + Guid.NewGuid().ToString("N")[..8];
    public Dictionary<string, string> Settings { get; } = [];
    public Process? Worker { get; private set; }

    public AnnouncementSourceHosts()
    {
        Root = Path.Combine(Environment.GetEnvironmentVariable("SECUREOPS_SOURCE_EVIDENCE") ?? Path.GetTempPath(), "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
        listener.Stop();
        using var bitmap = new SKBitmap(64, 24);
        bitmap.Erase(SKColors.Crimson);
        using SKData image = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(Path.Combine(Root, "banner.png"), image.ToArray());
        foreach ((string key, string value) in new Dictionary<string, string>
        {
            ["DemoAuth:Enabled"] = "true",
            ["Access:DemoCompatibilityEnabled"] = "true",
            ["Access:RepositoryProvider"] = "SqlServer",
            ["SessionSecurity:RepositoryProvider"] = "SqlServer",
            ["ConnectionStrings:SecureOpsDb"] = Database.Connection,
            ["IdentityLookup:Provider"] = "Mock",
            ["Audit:Provider"] = "SqlServer",
            ["Audit:Queue:Enabled"] = "false",
            ["OperationalRecords:SourceProvider"] = "Disabled",
            ["Jira:Provider"] = "Disabled",
            ["OperationalRecords:ReadOnlyIntegrationMode"] = "false",
            ["OperationalRecords:ControlledTestWritesEnabled"] = "false",
            ["OperationalRecords:SourceCloseEnabled"] = "false",
            ["DataProtection:Mode"] = "Ephemeral",
            ["DataProtection:ApplicationName"] = "SourceAcceptance-" + Queue,
            ["Announcements:Enabled"] = "true",
            ["Announcements:Sender"] = "sender@example.invalid",
            ["Announcements:AssetDirectory"] = Root,
            ["Announcements:Banners:synthetic-v1"] = "banner.png",
            ["Announcements:Bundles:synthetic-bundle:Footer"] = "Synthetic acceptance",
            ["AnnouncementSource:Enabled"] = "true",
            ["AnnouncementSource:CollectionProvider"] = "Fixture",
            ["AnnouncementSource:ServiceProvider"] = "Fixture",
            ["AnnouncementSource:FixtureDirectory"] = Root,
            ["AnnouncementSource:JobTimeoutSeconds"] = "30",
            ["Hangfire:Enabled"] = "true",
            ["Hangfire:SchemaName"] = "HangFire",
            ["Hangfire:Queue"] = Queue,
            ["Hangfire:WorkerCount"] = "2",
            ["Hangfire:PrepareSchema"] = "false",
            ["Hangfire:QueuePollIntervalSeconds"] = "1",
            ["Logging:LogLevel:Default"] = "Information"
        })
        { Settings[key] = value; }
        foreach (string slot in new[] { "header", "main", "logo", "linkedin", "instagram", "youtube" })
        { Settings["Announcements:Bundles:synthetic-bundle:Assets:" + slot] = "synthetic-v1"; }
        foreach (string profile in new[] { "NonProd", "Prod01", "Prod02" })
        {
            foreach ((string key, string value) in new Dictionary<string, string>
            {
                ["CollectionId"] = profile.ToUpperInvariant(),
                ["Scope"] = profile + " scope",
                ["Impact"] = profile + " impact",
                ["Checks"] = profile + " checks",
                ["Description"] = profile + " description",
                ["To:0"] = profile.ToLowerInvariant() + "@example.invalid",
                ["To:1"] = "remove@example.invalid",
                ["Cc:0"] = "copy@example.invalid",
                ["Cc:1"] = profile.ToLowerInvariant() + "@example.invalid"
            })
            { Settings[$"AnnouncementSource:Profiles:{profile}:{key}"] = value; }
        }
    }

    public async Task FixturesAsync(int delay = 0)
    {
        foreach (string profile in new[] { "NONPROD", "PROD01", "PROD02" })
        {
            await FixtureAsync("collections", profile, new { pages = new[] { new[] { "DEVICE-A" }, new[] { "DEVICE-B" } }, complete = true, delayMilliseconds = profile == "PROD02" ? delay : 0 });
        }
        await FixtureAsync("services", "DEVICE-A", new { candidates = new[] { "Service A" } });
        await FixtureAsync("services", "DEVICE-B", new { candidates = new[] { "Service B" } });
        await FixtureAsync("changes", "OCO-TEST", new { rows = 1, startText = "2026-09-15T01:00:00.123+03:00", finishText = "2026-09-15T02:00:00.456+03:00" });
    }

    public async Task FixtureAsync(string area, string key, object value)
    {
        string directory = Path.Combine(Root, area);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, key + ".json"), JsonSerializer.Serialize(value));
    }

    public Process Start(string project)
    {
        string repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "SecureOps.sln")))
        { repository = Directory.GetParent(repository)?.FullName ?? throw new InvalidOperationException("Repository root not found."); }
        var start = new ProcessStartInfo("dotnet")
        { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string? payload = Environment.GetEnvironmentVariable("SECUREOPS_SOURCE_PAYLOAD_ROOT");
        string assembly = string.IsNullOrWhiteSpace(payload)
            ? Path.Combine(repository, "src", project, "bin", "Release", "net10.0", project + ".dll")
            : Path.Combine(Path.GetFullPath(payload), project["SecureOps.".Length..].ToLowerInvariant(), project + ".dll");
        if (!File.Exists(assembly))
        { throw new FileNotFoundException("Explicit source acceptance payload is missing.", assembly); }
        start.ArgumentList.Add(assembly);
        start.Environment["DOTNET_ENVIRONMENT"] = "Demo";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Demo";
        start.Environment["ASPNETCORE_URLS"] = Address.ToString();
        foreach ((string key, string value) in Settings)
        { start.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value; }
        var process = new Process { StartInfo = start };
        var log = new StringBuilder();
        process.OutputDataReceived += (_, args) => { lock (log) { log.AppendLine(args.Data); } };
        process.ErrorDataReceived += (_, args) => { lock (log) { log.AppendLine(args.Data); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
        _logs[process.Id] = log;
        if (project == "SecureOps.Worker")
        { Worker = process; }
        return process;
    }

    public HttpClient Client(string? actor = "platform-admin")
    {
        var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(20) };
        if (actor is not null)
        { client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor); }
        return client;
    }

    public static async Task UntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        using var budget = new CancellationTokenSource(timeout);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (!await predicate())
        { await timer.WaitForNextTickAsync(budget.Token); }
    }

    public async Task KillWorkerAsync()
    {
        Worker!.Kill(entireProcessTree: true);
        await Worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Process process in _processes)
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await File.WriteAllTextAsync(Path.Combine(Root, "host-" + process.Id + ".log"), _logs[process.Id].ToString());
            process.Dispose();
        }
    }
}
