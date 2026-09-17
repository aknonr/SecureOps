using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Worker;

// The Worker hosts the Hangfire job server per ADR-0003. It serves no HTTP traffic and never calls
// into the API or UI: coordination is only the shared SQL database and the Hangfire queue.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args.Where(arg => arg != "--diagnostics").ToArray());
if (args.Contains("--diagnostics", StringComparer.Ordinal))
{
    var diagnostics = new OperationsDiagnostics(builder.Configuration);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await diagnostics.InspectAsync(CancellationToken.None),
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}
builder.Services.AddSecureOpsInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);

// Absent or incomplete job configuration leaves the host running without a job server rather than
// starting one against a guessed connection string or schema.
if (builder.Services.TryAddSecureOpsJobServer(builder.Configuration))
{
    builder.Services.AddHostedService<JobServerHostedService>();
}

using IHost host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OperationsComposition")
    .LogInformation("SecureOps effective operations composition: {Composition}",
        System.Text.Json.JsonSerializer.Serialize(host.Services.GetRequiredService<OperationsDiagnostics>().Composition()));
await host.RunAsync();

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
            // Disposing the server lets in-flight jobs finish; their outcome is persisted either way.
            server.Dispose();
            logger.LogInformation("SecureOps job server stopped.");
            return Task.CompletedTask;
        }
    }
}
