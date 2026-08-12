using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureOps.Ui.Services;

namespace SecureOps.Ui.Pages;

/// <summary>
/// Confirmation shown after a completed sign-out.
/// </summary>
/// <remarks>
/// Anonymous by necessity: the session is already gone by the time this renders. Server-rendered so it
/// still appears when no Blazor circuit can be established.
/// </remarks>
[AllowAnonymous]
public sealed class SignedOutModel : PageModel
{
    private readonly IDemoModeState _shellMode;

    /// <summary>
    /// Initializes a new signed-out page model.
    /// </summary>
    /// <param name="shellMode">Effective shell mode.</param>
    public SignedOutModel(IDemoModeState shellMode)
    {
        _shellMode = shellMode;
    }

    /// <summary>
    /// Current host environment name.
    /// </summary>
    public string EnvironmentName => _shellMode.EnvironmentName;

    /// <summary>
    /// Whether the underlying session is managed by an external provider.
    /// </summary>
    /// <remarks>
    /// The API reports logout as provider-managed intent: SecureOps clears its own session, but the
    /// identity provider's session is owned by the browser. Saying so prevents an operator from
    /// assuming they are fully signed out of the enterprise identity everywhere.
    /// </remarks>
    public bool ProviderManaged { get; private set; }

    /// <summary>
    /// Handles GET /signed-out.
    /// </summary>
    /// <param name="provider">Optional provider-managed marker set by the sign-out endpoint.</param>
    public void OnGet([FromQuery] string? provider)
    {
        ProviderManaged = string.Equals(provider, "managed", StringComparison.OrdinalIgnoreCase);
    }
}
