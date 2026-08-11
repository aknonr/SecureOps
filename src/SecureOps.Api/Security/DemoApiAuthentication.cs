using Microsoft.AspNetCore.Authentication;

namespace SecureOps.Api.Security;

/// <summary>
/// Configuration for the narrowly scoped, non-production demo API authentication bridge.
/// </summary>
public sealed class DemoApiAuthOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "DemoAuth";

    /// <summary>
    /// Whether the demo authentication bridge is requested.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Request header carrying the fixed demo actor key.
    /// </summary>
    public string HeaderName { get; set; } = "X-SecureOps-Demo-Actor";
}

/// <summary>
/// Decision logic that keeps the demo API authentication bridge out of Production.
/// </summary>
public static class DemoApiAuthentication
{
    /// <summary>
    /// Authentication scheme name for the demo bridge.
    /// </summary>
    public const string SchemeName = "SecureOpsDemo";

    /// <summary>
    /// Fixed demo actor key mapped to a team-lead role.
    /// </summary>
    public const string TeamLeadActor = "team-lead";

    /// <summary>
    /// Fixed demo actor key mapped to a platform-admin role.
    /// </summary>
    public const string PlatformAdminActor = "platform-admin";

    /// <summary>
    /// Returns whether the host environment is allowed to use the demo bridge.
    /// </summary>
    /// <param name="environmentName">Host environment name.</param>
    /// <returns><c>true</c> only for Development or Demo.</returns>
    public static bool IsAllowedEnvironment(string? environmentName)
    {
        return string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Demo", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether the demo bridge should be activated.
    /// </summary>
    /// <param name="environmentName">Host environment name.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns><c>true</c> only when the environment is allowed and the explicit flag is set.</returns>
    public static bool IsEnabled(string? environmentName, IConfiguration configuration)
    {
        return IsAllowedEnvironment(environmentName)
            && configuration.GetValue($"{DemoApiAuthOptions.SectionName}:Enabled", false);
    }
}
