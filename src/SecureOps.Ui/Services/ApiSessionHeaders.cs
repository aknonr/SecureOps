namespace SecureOps.Ui.Services;

/// <summary>
/// Carries the browser-session correlation value from a typed API client to
/// <see cref="ApiSessionCookieHandler"/>.
/// </summary>
/// <remarks>
/// A typed client is constructed in the caller's DI scope, so it can see the browser session. The
/// message handler cannot: <c>IHttpClientFactory</c> builds handlers in their own scope and pools
/// them across users, and inside a Blazor circuit there is no <c>HttpContext</c> to fall back on.
/// Stamping the request is what bridges the two, and the handler strips the stamp before the request
/// is sent.
/// </remarks>
public static class ApiSessionHeaders
{
    /// <summary>Internal request header carrying the browser-session correlation value.</summary>
    /// <remarks>Never sent to the API. <see cref="TakeBrowserSessionKey"/> removes it first.</remarks>
    public const string BrowserSession = "X-SecureOps-Ui-Browser-Session";

    /// <summary>
    /// Stamps a typed client's <see cref="HttpClient"/> with the caller's browser session.
    /// </summary>
    /// <param name="httpClient">Client instance owned by the calling scope.</param>
    /// <param name="context">Browser-session correlation for the current circuit or request.</param>
    /// <remarks>
    /// Safe as a default header because <c>IHttpClientFactory</c> hands each typed client its own
    /// <see cref="HttpClient"/> instance; only the underlying handler chain is shared.
    /// </remarks>
    public static void Attach(HttpClient httpClient, IApiSessionContext context)
    {
        string? key = context.BrowserSessionKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        httpClient.DefaultRequestHeaders.Remove(BrowserSession);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(BrowserSession, key);
    }

    /// <summary>
    /// Reads and removes the correlation stamp from an outbound request.
    /// </summary>
    /// <param name="request">Outbound request.</param>
    /// <returns>The correlation value, or <see langword="null"/> when the request carries none.</returns>
    public static string? TakeBrowserSessionKey(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues(BrowserSession, out IEnumerable<string>? values))
        {
            return null;
        }

        string? key = values.FirstOrDefault();
        request.Headers.Remove(BrowserSession);

        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}
