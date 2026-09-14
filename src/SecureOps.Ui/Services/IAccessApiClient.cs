using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the application access endpoints in
/// <c>docs/contracts/secureops-api-v1-ui-integration.md</c>.
/// </summary>
/// <remarks>
/// Application access is resolved by the API independently of how the browser authenticated, so the
/// UI must read status, roles, and capabilities from here rather than inferring them from cookie
/// claims. Server authorization stays authoritative; these values only drive presentation.
/// </remarks>
public interface IAccessApiClient
{
    /// <summary>
    /// Gets the caller's current application access projection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Access status, roles, capabilities, and session policy.</returns>
    public Task<CurrentAccessResponse> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records logout intent with the API.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral logout result.</returns>
    public Task<LogoutResponse> LogoutAsync(CancellationToken cancellationToken);
}
