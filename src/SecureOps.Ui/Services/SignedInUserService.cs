using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using SecureOps.Ui.Configuration;

namespace SecureOps.Ui.Services;

/// <summary>
/// Claims-based implementation of <see cref="ISignedInUserService"/>.
/// </summary>
public sealed class SignedInUserService : ISignedInUserService
{
    /// <summary>Claim carrying how the session was established.</summary>
    public const string AuthenticationSourceClaim = "secureops:auth_source";

    /// <summary>Claim carrying a display name when the provider supplies one.</summary>
    public const string DisplayNameClaim = "secureops:display_name";

    /// <summary>Authentication source value used by the interim cookie sign-in path.</summary>
    public const string InterimAuthenticationSource = "interim-cookie";

    private readonly DemoModeOptions _options;

    /// <summary>
    /// Initializes a new signed-in user service.
    /// </summary>
    /// <param name="options">Shell options supplying the interim account name.</param>
    public SignedInUserService(IOptions<DemoModeOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public SignedInUser Describe(ClaimsPrincipal principal)
    {
        string accountName = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.Identity?.Name
            ?? "bilinmiyor";

        // A provider-supplied display name wins; otherwise the account name stands in. No placeholder
        // person is invented, so nothing on screen claims to be a real name that is not.
        string displayName = principal.FindFirstValue(DisplayNameClaim) ?? accountName;

        string authenticationSource = principal.FindFirstValue(AuthenticationSourceClaim)
            ?? principal.Identity?.AuthenticationType
            ?? "bilinmiyor";

        return new SignedInUser(accountName, displayName, authenticationSource);
    }

    /// <inheritdoc />
    public ClaimsPrincipal CreateInterimPrincipal()
    {
        // The account name mirrors the API demo actor so the header and the API's audit trail refer to
        // the same actor. Without this they would disagree and the account page would be misleading.
        string accountName = string.IsNullOrWhiteSpace(_options.ApiDemoActor)
            ? "secureops.operator"
            : _options.ApiDemoActor;

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, accountName),
            new(ClaimTypes.Name, accountName),
            new(AuthenticationSourceClaim, InterimAuthenticationSource)
        ];

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }
}
