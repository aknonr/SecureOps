using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Validates audit configuration before application startup completes.
/// </summary>
public static class AuditConfigurationValidator
{
    private static readonly string[] _validProviders = ["InMemory", "File", "SqlServer"];
    private static readonly string[] _validFullBehaviors = ["FailClosed", "DropAndCriticalLog"];

    /// <summary>
    /// Validates audit configuration for an environment.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environmentName">Host environment name.</param>
    /// <exception cref="InvalidOperationException">Thrown when configuration is unsafe or incomplete.</exception>
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        AuditOptions options = new();
        configuration.GetSection(AuditOptions.SectionName).Bind(options);

        if (!_validProviders.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit:Provider must be InMemory, File, or SqlServer.");
        }

        if (!_validFullBehaviors.Contains(options.Queue.FullBehavior, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit:Queue:FullBehavior must be FailClosed or DropAndCriticalLog.");
        }

        if (options.Queue.Enabled && options.Queue.Capacity <= 0)
        {
            throw new InvalidOperationException("Audit:Queue:Capacity must be greater than zero when queueing is enabled.");
        }

        if (options.FlushIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("Audit:FlushIntervalSeconds must be greater than zero.");
        }

        if (string.Equals(options.Provider, "File", StringComparison.OrdinalIgnoreCase))
        {
            ValidateFileOptions(options.File);
        }

        if (string.Equals(options.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required when Audit:Provider is SqlServer.");
        }

        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            ValidateProduction(configuration, options);
        }
    }

    private static void ValidateProduction(IConfiguration configuration, AuditOptions options)
    {
        if (string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit:Provider=InMemory is not allowed in Production.");
        }

        if (!options.FailClosed)
        {
            throw new InvalidOperationException("Audit:FailClosed must be true in Production.");
        }

        if (options.RequirePersistentStoreInProduction
            && string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Production requires a persistent audit store.");
        }

        if (string.Equals(options.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required when Audit:Provider is SqlServer in Production.");
        }
    }

    private static void ValidateFileOptions(AuditFileOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            throw new InvalidOperationException("Audit:File:Directory is required when Audit:Provider is File.");
        }

        if (!string.Equals(options.RollingInterval, "Day", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit:File:RollingInterval must be Day in Phase 1A.");
        }

        if (options.MaxFileSizeMB <= 0)
        {
            throw new InvalidOperationException("Audit:File:MaxFileSizeMB must be greater than zero.");
        }

        if (options.RetainedFileCountLimit <= 0)
        {
            throw new InvalidOperationException("Audit:File:RetainedFileCountLimit must be greater than zero.");
        }

        string auditDirectory = Path.GetFullPath(options.Directory);
        string publishDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (auditDirectory.StartsWith(publishDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit:File:Directory must not be inside the application publish directory.");
        }
    }
}
