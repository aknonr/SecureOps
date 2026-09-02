using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SecureOps.Shared.Configuration;

namespace SecureOps.Ui.Services;

/// <summary>
/// Server-side cookie jars for API calls, one per browser authentication session.
/// </summary>
/// <remarks>
/// This is the whole reason a browser refresh no longer creates a second application session. Every
/// typed API client shares these jars instead of owning a private cookie container, so the
/// <c>__Host-SecureOps.ApplicationSession</c> handle the API issued to a browser is replayed by all
/// of that browser's traffic — and by none of anyone else's.
/// <para>
/// The jars live only in this process's memory. Nothing here is sent to the browser, written to a
/// log, or surfaced in UI state; the handle is a credential, and the UI's job is to carry it, not to
/// show it.
/// </para>
/// </remarks>
public interface IApiSessionStore
{
    /// <summary>
    /// Returns the jar for a browser session, creating it on first use.
    /// </summary>
    /// <param name="browserSessionKey">Browser-session correlation value.</param>
    /// <returns>The jar owned by that browser session.</returns>
    public BrowserApiSession GetOrCreate(string browserSessionKey);

    /// <summary>
    /// Discards a browser session's jar.
    /// </summary>
    /// <param name="browserSessionKey">Browser-session correlation value.</param>
    /// <remarks>
    /// Called after API logout. The API has already ended the session server-side; dropping the jar
    /// makes sure a later request cannot replay a handle that is no longer valid.
    /// </remarks>
    public void Remove(string browserSessionKey);
}

/// <summary>
/// One browser session's API cookie jar and its first-request gate.
/// </summary>
public sealed class BrowserApiSession : IDisposable
{
    private readonly object _tokenLock = new();
    private OidcServerTokenSet? _oidcTokens;

    /// <summary>Cookies the API has issued to this browser session.</summary>
    public CookieContainer Cookies { get; } = new();

    /// <summary>Returns API cookies applicable to the outbound request.</summary>
    /// <remarks>
    /// Secure cookies are scoped against an HTTPS origin even when the server-to-server hop uses a
    /// loopback HTTP binding. The handle never leaves the host in that topology. Remote cleartext
    /// endpoints are rejected by startup validation rather than having Secure semantics bypassed.
    /// </remarks>
    public string GetApiCookieHeader(Uri requestUri) => Cookies.GetCookieHeader(CookieOrigin(requestUri));

    /// <summary>Applies an API Set-Cookie header to this browser session's server-side jar.</summary>
    public void SetApiCookies(Uri requestUri, string setCookie) =>
        Cookies.SetCookies(CookieOrigin(requestUri), setCookie);

    /// <summary>
    /// Serializes requests made before this browser session has an application-session cookie.
    /// </summary>
    /// <remarks>
    /// Without it, two tabs opened at the same moment both arrive with an empty jar and the API
    /// issues each one its own session — leaving an orphan visible on the Active Sessions page. The
    /// gate is only contended on the first request; once a cookie exists, callers skip it.
    /// </remarks>
    public SemaphoreSlim FirstRequestGate { get; } = new(1, 1);

    /// <summary>Serializes refresh-token redemption for this browser authentication session.</summary>
    public SemaphoreSlim TokenRefreshGate { get; } = new(1, 1);

    /// <summary>Stores a validated OIDC access token only in server process memory.</summary>
    public void SetOidcAccessToken(string token, DateTimeOffset expiresAtUtc) =>
        SetOidcTokens(new OidcServerTokenSet(token, expiresAtUtc, null, null, null, null));

    /// <summary>Atomically replaces all server-held OIDC token material.</summary>
    public void SetOidcTokens(OidcServerTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        lock (_tokenLock)
        {
            _oidcTokens = tokens;
        }
    }

    /// <summary>Returns a non-expired access token without logging or exposing it to browser state.</summary>
    public string? GetOidcAccessToken(DateTimeOffset now)
    {
        lock (_tokenLock)
        {
            return _oidcTokens is { AccessTokenExpiresAtUtc: var expiry } current && expiry > now
                ? current.AccessToken
                : null;
        }
    }

    /// <summary>Returns the server-held ID token for a standards-based logout hint.</summary>
    public string? GetOidcIdToken()
    {
        lock (_tokenLock)
        {
            return _oidcTokens?.IdToken;
        }
    }

    /// <summary>Returns or refreshes the server-held access token once for this operation.</summary>
    public async Task<OidcAccessTokenResult> GetOidcAccessTokenAsync(
        DateTimeOffset now,
        OidcOptions options,
        IOidcBackchannelClient backchannel,
        CancellationToken cancellationToken)
    {
        OidcServerTokenSet? current = Snapshot();
        if (IsUsable(current, now, options.AccessTokenRefreshSkewSeconds))
        {
            return new OidcAccessTokenResult(current!.AccessToken, false);
        }

        if (!CanRefresh(current, now))
        {
            ClearTokens();
            return new OidcAccessTokenResult(null, true);
        }

        await TokenRefreshGate.WaitAsync(cancellationToken);
        try
        {
            current = Snapshot();
            if (IsUsable(current, now, options.AccessTokenRefreshSkewSeconds))
            {
                return new OidcAccessTokenResult(current!.AccessToken, false);
            }

            if (!CanRefresh(current, now))
            {
                ClearTokens();
                return new OidcAccessTokenResult(null, true);
            }

            Dictionary<string, string> parameters = new(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = options.ClientId,
                ["refresh_token"] = current!.RefreshToken!
            };
            if (string.Equals(options.ClientAuthenticationMethod, "ClientSecretPost", StringComparison.OrdinalIgnoreCase))
            {
                parameters["client_secret"] = options.ClientSecret;
            }

            OpenIdConnectMessage response = await backchannel.PostTokenAsync(
                current.TokenEndpoint!,
                parameters,
                cancellationToken);
            if (!TryCreateReplacement(response, current, now, options, out OidcServerTokenSet? replacement))
            {
                ClearTokens();
                return new OidcAccessTokenResult(null, true);
            }

            SetOidcTokens(replacement!);
            return new OidcAccessTokenResult(replacement!.AccessToken, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ClearTokens();
            return new OidcAccessTokenResult(null, true);
        }
        finally
        {
            TokenRefreshGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ClearTokens();
        FirstRequestGate.Dispose();
        TokenRefreshGate.Dispose();
    }

    private static Uri CookieOrigin(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        if (string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return requestUri;
        }

        if (string.Equals(requestUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && requestUri.IsLoopback)
        {
            return new UriBuilder(requestUri) { Scheme = Uri.UriSchemeHttps }.Uri;
        }

        throw new InvalidOperationException(
            "SecureOps API session transport requires HTTPS or a loopback HTTP endpoint.");
    }

    private OidcServerTokenSet? Snapshot()
    {
        lock (_tokenLock)
        {
            return _oidcTokens;
        }
    }

    private void ClearTokens()
    {
        lock (_tokenLock)
        {
            _oidcTokens = null;
        }
    }

    private static bool IsUsable(OidcServerTokenSet? current, DateTimeOffset now, int skewSeconds) =>
        current is not null && current.AccessTokenExpiresAtUtc > now.AddSeconds(skewSeconds);

    private static bool CanRefresh(OidcServerTokenSet? current, DateTimeOffset now) =>
        current is { RefreshToken: not null, RefreshTokenExpiresAtUtc: { } refreshExpiry, TokenEndpoint: not null }
        && refreshExpiry > now;

    private static bool TryCreateReplacement(
        OpenIdConnectMessage response,
        OidcServerTokenSet current,
        DateTimeOffset now,
        OidcOptions options,
        out OidcServerTokenSet? replacement)
    {
        replacement = null;
        if (string.IsNullOrWhiteSpace(response.AccessToken)
            || response.AccessToken.Length > options.MaxAccessTokenLength
            || !int.TryParse(response.ExpiresIn, out int expiresIn)
            || expiresIn is < 30 or > 86_400)
        {
            return false;
        }

        string refreshToken = string.IsNullOrWhiteSpace(response.RefreshToken)
            ? current.RefreshToken!
            : response.RefreshToken;
        string? idToken = current.IdToken;
        if (refreshToken.Length > options.MaxServerTokenLength || idToken?.Length > options.MaxServerTokenLength)
        {
            return false;
        }

        replacement = new OidcServerTokenSet(
            response.AccessToken,
            now.AddSeconds(expiresIn),
            refreshToken,
            current.RefreshTokenExpiresAtUtc,
            idToken,
            current.TokenEndpoint);
        return true;
    }
}

/// <summary>All OIDC token material retained only in server process memory.</summary>
public sealed record OidcServerTokenSet(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAtUtc,
    string? IdToken,
    Uri? TokenEndpoint);

/// <summary>Result of one server-side access-token operation.</summary>
public sealed record OidcAccessTokenResult(string? AccessToken, bool RequiresReauthentication);

/// <summary>
/// Memory-cache backed <see cref="IApiSessionStore"/>.
/// </summary>
/// <remarks>
/// Entries expire on a sliding window so an abandoned browser session does not hold memory forever.
/// The window is deliberately longer than the API's own idle timeout: expiring a jar early would
/// hand the next request an empty container and create exactly the duplicate session this store
/// exists to prevent. The API remains the authority on when a session actually ends.
/// </remarks>
public sealed class ApiSessionStore : IApiSessionStore
{
    /// <summary>Sliding lifetime of an idle jar.</summary>
    public static readonly TimeSpan IdleRetention = TimeSpan.FromHours(13);

    private readonly IMemoryCache _cache;
    private readonly object _cacheLock = new();

    /// <summary>
    /// Initializes a new API session store.
    /// </summary>
    /// <param name="cache">Backing memory cache.</param>
    public ApiSessionStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public BrowserApiSession GetOrCreate(string browserSessionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browserSessionKey);

        lock (_cacheLock)
        {
            return _cache.GetOrCreate(CacheKey(browserSessionKey), entry =>
            {
                entry.SlidingExpiration = IdleRetention;
                entry.RegisterPostEvictionCallback(static (_, value, _, _) => (value as BrowserApiSession)?.Dispose());
                return new BrowserApiSession();
            })!;
        }
    }

    /// <inheritdoc />
    public void Remove(string browserSessionKey)
    {
        if (!string.IsNullOrWhiteSpace(browserSessionKey))
        {
            lock (_cacheLock)
            {
                _cache.Remove(CacheKey(browserSessionKey));
            }
        }
    }

    private static string CacheKey(string browserSessionKey) => $"api-session:{browserSessionKey}";
}
