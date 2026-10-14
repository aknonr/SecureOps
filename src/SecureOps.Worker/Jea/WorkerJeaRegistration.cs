using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

namespace SecureOps.Worker.Jea;

/// <summary>Worker wiring of the JEA runner and the <c>Jea</c> usage collector (fail closed, AGENTS.md rule 6).</summary>
public static class WorkerJeaRegistration
{
    /// <summary>
    /// Registers <see cref="IJeaRunner"/> and, only when the module is enabled and <c>ServiceAccounts:UsageScanJobs:Collector = Jea</c>,
    /// the <c>Jea</c> collector. No collector is registered when the transport cannot be used here (real WinRM off Windows, the fake in
    /// production, an unknown transport name): a queued job then ends <c>collectorUnavailable</c> and no server is contacted.
    /// </summary>
    /// <returns>The registered transport kind, or null when the runner is not available on this host.</returns>
    public static string? AddWorkerJea(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JeaRunnerOptions>(configuration.GetSection(JeaRunnerOptions.SectionName));
        JeaRunnerOptions jea = new();
        configuration.GetSection(JeaRunnerOptions.SectionName).Bind(jea);
        string environment = UsageScanJobAvailability.EnvironmentName(configuration);
        string? transport = jea.Transport switch
        {
            JeaTransportKinds.WsMan when OperatingSystem.IsWindows() => JeaTransportKinds.WsMan,
            JeaTransportKinds.Fake when UsageScanJobAvailability.IsNonProduction(environment) => JeaTransportKinds.Fake,
            _ => null
        };
        if (transport is null)
        {
            return null;
        }

        if (transport == JeaTransportKinds.Fake)
        {
            services.TryAddSingleton<IJeaRunner>(_ => new FakeJeaRunner(delay: TimeSpan.FromMilliseconds(300)));
        }
        else
        {
            services.TryAddSingleton<IJeaTransport>(provider => new WsManJeaTransport(provider.GetRequiredService<IOptions<JeaRunnerOptions>>()));
            services.TryAddSingleton<IJeaRunner>(provider => new JeaRunner(provider.GetRequiredService<IJeaTransport>(),
                provider.GetRequiredService<ILogger<JeaRunner>>()));
        }

        ServiceAccountOptions settings = new();
        configuration.GetSection(ServiceAccountOptions.SectionName).Bind(settings);
        if (settings.Enabled && settings.UsageScanJobs.Collector == UsageCollectorKinds.Jea
            && !services.Any(d => d.ServiceType == typeof(JeaServiceAccountUsageCollector)))
        {
            services.AddSingleton<JeaServiceAccountUsageCollector>();
            services.AddSingleton<IServiceAccountUsageCollector>(provider => provider.GetRequiredService<JeaServiceAccountUsageCollector>());
        }

        return transport;
    }
}
