using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure;

/// <summary>
/// Infrastructure dependency injection registration.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds SecureOps infrastructure services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddSecureOpsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.Configure<IdentityLookupOptions>(configuration.GetSection(IdentityLookupOptions.SectionName));
        services.Configure<PamProviderOptions>(configuration.GetSection(PamProviderOptions.SectionName));
        services.AddSingleton<IAuditStoreHealthState, AuditStoreHealthState>();

        services.AddSingleton<IIdentityAccountNormalizer, IdentityAccountNormalizer>();
        services.AddScoped<IPamAccountResolver, MockPamAccountResolver>();
        services.AddScoped<IIdentityLookupService, IdentityLookupService>();

        string? identityProvider = configuration[$"{IdentityLookupOptions.SectionName}:Provider"];
        if (string.Equals(identityProvider, "ActiveDirectory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IIdentityDirectoryProvider, ActiveDirectoryIdentityDirectoryProvider>();
        }
        else
        {
            services.AddSingleton<IIdentityDirectoryProvider, MockIdentityDirectoryProvider>();
        }

        string? auditProvider = configuration[$"{AuditOptions.SectionName}:Provider"];
        if (string.Equals(auditProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAuditEventSink, SqlAuditWriter>();
            AddPersistentAuditWriter(services, configuration);
        }
        else if (string.Equals(auditProvider, "File", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAuditEventSink, FileAuditEventSink>();
            AddPersistentAuditWriter(services, configuration);
        }
        else
        {
            services.AddSingleton<InMemoryAuditWriter>();
            services.AddSingleton<IAuditWriter>(sp => sp.GetRequiredService<InMemoryAuditWriter>());
            services.AddSingleton<IAuditEventSink>(sp => sp.GetRequiredService<InMemoryAuditWriter>());
            services.AddSingleton<IAuditQueueMetrics, NullAuditQueueMetrics>();
        }

        services.AddSingleton<AuditHealthReporter>();

        return services;
    }

    private static void AddPersistentAuditWriter(IServiceCollection services, IConfiguration configuration)
    {
        bool queueEnabled = configuration.GetValue("Audit:Queue:Enabled", true);
        if (queueEnabled)
        {
            services.AddSingleton<AuditQueue>();
            services.AddSingleton<IAuditQueueMetrics>(sp => sp.GetRequiredService<AuditQueue>());
            services.AddSingleton<IAuditWriter, QueuedAuditWriter>();
        }
        else
        {
            services.AddSingleton<IAuditQueueMetrics, NullAuditQueueMetrics>();
            services.AddSingleton<IAuditWriter, DirectAuditWriter>();
        }
    }
}
