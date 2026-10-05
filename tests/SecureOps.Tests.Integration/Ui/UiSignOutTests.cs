using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Ui.Hosting;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Integration.Ui;

/// <summary>
/// Pins that signing out of the UI also ends the SecureOps application session.
/// </summary>
/// <remarks>
/// Clearing this application's cookie only ends the browser's authentication to the UI. The
/// application session lives in the API, and until it is explicitly ended it stays open — still
/// listed on the Active Sessions page, still counted against the operator — until it idles out.
/// Calling API logout first is what makes "Oturumu kapat" mean what an operator expects.
/// </remarks>
public sealed partial class UiSignOutTests
{
    [Theory]
    [InlineData("/session-expired")]
    [InlineData("/login")]
    public async Task RevokedSession_NextHttpRequestDeletesCookieAndOldCookieCannotRestoreStore(string path)
    {
        using SignOutUiFactory factory = new(new RecordingAccessApiClient());
        using HttpClient browser = CreateClient(factory);
        HttpResponseMessage signedIn = await SignInAsync(browser);
        string cookie = signedIn.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-SecureOpsUi.Session=", StringComparison.Ordinal)).Split(';')[0];
        CookieAuthenticationOptions options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        string key = options.TicketDataFormat.Unprotect(cookie.Split('=', 2)[1])!.Principal.FindFirst(SignedInUserService.BrowserSessionClaim)!.Value;
        IApiSessionStore store = factory.Services.GetRequiredService<IApiSessionStore>();
        BrowserApiSession session = store.GetOrCreate(key);
        session.SetApiCookies(new Uri("https://localhost/"), "__Host-SecureOps.ApplicationSession=synthetic-handle; Path=/; Secure");
        session.SetOidcAccessToken("synthetic-token", DateTimeOffset.UtcNow.AddHours(1));
        store.RequireReauthentication(key);

        using HttpResponseMessage landing = await browser.GetAsync(path);

        landing.StatusCode.Should().Be(HttpStatusCode.OK);
        landing.Headers.GetValues("Set-Cookie").Should().Contain(value => value.StartsWith("__Host-SecureOpsUi.Session=;", StringComparison.Ordinal));
        session.GetApiCookieHeader(new Uri("https://localhost/")).Should().BeEmpty();
        session.GetOidcAccessToken(DateTimeOffset.UtcNow).Should().BeNull();
        using HttpClient replay = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost/")
        });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        using HttpResponseMessage replayed = await replay.GetAsync("/session-expired");
        replayed.Headers.GetValues("Set-Cookie").Should().Contain(value => value.StartsWith("__Host-SecureOpsUi.Session=;", StringComparison.Ordinal));
        store.GetOrCreate(key).RequiresReauthentication.Should().BeTrue();

        HttpResponseMessage newLogin = await SignInAsync(browser);
        string newCookie = newLogin.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-SecureOpsUi.Session=", StringComparison.Ordinal)).Split(';')[0];
        string newKey = options.TicketDataFormat.Unprotect(newCookie.Split('=', 2)[1])!.Principal.FindFirst(SignedInUserService.BrowserSessionClaim)!.Value;
        newKey.Should().NotBe(key);
        store.GetOrCreate(newKey).RequiresReauthentication.Should().BeFalse();
        store.GetOrCreate(key).RequiresReauthentication.Should().BeTrue();
    }

    [Fact]
    public async Task SignOut_EndsTheApiApplicationSession()
    {
        RecordingAccessApiClient accessApi = new();
        using SignOutUiFactory factory = new(accessApi);

        HttpResponseMessage response = await CreateClient(factory).GetAsync("/auth/sign-out");

        accessApi.LogoutCalls.Should().Be(1);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("signed-out");
    }

    [Fact]
    public async Task SignOut_CompletesEvenWhenTheApiLogoutFails()
    {
        // Refusing to sign someone out because the API was unreachable would strand them signed in,
        // which is the worse outcome. The failure is logged server-side and the UI session still ends.
        RecordingAccessApiClient accessApi = new()
        {
            Failure = new SecureOpsApiException(new UiProblem(
                UiProblemKind.UpstreamUnavailable,
                "SessionStoreUnavailable",
                "İşlem tamamlanamadı.",
                "Oturum deposu şu anda yanıt vermiyor.",
                [],
                Retryable: true,
                RequiresRefresh: false,
                CorrelationId: "corr-1",
                Stage: null,
                StatusCode: 503))
        };

        using SignOutUiFactory factory = new(accessApi);

        HttpResponseMessage response = await CreateClient(factory).GetAsync("/auth/sign-out");

        accessApi.LogoutCalls.Should().Be(1);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("signed-out");
    }

    [Fact]
    public async Task SignOut_LeaksNoSessionMaterialToTheBrowser()
    {
        RecordingAccessApiClient accessApi = new();
        using SignOutUiFactory factory = new(accessApi);

        HttpResponseMessage response = await CreateClient(factory).GetAsync("/auth/sign-out");
        string headers = response.Headers.ToString();

        // The application-session handle is a credential. It is carried server-side and must never
        // appear in a redirect, a cookie this application sets, or the location it sends the browser to.
        headers.Should().NotContain("__Host-SecureOps.ApplicationSession");
        response.Headers.Location!.OriginalString.Should().NotContain("session");
    }

    private static HttpClient CreateClient(SignOutUiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost/")
        });

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client)
    {
        string html = await client.GetStringAsync("/login", TestContext.Current.CancellationToken);
        string token = WebUtility.HtmlDecode(AntiforgeryTokenRegex().Match(html).Groups[1].Value);
        return await client.PostAsync("/auth/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = string.Empty
        }), TestContext.Current.CancellationToken);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenRegex();

    /// <summary>Hosts the UI with a recording access API client in place of the real one.</summary>
    private sealed class SignOutUiFactory : WebApplicationFactory<HttpsOffloadOptions>
    {
        private readonly IAccessApiClient _accessApi;

        public SignOutUiFactory(IAccessApiClient accessApi) => _accessApi = accessApi;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DemoMode:Enabled"] = "true",
                    ["DemoMode:AllowMockAuthentication"] = "true",
                    ["IdentityLookupApi:BaseAddress"] = "http://localhost:5000/",
                    ["ReverseProxy:HttpsOffload:Enabled"] = "false"
                }));

            builder.ConfigureServices(services => services.AddScoped(_ => _accessApi));

            return base.CreateHost(builder);
        }
    }

    /// <summary>Records logout calls and can fail on demand.</summary>
    private sealed class RecordingAccessApiClient : IAccessApiClient
    {
        public Task<SecureOps.Shared.Contracts.Access.AccessEffectiveResponse> GetMyEffectiveAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by sign-out tests.");

        public int LogoutCalls { get; private set; }

        public SecureOpsApiException? Failure { get; init; }

        public Task<LogoutResponse> LogoutAsync(CancellationToken cancellationToken)
        {
            LogoutCalls++;

            return Failure is not null
                ? Task.FromException<LogoutResponse>(Failure)
                : Task.FromResult(new LogoutResponse("ApplicationSessionEnded", "interim-cookie"));
        }

        public Task<CurrentAccessResponse> GetCurrentAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by the sign-out flow.");
    }
}
