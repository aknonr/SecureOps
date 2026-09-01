using System.Net;
using Microsoft.Extensions.Caching.Memory;

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
    private OidcApiAccessToken? _oidcAccessToken;

    /// <summary>Cookies the API has issued to this browser session.</summary>
    public CookieContainer Cookies { get; } = new();

    /// <summary>
    /// Serializes requests made before this browser session has an application-session cookie.
    /// </summary>
    /// <remarks>
    /// Without it, two tabs opened at the same moment both arrive with an empty jar and the API
    /// issues each one its own session — leaving an orphan visible on the Active Sessions page. The
    /// gate is only contended on the first request; once a cookie exists, callers skip it.
    /// </remarks>
    public SemaphoreSlim FirstRequestGate { get; } = new(1, 1);

    /// <summary>Stores a validated OIDC access token only in server process memory.</summary>
    public void SetOidcAccessToken(string token, DateTimeOffset expiresAtUtc) =>
        _oidcAccessToken = new OidcApiAccessToken(token, expiresAtUtc);

    /// <summary>Returns a non-expired access token without logging or exposing it to browser state.</summary>
    public string? GetOidcAccessToken(DateTimeOffset now)
    {
        OidcApiAccessToken? current = _oidcAccessToken;
        if (current is null || current.ExpiresAtUtc <= now)
        {
            _oidcAccessToken = null;
            return null;
        }

        return current.Value;
    }

    /// <inheritdoc />
    public void Dispose() => FirstRequestGate.Dispose();
}

/// <summary>One server-memory-only API access token.</summary>
internal sealed record OidcApiAccessToken(string Value, DateTimeOffset ExpiresAtUtc);

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

        return _cache.GetOrCreate(CacheKey(browserSessionKey), entry =>
        {
            entry.SlidingExpiration = IdleRetention;
            entry.RegisterPostEvictionCallback(static (_, value, _, _) => (value as BrowserApiSession)?.Dispose());
            return new BrowserApiSession();
        })!;
    }

    /// <inheritdoc />
    public void Remove(string browserSessionKey)
    {
        if (!string.IsNullOrWhiteSpace(browserSessionKey))
        {
            _cache.Remove(CacheKey(browserSessionKey));
        }
    }

    private static string CacheKey(string browserSessionKey) => $"api-session:{browserSessionKey}";
}
