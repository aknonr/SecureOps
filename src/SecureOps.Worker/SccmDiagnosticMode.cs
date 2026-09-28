using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Shared.Configuration;

namespace SecureOps.Worker;

internal static class SccmDiagnosticMode
{
    internal static async Task<SccmCollectionDiagnostic> RunAsync(IConfiguration configuration)
    {
        AnnouncementSourceOptions source = configuration.GetSection(AnnouncementSourceOptions.SectionName)
            .Get<AnnouncementSourceOptions>() ?? new();
        string? profile = configuration["SccmDiagnosticProfile"];
        if (!source.Enabled || source.CollectionProvider != "ConfigurationManager"
            || profile is null || !MaintenanceProfiles.IsAllowed(profile))
        { throw new InvalidOperationException("SccmDiagnostic.ConfigurationRequired"); }
        MaintenanceProfileOptions? selected = new MaintenanceProfileCatalog(Options.Create(source)).Configured(profile);
        if (selected is null)
        { throw new InvalidOperationException("SccmDiagnostic.ProfileRequired"); }
        var client = new ConfigurationManagerCollectionClient(Options.Create(source), TimeProvider.System,
            NullLogger<ConfigurationManagerCollectionClient>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(source.JobTimeoutSeconds, 1, 600)));
        return await client.DiagnoseAsync(selected.CollectionId, timeout.Token);
    }
}
