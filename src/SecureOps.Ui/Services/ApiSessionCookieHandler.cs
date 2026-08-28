using System.Net;
using Microsoft.Extensions.Logging;

namespace SecureOps.Ui.Services;

/// <summary>
/// Replays one browser session's API cookies across every typed API client.
/// </summary>
/// <remarks>
/// The API's application session is cookie-based. Left to itself, each typed client would keep its
/// own cookie container, and the container is pooled per client name rather than per browser — so
/// one operator's refresh would create session after session while two operators could end up
/// sharing one container. Both are wrong, and the second is a security defect.
/// <para>
/// This handler makes the correlation explicit instead. Cookie handling is switched off on the
/// primary handler, and the jar is chosen from the browser-session correlation value that the typed
/// client stamped on the request. Requests with no correlation value are still sent — unauthenticated
/// and startup calls are legitimate — they simply do not share a jar.
/// </para>
/// </remarks>
public sealed class ApiSessionCookieHandler : DelegatingHandler
{
    /// <summary>
    /// How long a request will wait for the first-request gate before proceeding without it.
    /// </summary>
    /// <remarks>
    /// A stalled first request must not freeze every other tab. Proceeding ungated risks one extra
    /// application session; blocking indefinitely would risk an unusable UI, which is worse.
    /// </remarks>
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(10);

    private readonly IApiSessionStore _store;
    private readonly ILogger<ApiSessionCookieHandler> _logger;

    /// <summary>
    /// Initializes a new API session cookie handler.
    /// </summary>
    /// <param name="store">Per-browser cookie jars.</param>
    /// <param name="logger">Logger. Never receives cookie values.</param>
    public ApiSessionCookieHandler(IApiSessionStore store, ILogger<ApiSessionCookieHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Removed before the request leaves the process. The correlation value is internal routing
        // information for this handler and has no meaning to the API.
        string? browserSessionKey = ApiSessionHeaders.TakeBrowserSessionKey(request);

        if (browserSessionKey is null || request.RequestUri is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        BrowserApiSession session = _store.GetOrCreate(browserSessionKey);
        Uri uri = request.RequestUri;

        if (!string.IsNullOrEmpty(session.Cookies.GetCookieHeader(uri)))
        {
            return await SendWithJarAsync(request, session, uri, cancellationToken);
        }

        // No cookie yet: serialize concurrent first requests so simultaneous tabs establish one
        // application session between them rather than one each.
        bool gated = await session.FirstRequestGate.WaitAsync(GateWait, cancellationToken);

        try
        {
            return await SendWithJarAsync(request, session, uri, cancellationToken);
        }
        finally
        {
            if (gated)
            {
                session.FirstRequestGate.Release();
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithJarAsync(
        HttpRequestMessage request,
        BrowserApiSession session,
        Uri uri,
        CancellationToken cancellationToken)
    {
        string cookieHeader = session.Cookies.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.Remove(HeaderNames.Cookie);
            request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookieHeader);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        Capture(session, uri, response);
        return response;
    }

    private void Capture(BrowserApiSession session, Uri uri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(HeaderNames.SetCookie, out IEnumerable<string>? setCookies))
        {
            return;
        }

        foreach (string setCookie in setCookies)
        {
            try
            {
                session.Cookies.SetCookies(uri, setCookie);
            }
            catch (CookieException ex)
            {
                // The cookie name is safe to record; the value is not, and SetCookies never gives us
                // one without the other, so nothing from the header is logged.
                _logger.LogWarning(ex, "Rejected a malformed Set-Cookie header from the SecureOps API.");
            }
        }
    }

    private static class HeaderNames
    {
        internal const string Cookie = "Cookie";
        internal const string SetCookie = "Set-Cookie";
    }
}
