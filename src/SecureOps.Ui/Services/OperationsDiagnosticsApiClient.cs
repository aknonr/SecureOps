using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Ui.Services;

/// <summary>Administrative read-only diagnostics through the browser-scoped authenticated session.</summary>
public sealed class OperationsDiagnosticsApiClient
{
    private readonly HttpClient _client;
    /// <summary>Uses the same protected per-browser transport as operational commands.</summary>
    public OperationsDiagnosticsApiClient(HttpClient client, IApiSessionContext session)
    { _client = client; ApiSessionHeaders.Attach(client, session); }
    /// <summary>Reads allowlisted effective configuration and local readiness observations.</summary>
    public async Task<OperationsReadiness> ReadAsync(CancellationToken token)
    {
        try
        {
            using HttpResponseMessage response = await _client.GetAsync("api/v1/diagnostics/operations", token);
            return await ApiResponseReader.ReadOrThrowAsync<OperationsReadiness>(response, token);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        { throw ApiResponseReader.ToTransportException(exception, token); }
    }
}
