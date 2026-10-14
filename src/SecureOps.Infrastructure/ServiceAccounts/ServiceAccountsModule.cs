using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts.Reminders;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Module wiring kept outside shared composition files; hosts call one extension method.</summary>
public static class ServiceAccountsModule
{
    /// <summary>Registers module services. Disabled configuration keeps every endpoint at 503 without touching SQL.</summary>
    public static IServiceCollection AddServiceAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServiceAccountOptions>(configuration.GetSection(ServiceAccountOptions.SectionName));
        ServiceAccountOptions settings = configuration.GetSection(ServiceAccountOptions.SectionName).Get<ServiceAccountOptions>() ?? new();
        bool enabled = settings.Enabled;
        if (enabled)
        {
            services.TryAddSingleton<SqlServiceAccountRepository>();
        }

        services.AddServiceAccountUsageScanJobs(settings, UsageScanJobAvailability.EnvironmentName(configuration));

        services.AddScoped(provider => new ServiceAccountService(
            enabled ? provider.GetRequiredService<SqlServiceAccountRepository>() : null,
            provider.GetRequiredService<IApplicationAccessService>(),
            provider.GetRequiredService<IOptions<ServiceAccountOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ServiceAccountService>>(),
            provider.GetService<SecureOps.Infrastructure.Identity.IDirectoryNameSearchProvider>(),
            new UsageScanJobServices(enabled ? provider.GetRequiredService<SqlServiceAccountRepository>() : null,
                provider.GetRequiredService<UsageScanJobAvailability>(), provider.GetRequiredService<IUsageScanJobDispatcher>())));
        services.AddScoped(provider => new ServiceAccountReminderJob(
            enabled ? provider.GetRequiredService<SqlServiceAccountRepository>() : null,
            provider.GetRequiredService<IOptions<ServiceAccountOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ServiceAccountReminderJob>>()));
        return services;
    }
}
