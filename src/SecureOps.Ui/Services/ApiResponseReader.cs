using System.Net.Http.Json;
using System.Text.Json;

namespace SecureOps.Ui.Services;

/// <summary>
/// Shared response handling for SecureOps API clients: success bodies are deserialized, failures are
/// translated into <see cref="SecureOpsApiException"/> carrying a ready-to-render <see cref="UiProblem"/>.
/// </summary>
/// <remarks>
/// Centralizing this keeps every client on one error contract, so adding an endpoint does not mean
/// re-implementing status handling. Transport failures and timeouts are classified here too, because
/// from the operator's point of view "the service did not answer" is just another failure state.
/// </remarks>
public static class ApiResponseReader
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Shared JSON options matching the API's web defaults.
    /// </summary>
    public static JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Reads a successful response body, or throws a translated API exception.
    /// </summary>
    /// <typeparam name="T">Expected response contract.</typeparam>
    /// <param name="response">HTTP response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response body.</returns>
    /// <exception cref="SecureOpsApiException">Thrown for any non-success status or empty body.</exception>
    public static async Task<T> ReadOrThrowAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }

        return await ReadBodyAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Reads a response body that the caller has already accepted as a valid outcome.
    /// </summary>
    /// <typeparam name="T">Expected response contract.</typeparam>
    /// <param name="response">HTTP response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response body.</returns>
    /// <exception cref="SecureOpsApiException">Thrown when the body is missing or unparsable.</exception>
    public static async Task<T> ReadBodyAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            T? result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }
        catch (JsonException)
        {
            // Fall through to the shared unexpected-response problem below.
        }

        throw new SecureOpsApiException(UiProblemFactory.FromResponse((int)response.StatusCode, null) with
        {
            Kind = UiProblemKind.Unexpected,
            Code = "EmptyApiResponse",
            Title = "Servis beklenmeyen bir yanıt döndürdü",
            Explanation = "Yanıt işlenemedi, bu nedenle ekranda gösterilmedi."
        });
    }

    /// <summary>
    /// Translates a failed response into an exception, reading the ProblemDetails body when present.
    /// </summary>
    /// <param name="response">Failed HTTP response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exception carrying the translated problem.</returns>
    public static async Task<SecureOpsApiException> ToExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ProblemDetailsPayload? payload = null;

        try
        {
            payload = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>(_jsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A non-JSON error body (proxy HTML, empty response) still classifies by status code.
        }

        return new SecureOpsApiException(UiProblemFactory.FromResponse((int)response.StatusCode, payload));
    }

    /// <summary>
    /// Classifies a transport-layer failure raised while sending a request.
    /// </summary>
    /// <param name="exception">Exception raised by <see cref="HttpClient"/>.</param>
    /// <param name="cancellationToken">Token the caller passed, used to distinguish a real cancellation.</param>
    /// <returns>Exception carrying the translated problem.</returns>
    public static SecureOpsApiException ToTransportException(
        Exception exception,
        CancellationToken cancellationToken)
    {
        // HttpClient surfaces its own timeout as TaskCanceledException, which is indistinguishable from
        // caller cancellation except by inspecting the caller's token.
        bool isTimeout = exception is TaskCanceledException or OperationCanceledException
            && !cancellationToken.IsCancellationRequested;

        UiProblem problem = isTimeout
            ? UiProblemFactory.TimedOut()
            : UiProblemFactory.NetworkFailure();

        return new SecureOpsApiException(problem, exception);
    }
}
