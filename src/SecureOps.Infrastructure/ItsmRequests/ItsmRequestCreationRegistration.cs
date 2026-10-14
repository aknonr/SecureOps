using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SecureOps.Infrastructure.ItsmRequests;

/// <summary>Server-owned OR creation configuration (ADR-0030 §5). Disabled by default; nothing falls back to Fake.</summary>
public sealed class ItsmRequestCreationOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "ItsmRequestCreation";

    /// <summary>"Disabled" (default) or "Fake" (Development, Demo, Test only). No real provider exists yet.</summary>
    public string Provider { get; set; } = DisabledItsmRequestCreator.ProviderName;

    /// <summary>Upper bound for one dispatch, independent of the browser request; after it the outcome is unknown.</summary>
    public int DispatchTimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Validates and registers <see cref="IItsmRequestCreator"/>. Validation runs at registration, so the host fails at startup on
/// an unknown provider, Fake outside synthetic environments, or a request for the Turuncu Hat adapter, which is deliberately
/// not implemented (ADR-0030 is Proposed; the insert contract is not evidenced; AGENTS.md rule 8).
/// </summary>
public static class ItsmRequestCreationRegistration
{
    /// <summary>Registers the selected creator (Disabled unless configured otherwise).</summary>
    public static IServiceCollection AddItsmRequestCreation(this IServiceCollection services, IConfiguration configuration, string? environmentName)
    {
        string provider = Validate(configuration, environmentName);
        services.Configure<ItsmRequestCreationOptions>(configuration.GetSection(ItsmRequestCreationOptions.SectionName));
        if (string.Equals(provider, FakeItsmRequestCreator.ProviderName, StringComparison.Ordinal))
        {
            services.TryAddSingleton<IItsmRequestCreator, FakeItsmRequestCreator>();
        }
        else
        {
            services.TryAddSingleton<IItsmRequestCreator, DisabledItsmRequestCreator>();
        }

        return services;
    }

    /// <summary>Fail-fast validation; returns the selected provider name.</summary>
    public static string Validate(IConfiguration configuration, string? environmentName)
    {
        ItsmRequestCreationOptions options = configuration.GetSection(ItsmRequestCreationOptions.SectionName).Get<ItsmRequestCreationOptions>() ?? new();
        if (options.DispatchTimeoutSeconds is < 5 or > 300)
        {
            throw new InvalidOperationException("ItsmRequestCreation:DispatchTimeoutSeconds must be between 5 and 300.");
        }

        string provider = options.Provider?.Trim() ?? string.Empty;
        if (string.Equals(provider, DisabledItsmRequestCreator.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return DisabledItsmRequestCreator.ProviderName;
        }

        if (string.Equals(provider, FakeItsmRequestCreator.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return IsSyntheticEnvironment(environmentName)
                ? FakeItsmRequestCreator.ProviderName
                : throw new InvalidOperationException("ItsmRequestCreation:Provider Fake is permitted only in Development, Demo, or Test.");
        }

        if (string.Equals(provider, "TuruncuHat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ItsmRequestCreation:Provider TuruncuHat has no implementation: ADR-0030 is Proposed and the insert contract is not evidenced.");
        }

        throw new InvalidOperationException("ItsmRequestCreation:Provider must be Disabled or Fake.");
    }

    private static bool IsSyntheticEnvironment(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
