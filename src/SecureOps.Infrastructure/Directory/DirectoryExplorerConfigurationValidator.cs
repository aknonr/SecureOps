using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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

        if (options.TraversalTimeoutSeconds is < 1 or > 60
            || options.MaxTraversalDepth is < 1 or > 32
            || options.MaxTraversalNodes is < 1 or > 5000
            || options.MaxTraversalEdges is < 1 or > 10_000
            || options.MaxTraversalDepth > options.MaxTraversalNodes
            || options.MaxEffectiveMembers is < 1 or > 5000
            || options.MaxExportRows is < 1 or > 5000
            || options.MaxMembershipPaths is < 1 or > 20
            || options.MaxSpnsPerPrincipal is < 1 or > 500
            || options.MaxPrivilegedGroupIdentifiers is < 1 or > 128)
        {
            throw new InvalidOperationException("DirectoryExplorer traversal and enrichment settings are outside safe bounds.");
        }

        string[] configuredGroups = options.PrivilegedGroupIdentifiers ?? [];
        DirectoryExactInputNormalizer normalizer = new(Options.Create(options));
        string[] normalizedGroups = configuredGroups
            .Select(group => normalizer.NormalizeGroup(group))
            .Where(result => result.IsValid && result.Value is not null)
            .Select(result => result.Value!)
            .ToArray();
        if (configuredGroups.Length > options.MaxPrivilegedGroupIdentifiers
            || normalizedGroups.Length != configuredGroups.Length
            || normalizedGroups.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedGroups.Length)
        {
            throw new InvalidOperationException(
                "DirectoryExplorer privileged-group identifiers must be bounded, unique, and exact-input safe.");
        }
    }
}
