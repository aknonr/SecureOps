using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Worker.ServiceAccounts;
using SecureOps.Worker;

// The Worker hosts the Hangfire job server per ADR-0003. It serves no HTTP traffic and never calls
// into the API or UI: coordination is only the shared SQL database and the Hangfire queue.
try
{
    HostApplicationBuilder builder = WorkerHosting.CreateBuilder(args);
    if (args.Contains("--diagnostics", StringComparer.Ordinal) && args.Contains("--sccm-diagnostics", StringComparer.Ordinal))
    { throw new InvalidOperationException("Choose one diagnostic mode."); }
    if (args.Contains("--sccm-diagnostics", StringComparer.Ordinal))
    {
        SccmCollectionDiagnostic result = await SccmDiagnosticMode.RunAsync(builder.Configuration);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = result.State == "Complete" ? 0 : 2;
        return;
    }
    if (args.Contains("--diagnostics", StringComparer.Ordinal))
    {
        var diagnostics = new OperationsDiagnostics(builder.Configuration);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await diagnostics.InspectAsync(CancellationToken.None),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    bool service = WindowsServiceHelpers.IsWindowsService();
    string? dataDirectory = WorkerHosting.Validate(builder.Configuration, service);
    using FileStream? processLock = WorkerHosting.Acquire(dataDirectory);
    using Serilog.Core.Logger? lifecycle = WorkerHosting.CreateLifecycleLog(dataDirectory);
    try
    {
        builder.Services.AddWindowsService(options => options.ServiceName = WorkerHosting.ServiceName);
        // Service lifecycle logs use the approved directory, not automatic Event Log source creation.
        if (service)
        { builder.Logging.ClearProviders(); }
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(90));
        builder.Services.AddSecureOpsInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);

        // Preserve the legacy idle console mode; a service must not report Running with no job server.
        bool jobsConfigured = builder.Services.TryAddSecureOpsJobServer(builder.Configuration);
        if (jobsConfigured)
        { builder.Services.AddHostedService<JobServerHostedService>(); }
        else if (service)
        { throw new InvalidOperationException("WorkerHosting.JobServerConfigurationRequired"); }
        builder.Services.AddServiceAccountsWorker(builder.Configuration, jobsConfigured);

        using IHost host = builder.Build();
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using CancellationTokenRegistration started = lifetime.ApplicationStarted.Register(() => lifecycle?.Information("Worker started."));
        using CancellationTokenRegistration stopping = lifetime.ApplicationStopping.Register(() => lifecycle?.Information("Worker stopping."));
        using CancellationTokenRegistration stopped = lifetime.ApplicationStopped.Register(() => lifecycle?.Information("Worker stopped."));
        lifecycle?.Information("Worker starting. Service={Service} JobsConfigured={JobsConfigured} ProcessId={ProcessId}",
            service, jobsConfigured, Environment.ProcessId);
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OperationsComposition")
            .LogInformation("SecureOps effective operations composition: {Composition}",
                System.Text.Json.JsonSerializer.Serialize(host.Services.GetRequiredService<OperationsDiagnostics>().Composition()));
        await host.RunAsync();
    }
    catch (Exception)
    {
        // Do not persist remote bodies, configuration values or exception messages in lifecycle logs.
        lifecycle?.Error("Worker failed. Review protected diagnostics and workflow evidence before restart.");
        throw;
    }
}
catch (Exception)
{
    Console.Error.WriteLine("Worker startup/runtime failed. Check configuration, exclusive data directory and protected diagnostics.");
    Environment.ExitCode = 1;
}

namespace SecureOps.Worker
{
    /// <summary>Starts and stops the configured Hangfire job server with the Worker host lifetime.</summary>
    internal sealed class JobServerHostedService(IHostedJobServer server, ILogger<JobServerHostedService> logger)
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            server.Start();
            logger.LogInformation("SecureOps job server started.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Hangfire requests cancellation and waits its bounded shutdown. A crash can leave an
            // expired lease; existing recovery reconciles it without assuming a remote outcome.
            server.Dispose();
            logger.LogInformation("SecureOps job server stopped.");
            return Task.CompletedTask;
        }
    }
}
