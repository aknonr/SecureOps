using System.Net.Http.Json;
using SecureOps.Shared.Contracts.Identity;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the identity lookup API client.
/// </summary>
public sealed class IdentityLookupApiClient : IIdentityLookupApiClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new identity lookup API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    /// <param name="sessionContext">Browser session whose API cookies these calls belong to.</param>
    public IdentityLookupApiClient(HttpClient httpClient, IApiSessionContext sessionContext)
    {
        _httpClient = httpClient;
        ApiSessionHeaders.Attach(httpClient, sessionContext);
    }

    /// <inheritdoc />
    public Task<CurrentIdentityResponse> GetCurrentIdentityAsync(CancellationToken cancellationToken) =>
        GetAsync<CurrentIdentityResponse>("api/v1/identity/me", cancellationToken);

    /// <inheritdoc />
    public Task<IdentityLookupCapabilitiesResponse> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        GetAsync<IdentityLookupCapabilitiesResponse>("api/v1/identity/lookup/capabilities", cancellationToken);

    /// <inheritdoc />
    public Task<IdentityProviderHealthResponse> GetProviderHealthAsync(CancellationToken cancellationToken) =>
        GetAsync<IdentityProviderHealthResponse>("api/v1/health/identity-provider", cancellationToken);

    /// <inheritdoc />
    public async Task<DirectoryNameSearchResponse> NameSearchAsync(DirectoryNameSearchRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/v1/identity/name-search", request, ApiResponseReader.JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(exception, cancellationToken);
        }

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<DirectoryNameSearchResponse>(response, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IdentityLookupResponse> LookupAsync(
        IdentityLookupRequest request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "api/v1/identity/lookup",
                request,
                ApiResponseReader.JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }

        using (response)
        {
            // A not-found identity is reported as ProblemDetails (404, code IdentityNotFound), so it
            // flows through the shared failure path and reaches the page as a UiProblem.
            return await ApiResponseReader.ReadOrThrowAsync<IdentityLookupResponse>(response, cancellationToken);
        }
    }

    private async Task<T> GetAsync<T>(string route, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync(route, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<T>(response, cancellationToken);
        }
    }
}
