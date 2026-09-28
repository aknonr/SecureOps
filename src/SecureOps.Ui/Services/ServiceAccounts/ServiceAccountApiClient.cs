using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>Downloaded file returned by the Service Accounts API.</summary>
/// <param name="FileName">Server-chosen file name.</param>
/// <param name="ContentType">Content type.</param>
/// <param name="Content">Bytes.</param>
public sealed record ServiceAccountFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Service Accounts HTTP client on the shared browser/API session. Every decision (capability, scope, version,
/// import classification) is made by the API; this client only transports and translates failures.
/// </summary>
public sealed class ServiceAccountApiClient
{
    private const string _root = "api/v1/service-accounts";
    private readonly HttpClient _client;

    /// <summary>Attaches the existing browser-scoped API session.</summary>
    public ServiceAccountApiClient(HttpClient client, IApiSessionContext session)
    {
        _client = client;
        ApiSessionHeaders.Attach(client, session);
    }

    /// <summary>Reads a module endpoint.</summary>
    public Task<T> GetAsync<T>(string path, CancellationToken token) => SendAsync<T>(HttpMethod.Get, path, null, token);

    /// <summary>Sends a JSON command; optional headers (for example Idempotency-Key) are added verbatim.</summary>
    public Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token,
        IReadOnlyDictionary<string, string>? headers = null) =>
        ExecuteAsync(() =>
        {
            HttpRequestMessage request = new(method, _root + path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: ApiResponseReader.JsonOptions);
            }

            foreach ((string name, string value) in headers ?? new Dictionary<string, string>())
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(request);
        }, ReadAsync<T>, token);

    /// <summary>Uploads one file with form fields (import staging or evidence).</summary>
    public Task<T> UploadAsync<T>(string path, string fileName, string contentType, byte[] content, IReadOnlyDictionary<string, string?> fields,
        CancellationToken token) =>
        ExecuteAsync(() =>
        {
            MultipartFormDataContent form = [];
            ByteArrayContent file = new(content);
            file.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? type) ? type
                : new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
            foreach ((string name, string? value) in fields)
            {
                if (value is not null)
                {
                    form.Add(new StringContent(value), name);
                }
            }

            return Task.FromResult(new HttpRequestMessage(HttpMethod.Post, _root + path) { Content = form });
        }, ReadAsync<T>, token);

    /// <summary>Downloads a file (report export or evidence).</summary>
    public Task<ServiceAccountFile> DownloadAsync(string path, CancellationToken token) =>
        ExecuteAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, _root + path)), async (response, cancellation) =>
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await FailureAsync(response, cancellation);
            }

            string name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "dosya";
            return new ServiceAccountFile(name, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                await response.Content.ReadAsByteArrayAsync(cancellation));
        }, token);

    private async Task<T> ExecuteAsync<T>(Func<Task<HttpRequestMessage>> build, Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken token)
    {
        try
        {
            using HttpRequestMessage request = await build();
            using HttpResponseMessage response = await _client.SendAsync(request, token);
            return await read(response, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            throw ApiResponseReader.ToTransportException(ex, token);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken token) =>
        response.IsSuccessStatusCode ? await ApiResponseReader.ReadBodyAsync<T>(response, token) : throw await FailureAsync(response, token);

    /// <summary>Keeps the module's single <c>field</c> extension so a page can point at the rejected input.</summary>
    private static async Task<SecureOpsApiException> FailureAsync(HttpResponseMessage response, CancellationToken token)
    {
        ProblemDetailsPayload? payload = null;
        try
        {
            string body = await response.Content.ReadAsStringAsync(token);
            if (!string.IsNullOrWhiteSpace(body))
            {
                payload = JsonSerializer.Deserialize<ProblemDetailsPayload>(body, ApiResponseReader.JsonOptions);
                using var document = JsonDocument.Parse(body);
                if (payload is not null && payload.Fields is null && document.RootElement.TryGetProperty("field", out JsonElement field)
                    && field.ValueKind == JsonValueKind.String)
                {
                    payload.Fields = [field.GetString()!];
                }
            }
        }
        catch (JsonException)
        {
            // A non-JSON error body (proxy HTML, empty response) still classifies by status code.
        }

        return new SecureOpsApiException(UiProblemFactory.FromResponse((int)response.StatusCode, payload));
    }
}
