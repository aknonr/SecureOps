using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Reminders;
using SecureOps.Shared.Configuration;

namespace SecureOps.Worker.ServiceAccounts;

/// <summary>Worker wiring for the module: one additive call from the Worker's Program.cs.</summary>
public static class ServiceAccountsWorkerModule
{
    /// <summary>
    /// Registers module services and, only when the module, its reminders and the existing Hangfire server are all
    /// configured, one recurring job on the platform queue. No timer or second scheduler is introduced.
    /// </summary>
    public static IServiceCollection AddServiceAccountsWorker(this IServiceCollection services, IConfiguration configuration, bool jobServerConfigured)
    {
        ServiceAccountOptions settings = new();
        configuration.GetSection(ServiceAccountOptions.SectionName).Bind(settings);
        if (!jobServerConfigured || !settings.Enabled || !settings.Reminders.Enabled)
        {
            // Recurring jobs are never removed here (shared Hangfire state). A job registered while reminders were enabled
            // may still fire after they are disabled: it must activate and finish without SQL instead of failing and retrying.
            services.Configure<ServiceAccountOptions>(configuration.GetSection(ServiceAccountOptions.SectionName));
            services.AddScoped(provider => new ServiceAccountReminderJob(null,
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceAccountOptions>>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                provider.GetRequiredService<ILogger<ServiceAccountReminderJob>>()));
            return services;
        }
        services.AddServiceAccounts(configuration);
        services.AddSingleton<IServiceAccountScheduleStore, HangfireServiceAccountScheduleStore>();
        services.AddHostedService<ServiceAccountReminderSchedule>();
        return services;
    }
}

internal interface IServiceAccountScheduleStore
{
    public void AddOrUpdate(string id, string queue, string cron);
}

internal sealed class HangfireServiceAccountScheduleStore(JobStorage storage) : IServiceAccountScheduleStore
{
    public void AddOrUpdate(string id, string queue, string cron) =>
        new RecurringJobManager(storage).AddOrUpdate<ServiceAccountReminderJob>(id, queue,
            job => job.RunScheduledAsync(), cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}

/// <summary>Adds or updates the module-owned recurring job at Worker start-up; it runs nothing itself.</summary>
internal sealed class ServiceAccountReminderSchedule(IServiceAccountScheduleStore store, IConfiguration configuration,
    ILogger<ServiceAccountReminderSchedule> logger) : IHostedService
{
    private const string _jobPrefix = "service-accounts:reminders:v1:";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        HangfireOptions hangfire = new();
        configuration.GetSection(HangfireOptions.SectionName).Bind(hangfire);
        string queue = hangfire.Queue.ToLowerInvariant();
        ServiceAccountOptions settings = new();
        configuration.GetSection(ServiceAccountOptions.SectionName).Bind(settings);
        if (!hangfire.Enabled || !settings.Enabled || !settings.Reminders.Enabled)
        { return Task.CompletedTask; }
        store.AddOrUpdate(_jobPrefix + queue, queue, settings.Reminders.Cron);
        logger.LogInformation("Service account reminders scheduled on queue {Queue} with cron {Cron} (UTC).", queue, settings.Reminders.Cron);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
