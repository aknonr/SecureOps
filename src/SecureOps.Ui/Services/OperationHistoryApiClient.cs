using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Ui.Services;

/// <summary>Record-scoped immutable actor/outcome evidence through the normal authenticated transport.</summary>
public sealed class OperationHistoryApiClient
{
    private readonly HttpClient _client;
    /// <summary>Uses this browser's API session only.</summary>
    public OperationHistoryApiClient(HttpClient client, IApiSessionContext session) { _client = client; ApiSessionHeaders.Attach(client, session); }
    /// <summary>Read only; cannot execute or reconcile a remote effect.</summary>
    public async Task<OperationHistoryResponse> GetAsync(string recordType, Guid id, CancellationToken token)
    {
        try
        {
            using HttpResponseMessage response = await _client.GetAsync($"api/v1/operations/{Uri.EscapeDataString(recordType)}/{id}/events", token);
            if (!response.IsSuccessStatusCode)
            { throw await ApiResponseReader.ToExceptionAsync(response, token); }
            return await ApiResponseReader.ReadBodyAsync<OperationHistoryResponse>(response, token);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        { throw ApiResponseReader.ToTransportException(exception, token); }
    }
}
