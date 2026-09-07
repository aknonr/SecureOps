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
    /// <param name="sessionContext">Browser session whose API cookies these calls belong to.</param>
    public OperationalRecordApiClient(HttpClient httpClient, IApiSessionContext sessionContext)
    {
        _httpClient = httpClient;
        ApiSessionHeaders.Attach(httpClient, sessionContext);
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
    public Task<JiraPreviewResponse> ReviewAsync(Guid id, JiraReviewRequest request, CancellationToken cancellationToken) =>
        SendAsync<JiraPreviewResponse>(() => _httpClient.PostAsJsonAsync($"{Root}/{id}/jira-review", request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<JiraTransferResponse> CreateJiraAsync(Guid id, CancellationToken cancellationToken) =>
        // No Idempotency-Key header on purpose — see IOperationalRecordApiClient.
        SendPublicationAsync(
            () => _httpClient.PostAsync($"{Root}/{id}/jira", content: null, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<JiraTransferResponse> RetryAsync(Guid id, CancellationToken cancellationToken) =>
        SendPublicationAsync(
            () => _httpClient.PostAsync($"{Root}/{id}/retry", content: null, cancellationToken),
            cancellationToken);

    private static async Task<JiraTransferResponse> SendPublicationAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<JiraTransferResponse>(send, cancellationToken);
        }
        catch (SecureOpsApiException ex) when (ex.Problem.Kind is UiProblemKind.Network or UiProblemKind.Timeout or UiProblemKind.Unexpected
            || (ex.Problem.StatusCode >= 500 && ex.Problem.Stage is not
                ("source-validation" or "requester-resolution" or "operator-reporter-resolution" or "jira-create" or "source-close")))
        {
            throw new SecureOpsApiException(UiProblemFactory.UncertainPublication(ex.Problem));
        }
    }

    private static async Task<T> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await send();
            return await ApiResponseReader.ReadOrThrowAsync<T>(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }
    }
}
