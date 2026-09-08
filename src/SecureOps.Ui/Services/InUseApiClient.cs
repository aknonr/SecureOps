using System.Net.Http.Json;

namespace SecureOps.Ui.Services;

/// <summary>In Use HTTP client using the shared authenticated browser/API session and error reader.</summary>
public sealed class InUseApiClient
{
    private readonly HttpClient _client;
    /// <summary>Attaches the existing browser-scoped API session.</summary>
    public InUseApiClient(HttpClient client, IApiSessionContext session)
    { _client = client; ApiSessionHeaders.Attach(client, session); }

    /// <summary>Reads a local In Use endpoint.</summary>
    public Task<T> GetAsync<T>(string path, CancellationToken token) => SendAsync<T>(HttpMethod.Get, path, null, token);
    /// <summary>Sends an explicit local command or read-only refresh.</summary>
    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token)
    {
        try
        {
            using HttpRequestMessage request = new(method, "api/v1/in-use" + path);
            if (body is not null)
            { request.Content = JsonContent.Create(body, options: ApiResponseReader.JsonOptions); }
            using HttpResponseMessage response = await _client.SendAsync(request, token);
            return await ApiResponseReader.ReadOrThrowAsync<T>(response, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        { throw ApiResponseReader.ToTransportException(ex, token); }
    }
}
