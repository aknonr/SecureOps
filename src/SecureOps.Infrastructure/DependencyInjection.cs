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
        services.Configure<IdentityLookupOptions>(configuration.GetSection(IdentityLookupOptions.SectionName));

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

        string? auditProvider = configuration["Audit:Provider"];
        if (string.Equals(auditProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IAuditWriter, SqlAuditWriter>();
        }
        else
        {
            services.AddSingleton<IAuditWriter, InMemoryAuditWriter>();
        }

        return services;
    }
}
