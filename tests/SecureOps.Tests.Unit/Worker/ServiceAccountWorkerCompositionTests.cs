using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SecureOps.Worker.ServiceAccounts;

namespace SecureOps.Tests.Unit.Worker;

public sealed class ServiceAccountWorkerCompositionTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DisabledOrWithoutJobServer_RegistersNoSchedule(bool moduleEnabled, bool jobServerConfigured)
    {
        IConfiguration config = Configuration(moduleEnabled, true);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddServiceAccountsWorker(config, jobServerConfigured);

        services.Should().NotContain(d => d.ImplementationType == typeof(ServiceAccountReminderSchedule));
        services.Should().NotContain(d => d.ServiceType == typeof(IServiceAccountScheduleStore));
    }

    [Fact]
    public async Task EnabledStartup_ReusesModuleIdentity_AndLeavesOtherJobsUntouched()
    {
        IConfiguration config = Configuration(true, true);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddServiceAccountsWorker(config, true);
        RecordingStore store = new();
        store.Jobs.Add("announcement-source-recovery:announcement-source", "existing");
        services.Replace(ServiceDescriptor.Singleton<IServiceAccountScheduleStore>(store));
        using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService schedule = provider.GetServices<IHostedService>().Single(s => s is ServiceAccountReminderSchedule);

        await schedule.StartAsync(CancellationToken.None);
        await schedule.StartAsync(CancellationToken.None);

        store.Jobs.Should().HaveCount(2);
        store.Jobs["announcement-source-recovery:announcement-source"].Should().Be("existing");
        store.Jobs["service-accounts:reminders:v1:announcement-source"].Should().Be("announcement-source|15 5 * * 1-5");
        store.Calls.Should().Be(2, "repeated startup updates the same recurring identity");
    }

    /// <summary>
    /// The Worker never removes a recurring job (shared Hangfire state). If the module or its reminders are later disabled,
    /// a previously registered job still fires; Hangfire must be able to activate it and it must finish as a no-op
    /// instead of failing and retrying.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task StaleRecurringJob_AfterDisable_ActivatesAndDoesNothing(bool moduleEnabled, bool remindersEnabled)
    {
        IConfiguration config = Configuration(moduleEnabled, remindersEnabled);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddServiceAccountsWorker(config, jobServerConfigured: true);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        // Same activation path as Hangfire's ASP.NET Core job activator.
        var job = (SecureOps.Infrastructure.ServiceAccounts.Reminders.ServiceAccountReminderJob)ActivatorUtilities.GetServiceOrCreateInstance(
            scope.ServiceProvider, typeof(SecureOps.Infrastructure.ServiceAccounts.Reminders.ServiceAccountReminderJob));
        SecureOps.Shared.Contracts.ServiceAccounts.ReminderRunResult result = await job.RunAsync(CancellationToken.None);

        result.Evaluated.Should().Be(0);
        result.Enqueued.Should().Be(0);
        services.Should().NotContain(d => d.ImplementationType == typeof(ServiceAccountReminderSchedule), "disabling never adds a schedule");
    }

    private static IConfiguration Configuration(bool moduleEnabled, bool remindersEnabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceAccounts:Provider"] = moduleEnabled ? "SqlServer" : "Disabled",
            ["ServiceAccounts:Reminders:Enabled"] = remindersEnabled.ToString(),
            ["Hangfire:Enabled"] = "true",
            ["Hangfire:Queue"] = "announcement-source"
        }).Build();

    private sealed class RecordingStore : IServiceAccountScheduleStore
    {
        public Dictionary<string, string> Jobs { get; } = new(StringComparer.Ordinal);
        public int Calls { get; private set; }
        public void AddOrUpdate(string id, string queue, string cron)
        {
            Calls++;
            Jobs[id] = queue + "|" + cron;
        }
    }
}
