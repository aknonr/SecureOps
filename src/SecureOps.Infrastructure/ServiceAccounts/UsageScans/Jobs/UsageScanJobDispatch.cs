using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

/// <summary>
/// Whether web usage scans may run on this host, decided from configuration only (fail closed, AGENTS.md rule 6): the module is
/// enabled, a collector kind is configured, a fake collector only in Development/Demo/Test, and a dispatcher exists.
/// </summary>
public sealed class UsageScanJobAvailability(IOptions<ServiceAccountOptions> options, string environmentName, IUsageScanJobDispatcher dispatcher)
{
    private readonly ServiceAccountOptions _options = options.Value;

    /// <summary>The configured collector kind (<c>None</c>, <c>Fake</c> or <c>Jea</c>; anything else counts as <c>None</c>).</summary>
    public string Collector => _options.UsageScanJobs.Collector switch
    {
        UsageCollectorKinds.Fake => UsageCollectorKinds.Fake,
        UsageCollectorKinds.Jea => UsageCollectorKinds.Jea,
        _ => UsageCollectorKinds.None
    };

    /// <summary>Stable reason the feature is off on this host, or null when it may run.</summary>
    public string? Reason => !_options.Enabled ? "moduleDisabled"
        : Collector == UsageCollectorKinds.None ? "collectorNotConfigured"
        : !CollectorAllowed(Collector) ? "fakeNotAllowed"
        : !dispatcher.IsConfigured ? "queueNotConfigured"
        : null;

    /// <summary>True when a person may start a web scan here.</summary>
    public bool Available => Reason is null;

    /// <summary>Whether this host may use the collector kind a job was created with (a fake only outside production).</summary>
    public bool CollectorAllowed(string kind) => kind switch
    {
        UsageCollectorKinds.Jea => Collector == UsageCollectorKinds.Jea,
        UsageCollectorKinds.Fake => Collector == UsageCollectorKinds.Fake && IsNonProduction(environmentName),
        _ => false
    };

    /// <summary>
    /// The host environment as both host builders publish it in configuration (<c>environment</c>, from <c>DOTNET_ENVIRONMENT</c> or
    /// <c>ASPNETCORE_ENVIRONMENT</c>); missing means Production, so a fake collector is refused (fail closed).
    /// </summary>
    public static string EnvironmentName(IConfiguration configuration) =>
        configuration["environment"] is { Length: > 0 } name ? name : "Production";

    /// <summary>Development, Demo or Test (the same set as the demo identity bridge).</summary>
    public static bool IsNonProduction(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);

    /// <summary>Turkish text for a reason code.</summary>
    public static string ReasonText(string? reason) => reason switch
    {
        null => string.Empty,
        "moduleDisabled" => "Servis hesapları modülü bu ortamda etkin değil.",
        "collectorNotConfigured" => "Web taraması bu ortamda kapalı: sunucu tarafı toplayıcı (Worker'daki JEA çalıştırıcısı) henüz kurulu ve yapılandırılmış değil. "
            + "Taramayı onaylı script ile yapıp çıktısını \"Tarama sonucu yükle\" ile ekleyebilirsiniz.",
        "fakeNotAllowed" => "Web taraması kapalı: sahte toplayıcı yalnız geliştirme, demo ve test ortamında kullanılabilir.",
        "queueNotConfigured" => "Web taraması kapalı: iş kuyruğu (Hangfire) bu ortamda yapılandırılmamış.",
        _ => "Web taraması bu ortamda kapalı."
    };
}

/// <summary>Hands a created job to the job host. The API only enqueues; the Worker executes.</summary>
public interface IUsageScanJobDispatcher
{
    /// <summary>Whether this host can hand jobs over at all.</summary>
    public bool IsConfigured { get; }

    /// <summary>Queues the job; throws when it cannot (the caller closes the job as <c>dispatchFailed</c>).</summary>
    public void Enqueue(Guid jobId);
}

/// <summary>The existing Hangfire client and isolated queue (ADR-0001, ADR-0003); no second queue.</summary>
public sealed class HangfireUsageScanJobDispatcher(IServiceProvider services, IOptions<HangfireOptions> hangfire) : IUsageScanJobDispatcher
{
    /// <inheritdoc />
    public bool IsConfigured => hangfire.Value.Enabled && services.GetService(typeof(IBackgroundJobClient)) is IBackgroundJobClient;

    /// <inheritdoc />
    public void Enqueue(Guid jobId)
    {
        IBackgroundJobClient client = services.GetService(typeof(IBackgroundJobClient)) as IBackgroundJobClient
            ?? throw new InvalidOperationException("UsageScanJobQueueUnavailable");
        client.Create<UsageScanJobRunner>(runner => runner.RunAsync(jobId, CancellationToken.None),
            new Hangfire.States.EnqueuedState(hangfire.Value.Queue.ToLowerInvariant()));
    }
}

/// <summary>
/// Local demonstration only: with the fake collector in Development/Demo/Test and no Hangfire configured, the API host runs the same
/// runner in the background so the live view can be shown without a Worker. Refused everywhere else (fail closed).
/// </summary>
public sealed class InProcessDemoUsageScanJobDispatcher(IServiceScopeFactory scopes, ILogger<InProcessDemoUsageScanJobDispatcher> logger)
    : IUsageScanJobDispatcher
{
    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public void Enqueue(Guid jobId) =>
        _ = Task.Run(async () =>
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<UsageScanJobRunner>().RunAsync(jobId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError("Demo usage scan run failed. FailureType: {FailureType} JobId: {JobId}.", exception.GetType().Name, jobId);
            }
        });
}

/// <summary>Refuses to queue; the feature reports <c>queueNotConfigured</c>.</summary>
public sealed class UnavailableUsageScanJobDispatcher : IUsageScanJobDispatcher
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public void Enqueue(Guid jobId) => throw new InvalidOperationException("UsageScanJobQueueUnavailable");
}

/// <summary>Registration of the web usage scan job services, shared by the API and the Worker (idempotent).</summary>
public static class UsageScanJobRegistration
{
    /// <summary>
    /// Registers availability, dispatcher, runner, starter check and (only when configured as <c>Fake</c>) the fake collector. The real
    /// <c>Jea</c> collector is registered by the Worker's JEA runner (TARAMA-C), never here.
    /// </summary>
    public static IServiceCollection AddServiceAccountUsageScanJobs(this IServiceCollection services, ServiceAccountOptions settings, string environmentName)
    {
        services.TryAddSingleton<IUsageScanJobDispatcher>(provider =>
        {
            HangfireUsageScanJobDispatcher hangfire = new(provider, provider.GetRequiredService<IOptions<HangfireOptions>>());
            if (hangfire.IsConfigured)
            {
                return hangfire;
            }

            bool demo = settings.UsageScanJobs.Collector == UsageCollectorKinds.Fake && UsageScanJobAvailability.IsNonProduction(environmentName);
            return demo
                ? new InProcessDemoUsageScanJobDispatcher(provider.GetRequiredService<IServiceScopeFactory>(),
                    provider.GetRequiredService<ILogger<InProcessDemoUsageScanJobDispatcher>>())
                : new UnavailableUsageScanJobDispatcher();
        });
        services.TryAddSingleton(provider => new UsageScanJobAvailability(provider.GetRequiredService<IOptions<ServiceAccountOptions>>(), environmentName,
            provider.GetRequiredService<IUsageScanJobDispatcher>()));
        services.TryAddScoped<IUsageScanJobStarterCheck, AccessUsageScanJobStarterCheck>();
        if (settings.UsageScanJobs.Collector == UsageCollectorKinds.Fake)
        {
            int delay = Math.Clamp(settings.UsageScanJobs.FakeDelayMilliseconds, 0, 10_000);
            if (!services.Any(d => d.ServiceType == typeof(FakeServiceAccountUsageCollector)))
            {
                services.AddSingleton(provider =>
                    new FakeServiceAccountUsageCollector(provider.GetService<TimeProvider>() ?? TimeProvider.System, TimeSpan.FromMilliseconds(delay)));
                services.AddSingleton<IServiceAccountUsageCollector>(provider => provider.GetRequiredService<FakeServiceAccountUsageCollector>());
            }
        }

        services.TryAddScoped(provider => new UsageScanJobRunner(
            settings.Enabled ? provider.GetRequiredService<SqlServiceAccountRepository>() : null,
            provider.GetServices<IServiceAccountUsageCollector>(),
            provider.GetRequiredService<UsageScanJobAvailability>(),
            provider.GetRequiredService<IUsageScanJobStarterCheck>(),
            provider.GetRequiredService<IOptions<ServiceAccountOptions>>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<ILogger<UsageScanJobRunner>>()));
        return services;
    }
}
