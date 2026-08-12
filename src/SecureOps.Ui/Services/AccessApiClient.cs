using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the application access API client.
/// </summary>
public sealed class AccessApiClient : IAccessApiClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new access API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    public AccessApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<CurrentAccessResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync("api/v1/access/me", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<CurrentAccessResponse>(response, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<LogoutResponse> LogoutAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsync("api/v1/access/logout", content: null, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<LogoutResponse>(response, cancellationToken);
        }
    }
}
