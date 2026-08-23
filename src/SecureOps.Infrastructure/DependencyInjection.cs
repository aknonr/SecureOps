using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Infrastructure.Sessions;
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
        services.Configure<DirectoryExplorerOptions>(configuration.GetSection(DirectoryExplorerOptions.SectionName));
        services.Configure<PamProviderOptions>(configuration.GetSection(PamProviderOptions.SectionName));
        services.Configure<OperationalRecordsOptions>(configuration.GetSection(OperationalRecordsOptions.SectionName));
        services.Configure<JiraIntegrationOptions>(configuration.GetSection(JiraIntegrationOptions.SectionName));
        services.Configure<TuruncuHatOptions>(configuration.GetSection(TuruncuHatOptions.SectionName));
        services.Configure<AccessOptions>(configuration.GetSection(AccessOptions.SectionName));
        services.Configure<SessionSecurityOptions>(configuration.GetSection(SessionSecurityOptions.SectionName));
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.Configure<CommandIdempotencyOptions>(configuration.GetSection(CommandIdempotencyOptions.SectionName));
        services.AddSingleton<IAuditStoreHealthState, AuditStoreHealthState>();
        services.AddSingleton<EnterpriseIntegrationHealthState>();
        services.AddSingleton<EnterpriseIntegrationTelemetry>();
        services.AddSingleton<EnterpriseIntegrationDiagnostics>();

        services.AddHttpClient("TuruncuHat", (serviceProvider, client) =>
        {
            TuruncuHatOptions options = serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>().Value;
            client.BaseAddress = ProviderBaseAddress(options.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(serviceProvider => new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>().Value.ConnectTimeoutSeconds),
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 20
        }).RemoveAllLoggers();
        services.AddHttpClient("CorporateJira", (serviceProvider, client) =>
        {
            JiraIntegrationOptions options = serviceProvider.GetRequiredService<IOptions<JiraIntegrationOptions>>().Value;
            client.BaseAddress = ProviderBaseAddress(options.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(serviceProvider => new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(
                serviceProvider.GetRequiredService<IOptions<JiraIntegrationOptions>>().Value.ConnectTimeoutSeconds),
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 20
        }).RemoveAllLoggers();

        services.AddSingleton<IIdentityAccountNormalizer, IdentityAccountNormalizer>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IIdentityLookupCacheMetrics, IdentityLookupCacheMetrics>();
        services.AddSingleton<IdentityReadThroughCache>();
        services.AddSingleton<IIdentityReadThroughCache>(sp => sp.GetRequiredService<IdentityReadThroughCache>());
        services.AddScoped<IPamAccountResolver, MockPamAccountResolver>();
        services.AddScoped<IIdentityLookupService, IdentityLookupService>();
        services.AddSingleton<DirectoryExactInputNormalizer>();
        services.AddSingleton<DirectoryQueryCache>();
        services.AddScoped<IDirectoryGroupQueryService, DirectoryGroupQueryService>();
        services.AddScoped<DirectoryMembershipGraphBuilder>();
        services.AddScoped<IDirectoryEnrichmentQueryService, DirectoryEnrichmentQueryService>();

        string? identityProvider = configuration[$"{IdentityLookupOptions.SectionName}:Provider"];
        if (string.Equals(identityProvider, "ActiveDirectory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IActiveDirectoryLookupClient, ActiveDirectoryLookupClient>();
            services.AddScoped<IIdentityDirectoryProvider, ActiveDirectoryIdentityDirectoryProvider>();
            services.AddSingleton<IActiveDirectoryGroupClient, ActiveDirectoryGroupClient>();
            services.AddSingleton<IActiveDirectoryEnrichmentClient, ActiveDirectoryGroupClient>();
            services.AddScoped<IDirectoryGroupProvider, ActiveDirectoryDirectoryGroupProvider>();
            services.AddScoped<IDirectoryEnrichmentProvider, ActiveDirectoryDirectoryEnrichmentProvider>();
        }
        else
        {
            services.AddSingleton<IIdentityDirectoryProvider>(serviceProvider =>
                new MockIdentityDirectoryProvider(
                    serviceProvider.GetRequiredService<IOptions<IdentityLookupOptions>>()));
            services.AddSingleton<IDirectoryGroupProvider, MockDirectoryGroupProvider>();
            services.AddSingleton<IDirectoryEnrichmentProvider, MockDirectoryEnrichmentProvider>();
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

        services.AddSingleton<ICorporatePrincipalResolver, CorporatePrincipalResolver>();
        services.AddScoped<IAccessIdentityProfileResolver, AccessIdentityProfileResolver>();
        if (string.Equals(configuration[$"{AccessOptions.SectionName}:RepositoryProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IAccessRepository, SqlAccessRepository>();
        }
        else
        {
            services.AddSingleton<IAccessRepository, InMemoryAccessRepository>();
        }

        services.AddScoped<IApplicationAccessService, ApplicationAccessService>();
        if (string.Equals(configuration[$"{SessionSecurityOptions.SectionName}:RepositoryProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IApplicationSessionRepository, SqlApplicationSessionRepository>();
        }
        else
        {
            services.AddSingleton<IApplicationSessionRepository, InMemoryApplicationSessionRepository>();
        }

        services.AddScoped<IApplicationSessionService, ApplicationSessionService>();
        services.AddScoped<ApplicationSessionContext>();

        string? sourceProvider = configuration[$"{OperationalRecordsOptions.SectionName}:SourceProvider"];
        if (string.Equals(sourceProvider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IOperationalRecordClient, FakeOperationalRecordClient>();
            services.AddSingleton<IOperationalRecordClassifier, FakeOperationalRecordClassifier>();
        }
        else if (string.Equals(sourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ITuruncuHatSessionManager>(serviceProvider => new TuruncuHatSessionManager(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationHealthState>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TuruncuHatSessionManager>>()));
            services.AddSingleton<IOperationalRecordClient>(serviceProvider => new TuruncuHatOperationalRecordClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                serviceProvider.GetRequiredService<ITuruncuHatSessionManager>(),
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationHealthState>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TuruncuHatOperationalRecordClient>>()));
            services.AddSingleton<IOperationalRecordClassifier, TuruncuHatOperationalRecordClassifier>();
        }
        else
        {
            services.AddSingleton<IOperationalRecordClient, DisabledOperationalRecordClient>();
            services.AddSingleton<IOperationalRecordClassifier, ManualReviewOperationalRecordClassifier>();
        }

        string? jiraProvider = configuration[$"{JiraIntegrationOptions.SectionName}:Provider"];
        if (string.Equals(jiraProvider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IJiraClient, FakeJiraClient>();
            services.AddSingleton<IRequesterResolver, FakeRequesterResolver>();
        }
        else if (string.Equals(jiraProvider, "Corporate", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IJiraClient>(serviceProvider => new CorporateJiraClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("CorporateJira"),
                serviceProvider.GetRequiredService<IOptions<JiraIntegrationOptions>>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationHealthState>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CorporateJiraClient>>()));
            services.AddSingleton<IRequesterResolver>(serviceProvider => new CorporateJiraRequesterResolver(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("CorporateJira"),
                serviceProvider.GetRequiredService<IOptions<JiraIntegrationOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationHealthState>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CorporateJiraRequesterResolver>>()));
        }
        else
        {
            services.AddSingleton<IJiraClient, DisabledJiraClient>();
            services.AddSingleton<IRequesterResolver, UnresolvedRequesterResolver>();
        }
        if (string.Equals(configuration[$"{OperationalRecordsOptions.SectionName}:RepositoryProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IOperationalRecordRepository, SqlOperationalRecordRepository>();
            services.AddScoped<ICommandIdempotencyStore, SqlCommandIdempotencyStore>();
        }
        else
        {
            services.AddSingleton<IOperationalRecordRepository, InMemoryOperationalRecordRepository>();
            services.AddSingleton<ICommandIdempotencyStore, InMemoryCommandIdempotencyStore>();
        }

        services.AddScoped<IJiraIssueDraftService, JiraIssueDraftService>();
        services.AddScoped<IOperationalRecordService, OperationalRecordService>();
        services.AddScoped<IJiraTransferService, JiraTransferService>();

        services.AddSingleton<ReportingWindowResolver>();
        services.AddSingleton<ManagementReportProjector>();
        services.AddScoped<IManagementReportingService, ManagementReportingService>();
        bool authoritativeReporting = string.Equals(auditProvider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            && string.Equals(configuration[$"{AccessOptions.SectionName}:RepositoryProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase)
            && string.Equals(configuration[$"{OperationalRecordsOptions.SectionName}:RepositoryProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase);
        if (authoritativeReporting)
        {
            services.AddScoped<IManagementReportingRepository, SqlManagementReportingRepository>();
        }
        else
        {
            services.AddSingleton<IManagementReportingRepository, UnavailableManagementReportingRepository>();
        }

        return services;
    }

    private static Uri ProviderBaseAddress(string value) =>
        new($"{value.TrimEnd('/')}/", UriKind.Absolute);

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
