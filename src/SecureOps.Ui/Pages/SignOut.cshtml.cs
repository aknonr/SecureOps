using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

/// <summary>
/// Sign-out confirmation. Showing it changes no state; the sign-out is the antiforgery-protected POST.
/// </summary>
/// <remarks>
/// Anonymous so an already-lapsed session can still reach a clean sign-out.
/// </remarks>
[AllowAnonymous]
public sealed class SignOutModel : PageModel
{
    private readonly IDemoModeState _shellMode;

    /// <summary>
    /// Initializes a new sign-out confirmation model.
    /// </summary>
    /// <param name="shellMode">Effective shell mode.</param>
    public SignOutModel(IDemoModeState shellMode)
    {
        _shellMode = shellMode;
    }

    /// <summary>
    /// Whether this host should name its environment at all.
    /// </summary>
    public bool ShowEnvironmentMarker => _shellMode.ShowEnvironmentMarker;

    /// <summary>
    /// Current host environment name.
    /// </summary>
    public string EnvironmentName => _shellMode.EnvironmentName;

    /// <summary>
    /// Handles GET /sign-out.
    /// </summary>
    public void OnGet()
    {
    }
}
