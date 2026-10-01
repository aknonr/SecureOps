using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureOps.Ui.Security;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

/// <summary>
/// Shown when a session lapsed rather than being deliberately ended.
/// </summary>
[AllowAnonymous]
public sealed class SessionExpiredModel : PageModel
{
    private readonly IDemoModeState _shellMode;

    /// <summary>
    /// Initializes a new session-expired page model.
    /// </summary>
    /// <param name="shellMode">Effective shell mode.</param>
    public SessionExpiredModel(IDemoModeState shellMode)
    {
        _shellMode = shellMode;
    }

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
    /// Sign-in URL carrying the sanitized return path.
    /// </summary>
    public string SignInUrl { get; private set; } = "login";

    /// <summary>
    /// Handles GET /session-expired.
    /// </summary>
    /// <param name="returnUrl">Optional local path the operator was on when the session lapsed.</param>
    public void OnGet([FromQuery] string? returnUrl)
    {
        string safeReturnUrl = LocalReturnUrl.Sanitize(returnUrl);
        SignInUrl = $"{Url.Content("~/login")}?returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
    }
}
