using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Infrastructure.Persistence;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Infrastructure.Resources;
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
    /// <param name="environmentName">Host-verified environment; defaults to restrictive Production.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddSecureOpsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration, string environmentName = "Production")
    {
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.Configure<IdentityLookupOptions>(configuration.GetSection(IdentityLookupOptions.SectionName));
        services.Configure<DirectoryExplorerOptions>(configuration.GetSection(DirectoryExplorerOptions.SectionName));
        services.Configure<PamProviderOptions>(configuration.GetSection(PamProviderOptions.SectionName));
        services.Configure<OperationalRecordsOptions>(configuration.GetSection(OperationalRecordsOptions.SectionName));
        services.Configure<JiraIntegrationOptions>(configuration.GetSection(JiraIntegrationOptions.SectionName));
        services.Configure<TuruncuHatOptions>(configuration.GetSection(TuruncuHatOptions.SectionName));
        services.Configure<AccessOptions>(configuration.GetSection(AccessOptions.SectionName));
        services.Configure<BootstrapAdminOptions>(configuration.GetSection(BootstrapAdminOptions.SectionName));
        services.Configure<SessionSecurityOptions>(configuration.GetSection(SessionSecurityOptions.SectionName));
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.Configure<CommandIdempotencyOptions>(configuration.GetSection(CommandIdempotencyOptions.SectionName));
        services.AddSingleton<IAuditStoreHealthState, AuditStoreHealthState>();
        services.AddSingleton<EnterpriseIntegrationHealthState>();
        services.AddSingleton<EnterpriseIntegrationTelemetry>();
        services.AddSingleton<EnterpriseIntegrationDiagnostics>();
        services.AddSingleton<ISqlPersistenceProbe, SqlPersistenceProbe>();
        services.AddSingleton<SqlPersistenceHealthReporter>();

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
        services.AddScoped<DirectoryGroupAnalysisBuilder>();
        services.AddScoped<IDirectoryGroupAnalysisService, DirectoryGroupAnalysisService>();

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
        services.AddSingleton<OidcExternalIdentityNormalizer>();
        services.AddScoped<IAccessIdentityProfileResolver, AccessIdentityProfileResolver>();
        if (string.Equals(configuration[$"{AccessOptions.SectionName}:RepositoryProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IAccessRepository, SqlAccessRepository>();
            services.AddScoped<IResourceRepository, SqlResourceRepository>();
            services.AddScoped<IInUseRepository, SqlInUseRepository>();
            services.AddScoped<IFirstAdminBootstrapStore, SqlFirstAdminBootstrapStore>();
        }
        else
        {
            services.AddSingleton<IAccessRepository, InMemoryAccessRepository>();
            services.AddSingleton<IResourceRepository, InMemoryResourceRepository>();
            services.AddSingleton<IInUseRepository, InMemoryInUseRepository>();
            services.AddSingleton<IFirstAdminBootstrapStore, UnavailableFirstAdminBootstrapStore>();
        }

        services.AddScoped<IApplicationAccessService, ApplicationAccessService>();
        services.AddScoped<ResourceCatalogueService>();
        services.Configure<AnnouncementOptions>(configuration.GetSection("Announcements"));
        services.AddScoped<Announcements.SqlAnnouncementStore>();
        services.AddScoped<SqlInUseIdentities>();
        services.AddSingleton<Announcements.AnnouncementRenderer>();
        services.AddScoped<Announcements.AnnouncementService>();
        services.Configure<AnnouncementMailOptions>(configuration.GetSection(AnnouncementMailOptions.SectionName));
        services.AddScoped<Announcements.Mail.SqlAnnouncementMailStore>();
        services.AddScoped<Commands.SqlOperationHistory>();
        services.AddSingleton(provider => new Announcements.Mail.AnnouncementMailPolicy(provider.GetRequiredService<IOptions<AnnouncementMailOptions>>(), environmentName));
        services.AddScoped<Announcements.Mail.IAnnouncementMailTransport, Announcements.Mail.SmtpAnnouncementTransport>();
        services.AddScoped<Announcements.Mail.IAnnouncementMailDispatcher, Announcements.Mail.AnnouncementMailDispatcher>();
        services.AddScoped<Announcements.Mail.AnnouncementMailWorker>();
        services.AddScoped<Announcements.Mail.AnnouncementMailRecovery>();
        AddAnnouncementSource(services, configuration);
        services.Configure<InUsePolicyOptions>(configuration.GetSection("InUsePolicy"));
        services.AddSingleton<InUsePolicy>();
        services.AddScoped<InUseService>();
        services.AddSingleton<InUseReportArchive>();
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
        services.AddSingleton<IInUseSourceClient>(provider =>
            string.Equals(sourceProvider, "Fake", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceProvider, "Simulation", StringComparison.OrdinalIgnoreCase) ? new LocalInUseSourceClient()
            : provider.GetRequiredService<IOperationalRecordClient>() is IInUseSourceClient client ? client
            : new DisabledInUseSourceClient());
        if (string.Equals(sourceProvider, "Simulation", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IOperationalRecordClient, SimulationOperationalRecordClient>();
            services.AddSingleton<IOperationalRecordClassifier, SimulationOperationalRecordClassifier>();
        }
        else if (string.Equals(sourceProvider, "Fake", StringComparison.OrdinalIgnoreCase))
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
                serviceProvider.GetRequiredService<IOptions<OperationalRecordsOptions>>(),
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
        if (string.Equals(jiraProvider, "Simulation", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IJiraClient, SimulationJiraClient>();
            services.AddSingleton<IRequesterResolver, SimulationRequesterResolver>();
        }
        else if (string.Equals(jiraProvider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IJiraClient, FakeJiraClient>();
            services.AddSingleton<IRequesterResolver, FakeRequesterResolver>();
        }
        else if (string.Equals(jiraProvider, "Corporate", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IJiraClient>(serviceProvider => new CorporateJiraClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("CorporateJira"),
                serviceProvider.GetRequiredService<IOptions<JiraIntegrationOptions>>(),
                serviceProvider.GetRequiredService<IOptions<OperationalRecordsOptions>>(),
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
        services.AddSingleton<IJiraUserResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<IRequesterResolver>());
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

    // Source adapters are selected by validated server-owned configuration and never fall back to a
    // working provider: an unset or unknown provider stays Disabled and fails closed on first use.
    private static void AddAnnouncementSource(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new Announcements.OperationsDiagnostics(configuration));
        services.Configure<AnnouncementSourceOptions>(configuration.GetSection(AnnouncementSourceOptions.SectionName));
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.SectionName));
        services.AddSingleton<Announcements.Sources.MaintenanceProfileCatalog>();
        services.AddScoped<Announcements.Sources.SqlAnnouncementSourceStore>();
        services.AddScoped<Announcements.Sources.AnnouncementSourceCollector>();
        services.AddScoped<Announcements.Sources.AnnouncementSourceService>();
        services.AddScoped<Announcements.Sources.AnnouncementSourceJobRunner>();
        services.AddScoped<Announcements.Sources.AnnouncementSourceRecovery>();
        services.AddScoped<Announcements.Sources.IAnnouncementSourceAuthorizationRecheck,
            Announcements.Sources.AnnouncementSourceAuthorizationRecheck>();

        string? collectionProvider = configuration[$"{AnnouncementSourceOptions.SectionName}:CollectionProvider"];
        if (string.Equals(collectionProvider, "ConfigurationManager", StringComparison.OrdinalIgnoreCase))
        { services.AddScoped<Announcements.Sources.ICollectionMembershipClient, Announcements.Sources.ConfigurationManagerCollectionClient>(); }
        else if (string.Equals(collectionProvider, "Fixture", StringComparison.OrdinalIgnoreCase))
        { services.AddScoped<Announcements.Sources.ICollectionMembershipClient, Announcements.Sources.FixtureCollectionMembershipClient>(); }
        else
        { services.AddScoped<Announcements.Sources.ICollectionMembershipClient, Announcements.Sources.DisabledAnnouncementSourceClient>(); }

        string? serviceProviderName = configuration[$"{AnnouncementSourceOptions.SectionName}:ServiceProvider"];
        if (string.Equals(serviceProviderName, "TuruncuHat", StringComparison.OrdinalIgnoreCase))
        {
            // The Operational Record provider may be selected independently; the session manager is shared, not duplicated.
            services.TryAddSingleton<ITuruncuHatSessionManager>(serviceProvider => new TuruncuHatSessionManager(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationHealthState>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TuruncuHatSessionManager>>()));
            // Reuses the existing single-flight session manager; announcement configuration adds no credential.
            services.AddScoped<Announcements.Sources.IAnnouncementServiceSourceClient>(serviceProvider =>
                new Announcements.Sources.TuruncuHatAnnouncementSourceClient(
                    serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                    serviceProvider.GetRequiredService<ITuruncuHatSessionManager>(),
                    serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                    serviceProvider.GetRequiredService<IOptions<AnnouncementSourceOptions>>(),
                    serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Announcements.Sources.TuruncuHatAnnouncementSourceClient>>()));
        }
        else if (string.Equals(serviceProviderName, "Fixture", StringComparison.OrdinalIgnoreCase))
        { services.AddScoped<Announcements.Sources.IAnnouncementServiceSourceClient, Announcements.Sources.FixtureAnnouncementServiceSourceClient>(); }
        else
        { services.AddScoped<Announcements.Sources.IAnnouncementServiceSourceClient, Announcements.Sources.DisabledAnnouncementSourceClient>(); }
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
