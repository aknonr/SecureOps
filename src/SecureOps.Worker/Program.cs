using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Worker;

// The Worker hosts the Hangfire job server per ADR-0003. It serves no HTTP traffic and never calls
// into the API or UI: coordination is only the shared SQL database and the Hangfire queue.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSecureOpsInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);

// Absent or incomplete job configuration leaves the host running without a job server rather than
// starting one against a guessed connection string or schema.
if (builder.Services.TryAddSecureOpsJobServer(builder.Configuration))
{
    builder.Services.AddHostedService<JobServerHostedService>();
}

using IHost host = builder.Build();
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
