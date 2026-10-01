using System.Security.Claims;

namespace SecureOps.Ui.Services;

/// <summary>
/// Reads the browser-authenticated principal, and issues one for the interim cookie sign-in path.
/// </summary>
public interface ISignedInUserService
{
    /// <summary>
    /// Describes the signed-in principal for presentation.
    /// </summary>
    /// <param name="principal">Authenticated principal.</param>
    /// <returns>Signed-in user description.</returns>
    public SignedInUser Describe(ClaimsPrincipal principal);

    /// <summary>
    /// Builds the principal issued by the interim cookie sign-in endpoint.
    /// </summary>
    /// <returns>Principal to sign in.</returns>
    /// <remarks>
    /// Exists only until an enterprise identity provider issues the session. It grants no application
    /// authority: every permission decision is made by the API from its own access store.
    /// </remarks>
    public ClaimsPrincipal CreateInterimPrincipal();
}
