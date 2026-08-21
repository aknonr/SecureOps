using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the operational-record API client.
/// </summary>
public sealed class OperationalRecordApiClient : IOperationalRecordApiClient
{
    private const string Root = "api/v1/operational-records";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new operational-record API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    public OperationalRecordApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationalRecordResponse>> ListAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<OperationalRecordResponse>>(
            () => _httpClient.GetAsync(Root, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationalRecordResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync<OperationalRecordResponse>(
            () => _httpClient.GetAsync($"{Root}/{id}", cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<JiraPreviewResponse> PreviewAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync<JiraPreviewResponse>(
            () => _httpClient.PostAsync($"{Root}/{id}/jira-preview", content: null, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<JiraTransferResponse> CreateJiraAsync(Guid id, CancellationToken cancellationToken) =>
        // No Idempotency-Key header on purpose — see IOperationalRecordApiClient.
        SendAsync<JiraTransferResponse>(
            () => _httpClient.PostAsync($"{Root}/{id}/jira", content: null, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<JiraTransferResponse> RetryAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync<JiraTransferResponse>(
            () => _httpClient.PostAsync($"{Root}/{id}/retry", content: null, cancellationToken),
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
