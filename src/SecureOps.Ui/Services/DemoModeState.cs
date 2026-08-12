using Microsoft.Extensions.Options;
using SecureOps.Ui.Configuration;

namespace SecureOps.Ui.Services;

/// <summary>
/// Computes the effective shell mode from configuration and environment.
/// </summary>
public sealed class DemoModeState : IDemoModeState
{
    private readonly IHostEnvironment _environment;
    private readonly DemoModeOptions _options;

    /// <summary>
    /// Initializes a new shell mode service.
    /// </summary>
    /// <param name="environment">Current host environment.</param>
    /// <param name="options">Shell options.</param>
    public DemoModeState(IHostEnvironment environment, IOptions<DemoModeOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    /// <inheritdoc />
    public bool Enabled => _options.Enabled && IsAllowedInterimEnvironment;

    /// <inheritdoc />
    public bool MockAuthenticationEnabled => Enabled && _options.AllowMockAuthentication;

    /// <inheritdoc />
    /// <remarks>
    /// Driven by the environment rather than by the interim-auth flag: a non-production host stays
    /// marked as such even after real authentication replaces the interim path.
    /// </remarks>
    public bool ShowEnvironmentMarker => !_environment.IsProduction();

    /// <inheritdoc />
    public string EnvironmentName => _environment.EnvironmentName;

    private bool IsAllowedInterimEnvironment =>
        _environment.IsDevelopment()
        || string.Equals(_environment.EnvironmentName, "Demo", StringComparison.OrdinalIgnoreCase);
}
