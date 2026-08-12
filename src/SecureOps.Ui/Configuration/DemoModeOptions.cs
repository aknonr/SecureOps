namespace SecureOps.Ui.Configuration;

/// <summary>
/// Configuration for the local UI demonstration shell.
/// </summary>
public sealed class DemoModeOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "DemoMode";

    /// <summary>
    /// Enables the presentation-only UI shell.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Allows local cookie-backed mock authentication for demo profiles.
    /// </summary>
    public bool AllowMockAuthentication { get; set; }

    /// <summary>
    /// Fixed demo actor key sent to the API's non-production demo authentication bridge.
    /// Either <c>platform-admin</c> or <c>team-lead</c>; both satisfy the lookup policy.
    /// </summary>
    public string ApiDemoActor { get; set; } = "platform-admin";

    /// <summary>
    /// Header name used to carry the demo actor key to the API.
    /// </summary>
    public string ApiDemoActorHeader { get; set; } = "X-SecureOps-Demo-Actor";
}
