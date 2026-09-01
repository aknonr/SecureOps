using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the browser-session-aware API transport.
/// </summary>
/// <remarks>
/// These are the regressions that matter operationally. Before this transport existed, every typed
/// client kept its own pooled cookie container, so a refresh opened another application session and
/// the Active Sessions page filled with duplicates of one person. Reuse within a browser, isolation
/// between browsers, and no leak of the correlation stamp are what stop that from coming back.
/// </remarks>
public sealed class ApiSessionTransportTests
{
    private const string ApiCookie = "__Host-SecureOps.ApplicationSession";

    [Fact]
    public async Task SecondRequest_ReplaysTheSessionCookieTheApiIssued()
    {
        // A browser refresh: same correlation value, second call. The API must see the handle it
        // already issued rather than being asked for a new session.
        (HttpClient client, RecordingHandler handler, _) = Create("browser-a", issueCookie: true);

        await client.GetAsync("api/v1/access/me");
        await client.GetAsync("api/v1/access/me");

        handler.SentCookies[0].Should().BeNull();
        handler.SentCookies[1].Should().Contain(ApiCookie + "=session-1");
    }

    [Fact]
    public async Task SeparateBrowserSessions_DoNotShareACookieJar()
    {
        // A private window is a different browser session. Merging the two would collapse them into
        // one application session and make revocation meaningless.
        IApiSessionStore store = NewStore();
        (HttpClient first, RecordingHandler firstHandler, _) = Create("browser-a", true, store);
        (HttpClient second, RecordingHandler secondHandler, _) = Create("browser-b", true, store);

        await first.GetAsync("api/v1/access/me");
        await first.GetAsync("api/v1/access/me");
        await second.GetAsync("api/v1/access/me");

        firstHandler.SentCookies[1].Should().Contain(ApiCookie + "=session-1");
        secondHandler.SentCookies[0].Should().BeNull();
    }

    [Fact]
    public async Task DifferentTypedClients_ShareOneBrowserSessionJar()
    {
        // Navigating between pages uses different typed clients. They must land on the same
        // application session, which is exactly what per-client cookie containers could not do.
        IApiSessionStore store = NewStore();
        (HttpClient sessions, _, _) = Create("browser-a", true, store);
        (HttpClient directory, RecordingHandler directoryHandler, _) = Create("browser-a", true, store);

        await sessions.GetAsync("api/v1/sessions/current");
        await directory.GetAsync("api/v1/directory/groups/lookup");

        directoryHandler.SentCookies[0].Should().Contain(ApiCookie + "=session-1");
    }

    [Fact]
    public async Task CorrelationStamp_NeverReachesTheApi()
    {
        // It is internal routing information for the handler. On the wire it would be an
        // unexplained header carrying a value the API has no business seeing.
        (HttpClient client, RecordingHandler handler, _) = Create("browser-a", issueCookie: true);

        await client.GetAsync("api/v1/access/me");

        handler.SawCorrelationHeader.Should().BeFalse();
    }

    [Fact]
    public async Task RequestWithoutCorrelation_StillSucceedsWithoutASharedJar()
    {
        // Unauthenticated and startup calls have no browser session yet. They must not fail, and
        // they must not fall back to a shared default jar.
        (HttpClient client, RecordingHandler handler, _) = Create(browserSessionKey: null, issueCookie: true);

        await client.GetAsync("api/v1/access/me");
        await client.GetAsync("api/v1/access/me");

        handler.SentCookies.Should().AllSatisfy(cookie => cookie.Should().BeNull());
    }

    [Fact]
    public async Task RemovingAJar_StopsTheHandleFromBeingReplayed()
    {
        // What sign-out relies on: after API logout the handle is dead, and replaying it for the
        // next person on this browser would be worse than losing session reuse.
        IApiSessionStore store = NewStore();
        (HttpClient client, RecordingHandler handler, _) = Create("browser-a", true, store);

        await client.GetAsync("api/v1/access/me");
        store.Remove("browser-a");
        await client.GetAsync("api/v1/access/me");

        handler.SentCookies[1].Should().BeNull();
    }

    [Fact]
    public async Task MalformedSetCookie_IsRejectedWithoutFailingTheRequest()
    {
        (HttpClient client, RecordingHandler handler, _) = Create("browser-a", issueCookie: false);
        handler.MalformedSetCookie = true;

        HttpResponseMessage response = await client.GetAsync("api/v1/access/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Attach_AddsNothingWhenThereIsNoBrowserSession()
    {
        using HttpClient client = new();

        ApiSessionHeaders.Attach(client, new FakeApiSessionContext());

        client.DefaultRequestHeaders.Contains(ApiSessionHeaders.BrowserSession).Should().BeFalse();
    }

    [Fact]
    public void Context_ReadsTheCorrelationClaimWhenNoCircuitHasSeededIt()
    {
        // Razor Pages, endpoints, and prerendering all run with an HttpContext. Only a live circuit
        // needs the seed, so the claim is the fallback rather than the exception.
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(SignedInUserService.BrowserSessionClaim, "from-cookie")],
                "test"))
        };

        ApiSessionContext context = new(new HttpContextAccessor { HttpContext = httpContext });

        context.BrowserSessionKey.Should().Be("from-cookie");
    }

    [Fact]
    public void Context_PrefersTheCircuitSeedAndIgnoresAnEmptyOne()
    {
        ApiSessionContext context = new(new HttpContextAccessor());

        context.Seed("   ");
        context.BrowserSessionKey.Should().BeNull();

        context.Seed("from-circuit");
        context.BrowserSessionKey.Should().Be("from-circuit");
    }

    [Fact]
    public async Task OidcRelay_UsesOnlyTheCurrentServerSideBrowserToken()
    {
        IApiSessionStore store = NewStore();
        store.GetOrCreate("browser-a").SetOidcAccessToken("token-a", DateTimeOffset.UtcNow.AddMinutes(5));
        BearerRecordingHandler recording = new();
        OidcApiAccessTokenHandler relay = new(
            store,
            Options.Create(new OidcOptions { Enabled = true }),
            TimeProvider.System)
        {
            InnerHandler = recording
        };
        using HttpClient client = new(relay) { BaseAddress = new Uri("https://localhost/") };
        ApiSessionHeaders.Attach(client, new FakeApiSessionContext("browser-a"));

        await client.GetAsync("api/v1/access/me");

        recording.Authorization.Should().Be("Bearer token-a");
    }

    [Fact]
    public async Task OidcRelay_DisabledOrExpired_DoesNotSendBearerMaterial()
    {
        IApiSessionStore store = NewStore();
        store.GetOrCreate("browser-a").SetOidcAccessToken("expired", DateTimeOffset.UtcNow.AddMinutes(-1));
        BearerRecordingHandler recording = new();
        OidcApiAccessTokenHandler relay = new(
            store,
            Options.Create(new OidcOptions { Enabled = true }),
            TimeProvider.System)
        {
            InnerHandler = recording
        };
        using HttpClient client = new(relay) { BaseAddress = new Uri("https://localhost/") };
        ApiSessionHeaders.Attach(client, new FakeApiSessionContext("browser-a"));

        await client.GetAsync("api/v1/access/me");

        recording.Authorization.Should().BeNull();
    }

    [Fact]
    public void EveryTypedApiClient_TakesTheBrowserSessionContext()
    {
        // The failure this guards against is silent. A new client that does not stamp its requests
        // still works — it just quietly opens a second application session for the same browser on
        // every page that uses it, which is the defect this whole transport exists to remove.
        Type[] clients = typeof(ApiSessionContext).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.Name.EndsWith("ApiClient", StringComparison.Ordinal))
            .ToArray();

        clients.Should().NotBeEmpty("the assembly should contain typed API clients to check");

        foreach (Type client in clients)
        {
            client.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Should().Contain(
                    typeof(IApiSessionContext),
                    "{0} must join the shared browser-session transport",
                    client.Name);
        }
    }

    private static IApiSessionStore NewStore() =>
        new ApiSessionStore(new MemoryCache(new MemoryCacheOptions()));

    private static (HttpClient Client, RecordingHandler Handler, IApiSessionStore Store) Create(
        string? browserSessionKey,
        bool issueCookie,
        IApiSessionStore? store = null)
    {
        store ??= NewStore();
        RecordingHandler recording = new(issueCookie);

        ApiSessionCookieHandler sessionHandler = new(store, NullLogger<ApiSessionCookieHandler>.Instance)
        {
            InnerHandler = recording
        };

        HttpClient client = new(sessionHandler) { BaseAddress = new Uri("https://localhost:5001/") };
        ApiSessionHeaders.Attach(client, new FakeApiSessionContext(browserSessionKey));

        return (client, recording, store);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly bool _issueCookie;
        private int _issued;

        public RecordingHandler(bool issueCookie)
        {
            _issueCookie = issueCookie;
        }

        public List<string?> SentCookies { get; } = [];

        public bool SawCorrelationHeader { get; private set; }

        public bool MalformedSetCookie { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SentCookies.Add(request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies)
                ? string.Join("; ", cookies)
                : null);

            SawCorrelationHeader |= request.Headers.Contains(ApiSessionHeaders.BrowserSession);

            HttpResponseMessage response = new(HttpStatusCode.OK);

            if (MalformedSetCookie)
            {
                response.Headers.TryAddWithoutValidation("Set-Cookie", "=;;;");
            }
            else if (_issueCookie && SentCookies[^1] is null)
            {
                // Mirrors the API: a handle is issued only when the request arrived without one. A
                // replayed handle produces no new Set-Cookie.
                _issued++;
                response.Headers.TryAddWithoutValidation(
                    "Set-Cookie",
                    ApiCookie + "=session-" + _issued + "; Path=/; Secure; HttpOnly; SameSite=Lax");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class BearerRecordingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
