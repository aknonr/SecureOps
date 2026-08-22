using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Validates bounded Directory Explorer settings before startup.</summary>
public static class DirectoryExplorerConfigurationValidator
{
    /// <summary>Validates all Directory Explorer limits.</summary>
    public static void Validate(IConfiguration configuration)
    {
        DirectoryExplorerOptions options = configuration.GetSection(DirectoryExplorerOptions.SectionName)
            .Get<DirectoryExplorerOptions>() ?? new();

        if (options.DefaultPageSize < 1 || options.MaxPageSize is < 1 or > 500
            || options.DefaultPageSize > options.MaxPageSize)
        {
            throw new InvalidOperationException("DirectoryExplorer page sizes are outside safe bounds.");
        }

        if (options.ProviderResultLimit < options.MaxPageSize || options.ProviderResultLimit > 50_000
            || options.ProviderTimeoutSeconds is < 1 or > 60
            || options.ContinuationTokenLifetimeSeconds is < 30 or > 3600)
        {
            throw new InvalidOperationException("DirectoryExplorer provider and continuation limits are outside safe bounds.");
        }

        if (options.MaxGroupInputLength is < 1 or > 512 || options.MaxPurposeLength is < 1 or > 1024
            || options.Cache.TtlSeconds is < 1 or > 300 || options.Cache.MaxEntries is < 1 or > 5000)
        {
            throw new InvalidOperationException("DirectoryExplorer input and cache settings are outside safe bounds.");
        }
    }
}
