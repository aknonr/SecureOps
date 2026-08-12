using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly IDemoModeState _shellMode;

    /// <summary>
    /// Initializes a new login page model.
    /// </summary>
    /// <param name="shellMode">Effective shell mode.</param>
    public LoginModel(IDemoModeState shellMode)
    {
        _shellMode = shellMode;
    }

    /// <summary>
    /// Whether a session can currently be established from this page.
    /// </summary>
    public bool SignInEnabled => _shellMode.MockAuthenticationEnabled;

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
