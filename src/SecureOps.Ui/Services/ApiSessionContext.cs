namespace SecureOps.Ui.Services;

/// <summary>
/// Carries the browser-session correlation value for the current circuit or request.
/// </summary>
/// <remarks>
/// One browser authentication session must present one stable application-session handle to the API.
/// The correlation value is the key that makes that possible: it is stable for as long as the browser
/// keeps its authentication cookie, it is shared by every tab of that browser, and a separate or
/// private browser gets a different one.
/// <para>
/// It is not the application-session handle and must never be treated as one. It carries no
/// authority: the API still authenticates and authorizes every request independently, and the
/// correlation value only decides which server-side cookie jar the UI replays.
/// </para>
/// </remarks>
public interface IApiSessionContext
{
    /// <summary>
    /// Browser-session correlation value, or <see langword="null"/> when none is available.
    /// </summary>
    /// <remarks>
    /// Null is a degraded but safe state: the request proceeds without a shared jar, so it may
    /// establish its own application session. It must never fall back to a per-user or per-process
    /// constant, because that would collapse separate browsers into one session and weaken
    /// revocation.
    /// </remarks>
    public string? BrowserSessionKey { get; }

    /// <summary>
    /// Seeds the correlation value for a Blazor circuit.
    /// </summary>
    /// <param name="key">Correlation value captured from the authenticated principal.</param>
    /// <remarks>
    /// Called once by the root component. Inside a circuit there is no <c>HttpContext</c> to read
    /// from, so the value has to be handed in from the request that started the circuit.
    /// </remarks>
    public void Seed(string? key);
}

/// <summary>
/// Scoped <see cref="IApiSessionContext"/> backed by the root component seed with an
/// <c>HttpContext</c> fallback.
/// </summary>
/// <remarks>
/// Two hosting shapes need the same value. Blazor circuits get it from <see cref="Seed"/>, because
/// their scope outlives the request. Razor Pages, endpoints, and prerendering read it straight off
/// the authenticated principal, which is where it lives for the whole browser session.
/// </remarks>
public sealed class ApiSessionContext : IApiSessionContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private string? _seeded;

    /// <summary>
    /// Initializes a new API session context.
    /// </summary>
    /// <param name="httpContextAccessor">Accessor used for the non-circuit fallback.</param>
    public ApiSessionContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string? BrowserSessionKey => _seeded ?? FromHttpContext();

    /// <inheritdoc />
    public void Seed(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _seeded = key;
        }
    }

    private string? FromHttpContext()
    {
        string? claim = _httpContextAccessor.HttpContext?.User
            .FindFirst(SignedInUserService.BrowserSessionClaim)?.Value;

        return string.IsNullOrWhiteSpace(claim) ? null : claim;
    }
}
