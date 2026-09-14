using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the identity lookup endpoints in <c>docs/contracts/secureops-api-v1-ui-integration.md</c>.
/// </summary>
public interface IIdentityLookupApiClient
{
    /// <summary>
    /// Gets safe metadata about the current API caller.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current caller metadata.</returns>
    public Task<CurrentIdentityResponse> GetCurrentIdentityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the server's declared lookup validation limits and returned fields.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lookup capabilities.</returns>
    public Task<IdentityLookupCapabilitiesResponse> GetCapabilitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets safe identity provider health metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider health.</returns>
    public Task<IdentityProviderHealthResponse> GetProviderHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Looks up one exact account.
    /// </summary>
    /// <param name="request">Lookup request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lookup response when an identity was found.</returns>
    /// <exception cref="SecureOpsApiException">
    /// Thrown for every non-success outcome, including a not-found result, which the API reports as
    /// ProblemDetails with code <c>IdentityNotFound</c> rather than a response body.
    /// </exception>
    public Task<IdentityLookupResponse> LookupAsync(IdentityLookupRequest request, CancellationToken cancellationToken);
}
