using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Hangfire with SQL Server storage per ADR-0001 and ADR-0003, composed for the first time here.
/// The API registers only the enqueue client; the Worker additionally runs the job server. No second
/// queue, in-process background loop or polling service is introduced. Hangfire never creates its own
/// schema unless the isolated local test facility explicitly opts in, because the application does not
/// apply migrations in a corporate environment.
/// </summary>
public static class AnnouncementSourceJobHost
{
    /// <summary>Registers durable storage and the enqueue client; disabled configuration fails closed.</summary>
    public static IServiceCollection AddSecureOpsJobClient(this IServiceCollection services, IConfiguration configuration)
    {
        HangfireOptions options = Read(configuration);
        string? connection = configuration.GetConnectionString("SecureOpsDb");
        if (!options.Enabled || string.IsNullOrWhiteSpace(connection) || !IsSchemaName(options.SchemaName))
        {
            services.TryAddSingleton<IAnnouncementSourceDispatcher, UnavailableAnnouncementSourceDispatcher>();
            return services;
        }
        services.TryAddSingleton<JobStorage>(_ => Storage(connection, options));
        services.TryAddSingleton<IBackgroundJobClient>(provider => new BackgroundJobClient(provider.GetRequiredService<JobStorage>()));
        services.TryAddSingleton<IAnnouncementSourceDispatcher>(provider =>
            new HangfireAnnouncementSourceDispatcher(provider.GetRequiredService<IBackgroundJobClient>(), Queue(options.Queue)));
        return services;
    }

    /// <summary>Adds the Worker's job server. Returns false when configuration does not permit hosting.</summary>
    public static bool TryAddSecureOpsJobServer(this IServiceCollection services, IConfiguration configuration)
    {
        HangfireOptions options = Read(configuration);
        string? connection = configuration.GetConnectionString("SecureOpsDb");
        if (!options.Enabled || string.IsNullOrWhiteSpace(connection) || !IsSchemaName(options.SchemaName))
        { return false; }
        services.AddSecureOpsJobClient(configuration);
        services.AddSingleton<JobActivator, ServiceScopeJobActivator>();
        services.AddSingleton<IHostedJobServer>(provider => new HangfireJobServer(
            provider.GetRequiredService<JobStorage>(),
            provider.GetRequiredService<JobActivator>(),
            options));
        return true;
    }

    private static HangfireOptions Read(IConfiguration configuration)
    {
        HangfireOptions options = new();
        configuration.GetSection(HangfireOptions.SectionName).Bind(options);
        return options;
    }

    private static SqlServerStorage Storage(string connection, HangfireOptions options) =>
        new(connection, new SqlServerStorageOptions
        {
            SchemaName = options.SchemaName,
            PrepareSchemaIfNecessary = options.PrepareSchema,
            QueuePollInterval = TimeSpan.FromSeconds(Math.Clamp(options.QueuePollIntervalSeconds, 1, 60)),
            SlidingInvisibilityTimeout = TimeSpan.FromMinutes(Math.Clamp(options.InvisibilityTimeoutMinutes, 5, 180)),
            CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = true
        });

    private static string Queue(string value) =>
        IsQueueName(value) ? value.ToLowerInvariant() : "announcement-source";

    private static bool IsQueueName(string? value) => value is { Length: > 0 and <= 20 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool IsSchemaName(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}

/// <summary>A startable job server the Worker host owns.</summary>
public interface IHostedJobServer : IDisposable
{
    /// <summary>Begins processing the configured queue.</summary>
    public void Start();
}

/// <summary>Hangfire background job server bound to the configured queue only.</summary>
public sealed class HangfireJobServer(JobStorage storage, JobActivator activator, HangfireOptions options) : IHostedJobServer
{
    private BackgroundJobServer? _server;

    /// <inheritdoc />
    public void Start() => _server ??= new BackgroundJobServer(new BackgroundJobServerOptions
    {
        Queues = [options.Queue.ToLowerInvariant()],
        WorkerCount = options.WorkerCount > 0 ? Math.Clamp(options.WorkerCount, 1, 64) : Environment.ProcessorCount * 2,
        Activator = activator,
        ServerName = Environment.MachineName + ":secureops-worker"
    }, storage);

    /// <inheritdoc />
    public void Dispose() => _server?.Dispose();
}

/// <summary>
/// Resolves job classes from a per-job dependency scope so scoped SQL stores behave exactly as they do
/// in a request. Without this the Worker would share one container-root instance across every job.
/// </summary>
public sealed class ServiceScopeJobActivator(IServiceScopeFactory scopes) : JobActivator
{
    /// <inheritdoc />
    public override JobActivatorScope BeginScope(JobActivatorContext context) => new Scope(scopes.CreateScope());

    private sealed class Scope(IServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => scope.ServiceProvider.GetRequiredService(type);
        public override void DisposeScope() => scope.Dispose();
    }
}
