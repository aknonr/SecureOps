using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Ui.Security;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

/// <summary>
/// Sign-in entry point.
/// </summary>
/// <remarks>
/// Server-rendered rather than an interactive component because the antiforgery cookie must be issued
/// before the response starts, which a Blazor circuit cannot do. It also keeps the page reachable when
/// no circuit can be established.
/// </remarks>
/// <param name="shellMode">Effective shell mode.</param>
/// <param name="oidcOptions">Server-owned OIDC feature configuration.</param>
[AllowAnonymous]
public sealed class LoginModel(IDemoModeState shellMode, IOptions<OidcOptions> oidcOptions) : PageModel
{
    private readonly IDemoModeState _shellMode = shellMode;
    private readonly OidcOptions _oidc = oidcOptions.Value;

    /// <summary>
    /// Whether a session can currently be established from this page.
    /// </summary>
    public bool SignInEnabled => _oidc.Enabled || _shellMode.MockAuthenticationEnabled;

    /// <summary>Whether the sign-in action invokes the configured corporate identity provider.</summary>
    public bool OidcEnabled => _oidc.Enabled;

    /// <summary>
    /// Whether this host should name its environment at all.
    /// </summary>
    /// <remarks>
    /// False in Production. A Demo or Test marker is useful to an operator who might otherwise
    /// mistake a rehearsal for the live platform; naming the environment on the production
    /// sign-in page is presentation noise that belongs to neither.
    /// </remarks>
    public bool ShowEnvironmentMarker => _shellMode.ShowEnvironmentMarker;

    /// <summary>
    /// Current host environment name.
    /// </summary>
    public string EnvironmentName => _shellMode.EnvironmentName;

    /// <summary>
    /// Safe local return path carried through sign-in.
    /// </summary>
    public string ReturnUrl { get; private set; } = LocalReturnUrl.DefaultPath;

    /// <summary>
    /// Operator-facing error message, if the previous attempt failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Handles GET /login.
    /// </summary>
    /// <param name="returnUrl">Optional local return path.</param>
    /// <param name="error">Optional safe error code.</param>
    public void OnGet([FromQuery] string? returnUrl, [FromQuery] string? error)
    {
        ReturnUrl = LocalReturnUrl.Sanitize(returnUrl);

        // Only known codes map to text. An arbitrary query value can never reach the page, so the
        // error area cannot be used to render attacker-supplied content.
        ErrorMessage = error switch
        {
            "sign-in-disabled" => "Bu ortamda oturum açma etkin değil.",
            "sign-in-failed" => "Oturum açılamadı. Lütfen tekrar deneyin.",
            _ => null
        };
    }
}
