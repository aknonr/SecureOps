using System.Net.Http.Json;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the access administration API client.
/// </summary>
public sealed class AccessAdminApiClient : IAccessAdminApiClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new access administration API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    /// <param name="sessionContext">Browser session whose API cookies these calls belong to.</param>
    public AccessAdminApiClient(HttpClient httpClient, IApiSessionContext sessionContext)
    {
        _httpClient = httpClient;
        ApiSessionHeaders.Attach(httpClient, sessionContext);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccessUserResponse>> GetUsersAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AccessUserResponse>>("api/v1/access/users", cancellationToken);

    /// <inheritdoc />
    public Task<AccessUserResponse> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        GetAsync<AccessUserResponse>($"api/v1/access/users/{userId}", cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AccessRequestResponse>> GetRequestsAsync(
        string? status,
        CancellationToken cancellationToken)
    {
        // The status value is a fixed set chosen by the UI, never free text. An unrecognised value
        // is rejected by the API as AccessValidationFailed.
        string route = string.IsNullOrWhiteSpace(status)
            ? "api/v1/access/requests"
            : $"api/v1/access/requests?status={Uri.EscapeDataString(status)}";

        return GetAsync<IReadOnlyList<AccessRequestResponse>>(route, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AccessRequestResponse> ApproveAsync(
        Guid requestId,
        string reason,
        IReadOnlyList<string> roles,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        DecideAsync(requestId, "approve", new AccessDecisionRequest(reason, roles, expectedVersion), cancellationToken);

    /// <inheritdoc />
    public Task<AccessRequestResponse> RejectAsync(
        Guid requestId,
        string reason,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        // Roles are meaningless for a rejection; the contract models that as null rather than empty.
        DecideAsync(
            requestId,
            "reject",
            new AccessDecisionRequest(reason, Roles: null, expectedVersion),
            cancellationToken);

    /// <inheritdoc />
    public async Task<CurrentAccessResponse> ReplaceRolesAsync(
        Guid userId,
        IReadOnlyList<string> roles,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendAsync(
            () => _httpClient.PutAsJsonAsync(
                $"api/v1/access/users/{userId}/roles",
                new AssignRolesRequest(roles, expectedVersion),
                cancellationToken),
            cancellationToken);

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<CurrentAccessResponse>(response, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<CurrentAccessResponse> DisableAsync(
        Guid userId,
        string reason,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendAsync(
            () => _httpClient.PostAsJsonAsync(
                $"api/v1/access/users/{userId}/disable",
                new DisableAccessRequest(reason, expectedVersion),
                cancellationToken),
            cancellationToken);

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<CurrentAccessResponse>(response, cancellationToken);
        }
    }

    private async Task<T> GetAsync<T>(string route, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendAsync(
            () => _httpClient.GetAsync(route, cancellationToken),
            cancellationToken);

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<T>(response, cancellationToken);
        }
    }

    private async Task<AccessRequestResponse> DecideAsync(
        Guid requestId,
        string decision,
        AccessDecisionRequest body,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendAsync(
            () => _httpClient.PostAsJsonAsync(
                $"api/v1/access/requests/{requestId}/{decision}",
                body,
                cancellationToken),
            cancellationToken);

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<AccessRequestResponse>(response, cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }
    }
}
