using System.Globalization;
using System.Net.Http.Json;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Shared.Contracts.Sessions;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the application-session and integration-diagnostics client.
/// </summary>
public sealed class SessionApiClient : ISessionApiClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new session API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    /// <param name="sessionContext">Browser session whose API cookies these calls belong to.</param>
    public SessionApiClient(HttpClient httpClient, IApiSessionContext sessionContext)
    {
        _httpClient = httpClient;
        ApiSessionHeaders.Attach(httpClient, sessionContext);
    }

    /// <inheritdoc />
    public Task<ApplicationSessionResponse> GetCurrentAsync(CancellationToken cancellationToken) =>
        SendAsync<ApplicationSessionResponse>(
            () => _httpClient.GetAsync("api/v1/sessions/current", cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ActiveApplicationSessionsResponse> GetActiveAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        SendAsync<ActiveApplicationSessionsResponse>(
            () => _httpClient.GetAsync(
                $"api/v1/sessions/active?page={page.ToString(CultureInfo.InvariantCulture)}"
                    + $"&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}",
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ApplicationSessionEndedResponse> RevokeAsync(
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken) =>
        SendAsync<ApplicationSessionEndedResponse>(
            () => _httpClient.PostAsJsonAsync(
                "api/v1/sessions/revoke",
                new RevokeApplicationSessionRequest(sessionId, reason),
                ApiResponseReader.JsonOptions,
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<EnterpriseIntegrationHealthResponse> GetIntegrationHealthAsync(
        CancellationToken cancellationToken) =>
        SendAsync<EnterpriseIntegrationHealthResponse>(
            () => _httpClient.GetAsync("api/v1/health/enterprise-integrations", cancellationToken),
            cancellationToken);

    private static async Task<T> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await send();
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
