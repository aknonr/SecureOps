using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Validates and registers the ITSM record lookup provider (ADR-0031). Validation runs at registration, so the API
/// and the Worker both fail at startup on an unknown provider, Fake outside synthetic environments, or an incomplete
/// Turuncu Hat read configuration. Disabled is the default; nothing falls back to Fake.
/// </summary>
public static class ItsmRecordLookupRegistration
{
    /// <summary>Validates <see cref="ItsmRecordLookupOptions"/> and registers the selected <see cref="IItsmRecordLookup"/>.</summary>
    public static IServiceCollection AddItsmRecordLookup(
        this IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        string provider = Validate(configuration, environmentName);
        services.Configure<ItsmRecordLookupOptions>(configuration.GetSection(ItsmRecordLookupOptions.SectionName));
        if (string.Equals(provider, TuruncuHatItsmRecordLookup.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            // Shares the single-flight session already used by other Turuncu Hat readers; adds no credential.
            services.TryAddSingleton<ITuruncuHatSessionManager>(serviceProvider => new TuruncuHatSessionManager(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationHealthState>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<ILogger<TuruncuHatSessionManager>>()));
            services.AddSingleton<IItsmRecordLookup>(serviceProvider => new TuruncuHatItsmRecordLookup(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                serviceProvider.GetRequiredService<ITuruncuHatSessionManager>(),
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                serviceProvider.GetRequiredService<IOptions<ItsmRecordLookupOptions>>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<ILogger<TuruncuHatItsmRecordLookup>>()));
            services.AddSingleton(serviceProvider => new TuruncuHatItsmRecordTracking(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                serviceProvider.GetRequiredService<ITuruncuHatSessionManager>(),
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                serviceProvider.GetRequiredService<IOptions<ItsmRecordLookupOptions>>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<ILogger<TuruncuHatItsmRecordTracking>>()));
            services.AddSingleton<IItsmRecordTracking>(serviceProvider => serviceProvider.GetRequiredService<TuruncuHatItsmRecordTracking>());
            services.AddSingleton<IItsmWasasTracking>(serviceProvider => serviceProvider.GetRequiredService<TuruncuHatItsmRecordTracking>());
            services.AddSingleton<IItsmUserMapping>(serviceProvider => new TuruncuHatItsmUserMapping(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TuruncuHat"),
                serviceProvider.GetRequiredService<ITuruncuHatSessionManager>(),
                serviceProvider.GetRequiredService<IOptions<TuruncuHatOptions>>(),
                serviceProvider.GetRequiredService<EnterpriseIntegrationTelemetry>(),
                serviceProvider.GetRequiredService<ILogger<TuruncuHatItsmUserMapping>>()));
        }
        else if (string.Equals(provider, FakeItsmRecordLookup.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IItsmRecordLookup, FakeItsmRecordLookup>();
            services.AddSingleton<FakeItsmRecordTracking>();
            services.AddSingleton<IItsmRecordTracking>(serviceProvider => serviceProvider.GetRequiredService<FakeItsmRecordTracking>());
            services.AddSingleton<IItsmWasasTracking>(serviceProvider => serviceProvider.GetRequiredService<FakeItsmRecordTracking>());
            services.AddSingleton<IItsmUserMapping, FakeItsmUserMapping>();
        }
        else
        {
            services.AddSingleton<IItsmRecordLookup, DisabledItsmRecordLookup>();
            services.AddSingleton<DisabledItsmRecordTracking>();
            services.AddSingleton<IItsmRecordTracking>(serviceProvider => serviceProvider.GetRequiredService<DisabledItsmRecordTracking>());
            services.AddSingleton<IItsmWasasTracking>(serviceProvider => serviceProvider.GetRequiredService<DisabledItsmRecordTracking>());
            services.AddSingleton<IItsmUserMapping, DisabledItsmUserMapping>();
        }

        return services;
    }

    /// <summary>Fail-fast validation; returns the selected provider name.</summary>
    public static string Validate(IConfiguration configuration, string environmentName)
    {
        ItsmRecordLookupOptions options = configuration.GetSection(ItsmRecordLookupOptions.SectionName).Get<ItsmRecordLookupOptions>() ?? new();
        string provider = options.Provider?.Trim() ?? string.Empty;
        if (string.Equals(provider, DisabledItsmRecordLookup.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return DisabledItsmRecordLookup.ProviderName;
        }

        if (string.Equals(provider, FakeItsmRecordLookup.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsSyntheticEnvironment(environmentName))
            {
                throw new InvalidOperationException("ItsmRecordLookup:Provider Fake is permitted only in Development, Demo, or Test.");
            }

            return FakeItsmRecordLookup.ProviderName;
        }

        if (!string.Equals(provider, TuruncuHatItsmRecordLookup.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ItsmRecordLookup:Provider must be Disabled, Fake, or TuruncuHat.");
        }

        if (!IsSafeIdentifier(options.OrBaseObject) || !IsSafeIdentifier(options.OcoBaseObject))
        {
            throw new InvalidOperationException("ItsmRecordLookup:OrBaseObject and ItsmRecordLookup:OcoBaseObject are required identifiers for TuruncuHat.");
        }

        if (!IsOptionalIdentifier(options.OrStatusField) || !IsOptionalIdentifier(options.OrStageField) || !IsOptionalIdentifier(options.OrAssignedGroupField)
            || !IsOptionalIdentifier(options.CommentMainObject) || !IsOptionalIdentifier(options.ActivityMainObject)
            || !IsSafeIdentifier(options.CommentTimeField))
        {
            throw new InvalidOperationException(
                "ItsmRecordLookup:OrStatusField, OrStageField, OrAssignedGroupField, CommentMainObject, ActivityMainObject must be empty or identifiers, and CommentTimeField an identifier.");
        }

        if (!IsOptionalDigits(options.WasasGroupId) || !IsOptionalDigits(options.ActivityApprovedStatus)
            || !(options.ClosedStageIds ?? []).All(IsDigits) || !(options.ClosedStatusIds ?? []).All(IsDigits)
            || (options.ActivityTaskModelContains ?? []).Any(fragment => string.IsNullOrWhiteSpace(fragment) || fragment.Length > 128))
        {
            throw new InvalidOperationException(
                "ItsmRecordLookup:WasasGroupId, ActivityApprovedStatus, ClosedStageIds and ClosedStatusIds must be digits (the first two may be empty) and ActivityTaskModelContains entries non-empty text of at most 128 characters.");
        }

        ValidateTuruncuHatWire(configuration.GetSection(TuruncuHatOptions.SectionName).Get<TuruncuHatOptions>() ?? new());
        return TuruncuHatItsmRecordLookup.ProviderName;
    }

    // Only what a read needs: the OR-import mapping (source object, group, DCC list) is not required here.
    private static void ValidateTuruncuHatWire(TuruncuHatOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("TuruncuHat:BaseUrl must be an absolute HTTPS URL without embedded credentials.");
        }

        if (!IsRuntimeSecret(options.Authorization) || !IsRuntimeSecret(options.Username) || !IsRuntimeSecret(options.Password))
        {
            throw new InvalidOperationException(
                "TuruncuHat:Authorization, Username and Password are required and must be supplied through controlled runtime configuration.");
        }

        if (options.TenantId <= 0
            || options.SessionIdSegmentIndex < 0
            || options.SessionLifetimeSeconds is < 1 or > 86_400
            || options.ConnectTimeoutSeconds is < 1 or > 30
            || options.RequestTimeoutSeconds is < 1 or > 120
            || options.MaxResponseBytes is < 1024 or > 5_242_880)
        {
            throw new InvalidOperationException("TuruncuHat tenant, session or HTTP bounds are invalid.");
        }
    }

    private static bool IsRuntimeSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 && !value.Contains('\r') && !value.Contains('\n');

    private static bool IsOptionalDigits(string? value) => string.IsNullOrEmpty(value) || IsDigits(value);

    private static bool IsDigits(string? value) => value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit);

    private static bool IsOptionalIdentifier(string? value) => string.IsNullOrEmpty(value) || IsSafeIdentifier(value);

    private static bool IsSafeIdentifier(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_');

    private static bool IsSyntheticEnvironment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
