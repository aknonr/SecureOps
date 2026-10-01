using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SecureOps.Ui.Hosting;

namespace SecureOps.Tests.Integration.Ui;

/// <summary>
/// Pins the boundaries between the UI's failure classes.
/// </summary>
/// <remarks>
/// <para>
/// These exist because <c>MapFallbackToPage("/_Host")</c> answers every unmatched path, so without
/// deliberate handling any of these outcomes could collapse into the Blazor "Sayfa bulunamadı"
/// screen: a real 500 would be reported as a missing page, and a missing page would be reported as
/// "200 OK".
/// </para>
/// <para>
/// These drive requests as HTTPS. TestServer populates no connection endpoint information, so the
/// offload trust boundary cannot be evaluated in-process; that logic is covered separately in
/// <c>UiHttpsOffloadTests</c>. What is pinned here is routing and status semantics, which are
/// independent of how the scheme came to be HTTPS.
/// </para>
/// </remarks>
public sealed class UiErrorRoutingTests : IClassFixture<UiErrorRoutingTests.OffloadUiFactory>
{
    private readonly OffloadUiFactory _factory;

    public UiErrorRoutingTests(OffloadUiFactory factory) => _factory = factory;

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost/")
    });

    [Fact]
    public async Task Login_WhenRequestIsSeenAsHttps_RendersSignIn()
    {
        // TestServer cannot populate Connection.RemoteIpAddress or LocalPort, so the offload
        // conditions cannot be met here; this drives the request as HTTPS directly, which is the
        // state the middleware produces. The middleware's own decision logic is covered against real
        // HttpContext objects in UiHttpsOffloadTests, and the cleartext end-to-end behaviour was
        // verified against a running host over plain HTTP.
        HttpResponseMessage response = await CreateClient().GetAsync("/login?returnUrl=dashboard");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The sign-in action itself, rather than a heading: the card now leads with the product
        // name, and what proves this is the sign-in page is that it offers the sign-in action.
        body.Should().Contain("TEST Girişi");
        body.Should().NotContain("Sayfa bulunamadı");
    }

    [Fact]
    public async Task Root_WhileAnonymous_RoutesToTheSignInFlow_AndNotToNotFound()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/");

        // Either an immediate redirect to sign-in or the prerendered shell that performs it — both
        // are the sign-in flow. What matters is that it is not reported as a missing page.
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found)
        {
            response.Headers.Location!.OriginalString.Should().Contain("login");
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Sayfa bulunamadı");
    }

    [Fact]
    public async Task UnknownRoute_ReportsNotFoundStatus_NotOk()
    {
        // The fallback route answers 200 for everything; the router state must correct that, or a
        // missing page is advertised to browsers and monitoring as a success.
        HttpResponseMessage response = await CreateClient().GetAsync("/definitely-not-a-real-route");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("Sayfa bulunamadı");
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/reporting/operators")]
    [InlineData("/directory/users")]
    [InlineData("/directory/groups")]
    [InlineData("/admin/sessions")]
    [InlineData("/admin/system-status")]
    [InlineData("/identity-lookup")]
    [InlineData("/resources")]
    [InlineData("/resources/sets")]
    [InlineData("/admin/resources")]
    public async Task ReportingRoutes_ResolveToTheSignInFlow_AndNotToNotFound(string path)
    {
        // Both are [Authorize]. Anonymously they must route into sign-in — never be reported as
        // missing pages, which is what the catch-all fallback would otherwise make them look like
        // once the router finds no match for a signed-out visitor.
        HttpResponseMessage response = await CreateClient().GetAsync(path);

        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found)
        {
            response.Headers.Location!.OriginalString.Should().Contain("login");
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Sayfa bulunamadı");
    }

    [Theory]
    [InlineData("/session-expired", "Oturum süresi")]
    [InlineData("/signed-out", "Oturumunuz kapatıldı")]
    public async Task SessionStates_AreDistinctFromNotFound(string path, string marker)
    {
        HttpResponseMessage response = await CreateClient().GetAsync(path);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(marker);
        body.Should().NotContain("Sayfa bulunamadı");
    }

    [Fact]
    public async Task ForbiddenState_IsDistinctFromNotFound()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/access-denied");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain("Sayfa bulunamadı");
    }

    [Fact]
    public async Task ErrorState_IsDistinctFromNotFound()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/error");
        string body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("Sayfa bulunamadı");
    }

    /// <summary>
    /// Hosts the UI over cleartext with the loopback connection treated as an approved offload path.
    /// </summary>
    // Typed on a public SecureOps.Ui type rather than Program: both SecureOps.Api and SecureOps.Ui
    // declare a top-level Program in the global namespace, and only the API exposes its internals to
    // this project — so WebApplicationFactory<Program> silently boots the API instead of the UI.
    public sealed class OffloadUiFactory : WebApplicationFactory<HttpsOffloadOptions>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DemoMode:Enabled"] = "true",
                    ["DemoMode:AllowMockAuthentication"] = "true",
                    ["IdentityLookupApi:BaseAddress"] = "http://localhost:5000/",

                    // TestServer connections report no remote address and no local port, so the
                    // offload conditions cannot be satisfied there. These values keep the options
                    // valid at startup; the middleware's own decision logic is covered exhaustively
                    // by UiHttpsOffloadTests against a real HttpContext.
                    ["ReverseProxy:HttpsOffload:Enabled"] = "false",
                    ["ReverseProxy:HttpsOffload:TrustedProxyIps:0"] = "127.0.0.1",
                    ["ReverseProxy:HttpsOffload:ExpectedHosts:0"] = "localhost",
                    ["ReverseProxy:HttpsOffload:ExpectedLocalPort"] = "80"
                }));

            return base.CreateHost(builder);
        }
    }
}
