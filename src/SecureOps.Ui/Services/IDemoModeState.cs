namespace SecureOps.Ui.Services;

/// <summary>
/// Effective shell mode: which interim mechanisms are active, and whether this host should say so.
/// </summary>
/// <remarks>
/// Named for the underlying configuration section it reads (<c>DemoMode</c>), but the UI treats it as
/// environment state rather than a feature to advertise. The shell exposes a single quiet environment
/// marker; no screen presents "demo" or "sample data" as part of its content.
/// </remarks>
public interface IDemoModeState
{
    /// <summary>
    /// Whether interim shell mechanisms are enabled for the current environment.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Whether the interim cookie sign-in path may issue a session.
    /// </summary>
    public bool MockAuthenticationEnabled { get; }

    /// <summary>
    /// Whether the app bar should carry a non-production environment marker.
    /// </summary>
    public bool ShowEnvironmentMarker { get; }

    /// <summary>
    /// Current ASP.NET Core environment name.
    /// </summary>
    public string EnvironmentName { get; }
}
