using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SecureOps.Shared.Auth;
using SecureOps.Ui.Configuration;
using SecureOps.Ui.Hosting;

namespace SecureOps.Tests.Integration.Ui;

public sealed class UiHttpsOffloadTests
{
    private const string TrustedProxyIp = "192.0.2.10";
    private const string DirectClientIp = "192.0.2.20";
    private const string ExpectedHost = "ui.example.test";
    private const int ExpectedLocalPort = 8080;

    [Fact]
    public async Task InvokeAsync_WhenCompleteTrustBoundaryMatches_ChangesSchemeToHttps()
    {
        DefaultHttpContext context = CreateContext(TrustedProxyIp, ExpectedHost, ExpectedLocalPort);

        await CreateMiddleware().InvokeAsync(context);

        context.Request.Scheme.Should().Be(Uri.UriSchemeHttps);
    }

    [Theory]
    [InlineData(DirectClientIp, ExpectedHost, ExpectedLocalPort)]
    [InlineData(TrustedProxyIp, "wrong.example.test", ExpectedLocalPort)]
    [InlineData(TrustedProxyIp, ExpectedHost, 8081)]
    public async Task InvokeAsync_WhenTrustBoundaryDoesNotMatch_LeavesSchemeHttp(
        string remoteIp,
        string host,
        int localPort)
    {
        DefaultHttpContext context = CreateContext(remoteIp, host, localPort);

        await CreateMiddleware().InvokeAsync(context);

        context.Request.Scheme.Should().Be(Uri.UriSchemeHttp);
    }

    [Fact]
    public async Task InvokeAsync_WhenFeatureDisabled_LeavesSchemeHttp()
    {
        DefaultHttpContext context = CreateContext(TrustedProxyIp, ExpectedHost, ExpectedLocalPort);

        await CreateMiddleware(enabled: false).InvokeAsync(context);

        context.Request.Scheme.Should().Be(Uri.UriSchemeHttp);
    }

    [Fact]
    public async Task InvokeAsync_WhenUntrustedSourceSpoofsForwardedProto_LeavesSchemeHttp()
    {
        DefaultHttpContext context = CreateContext(DirectClientIp, ExpectedHost, ExpectedLocalPort);
        context.Request.Headers["X-Forwarded-Proto"] = Uri.UriSchemeHttps;

        await CreateMiddleware().InvokeAsync(context);

        context.Request.Scheme.Should().Be(Uri.UriSchemeHttp);
    }

    [Fact]
    public async Task InvokeAsync_WhenRemoteAddressIsIpv4MappedIpv6_ChangesSchemeToHttps()
    {
        DefaultHttpContext context = CreateContext($"::ffff:{TrustedProxyIp}", ExpectedHost, ExpectedLocalPort);

        await CreateMiddleware().InvokeAsync(context);

        context.Request.Scheme.Should().Be(Uri.UriSchemeHttps);
    }

    [Theory]
    [InlineData("not-an-ip", ExpectedHost, ExpectedLocalPort, "invalid IP address")]
    [InlineData(null, ExpectedHost, ExpectedLocalPort, "TrustedProxyIps must contain")]
    [InlineData(TrustedProxyIp, null, ExpectedLocalPort, "ExpectedHosts must contain")]
    public void StartupValidation_WhenConfigurationIsUnsafe_FailsClearly(
        string? proxyIp,
        string? host,
        int localPort,
        string expectedMessage)
    {
        Dictionary<string, string?> settings = BaseSettings();
        if (proxyIp is not null)
        {
            settings["ReverseProxy:HttpsOffload:TrustedProxyIps:0"] = proxyIp;
        }

        if (host is not null)
        {
            settings["ReverseProxy:HttpsOffload:ExpectedHosts:0"] = host;
        }

        settings["ReverseProxy:HttpsOffload:ExpectedLocalPort"] = localPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using WebApplicationFactory<DemoModeOptions> factory = CreateFactory(settings);

        Action act = () =>
        {
            using HttpClient _ = factory.CreateClient();
        };

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public async Task Pipeline_WhenTrustedOffloadMatches_RunsBeforeHttpsRedirectionAndAntiforgery()
    {
        using WebApplicationFactory<DemoModeOptions> factory = CreateFactory(EnabledSettings());
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"http://{ExpectedHost}"),
            HandleCookies = false
        });

        using HttpResponseMessage healthResponse = await client.GetAsync("/health");
        using HttpResponseMessage loginResponse = await client.GetAsync("/login");

        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        loginResponse.Headers.GetValues("Set-Cookie").Should().Contain(cookie =>
            cookie.Contains("__Host-SecureOpsUi.AntiForgery", StringComparison.Ordinal)
            && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OidcChallenge_WhenTrustedOffloadMatches_GeneratesPublicHttpsCallback()
    {
        Dictionary<string, string?> settings = EnabledSettings();
        settings["Oidc:Enabled"] = "true";
        settings["Oidc:Authority"] = "https://identity.example.test";
        settings["Oidc:MetadataAddress"] = "https://identity.example.test/idp/.well-known/openid-configurations";
        settings["Oidc:ClientId"] = "secureops-ui-test";
        settings["Oidc:ClientAuthenticationMethod"] = "None";
        settings["Oidc:ApiAudience"] = "secureops-api-test";
        settings["Oidc:Scopes:0"] = "openid";
        settings["Oidc:RequireHttpsMetadata"] = "true";
        settings["Oidc:UsePkce"] = "true";
        using OidcOffloadUiFactory factory = new(settings);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"http://{ExpectedHost}"),
            HandleCookies = false
        });
        HttpResponseMessage login = await client.GetAsync("/login");
        string html = await login.Content.ReadAsStringAsync();
        const string marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        int start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        string token = System.Net.WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]);

        using HttpRequestMessage request = new(HttpMethod.Post, "/auth/sign-in")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["returnUrl"] = string.Empty
            })
        };
        request.Headers.Add(
            "Cookie",
            string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])));
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        string redirectUri = response.Headers.Location!.Query
            .TrimStart('?')
            .Split('&')
            .Select(part => part.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]) == "redirect_uri")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .Single();
        redirectUri.Should().Be($"https://{ExpectedHost}/signin-oidc");
    }

    [Fact]
    public void SecurityOptions_AlwaysRequireSecureCookies()
    {
        using WebApplicationFactory<DemoModeOptions> factory = CreateFactory(EnabledSettings());

        AntiforgeryOptions antiforgery = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        CookieAuthenticationOptions authentication = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        antiforgery.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        authentication.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    private static HttpsOffloadMiddleware CreateMiddleware(bool enabled = true)
    {
        HttpsOffloadOptions options = new()
        {
            Enabled = enabled,
            TrustedProxyIps = [TrustedProxyIp],
            ExpectedHosts = [ExpectedHost],
            ExpectedLocalPort = ExpectedLocalPort
        };
        return new HttpsOffloadMiddleware(_ => Task.CompletedTask, Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string remoteIp, string host, int localPort)
    {
        DefaultHttpContext context = new();
        context.Request.Scheme = Uri.UriSchemeHttp;
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Connection.LocalPort = localPort;
        return context;
    }

    private static WebApplicationFactory<DemoModeOptions> CreateFactory(IReadOnlyDictionary<string, string?> settings)
    {
        return new WebApplicationFactory<DemoModeOptions>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Demo");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.Sources.Clear();
                    configuration.AddInMemoryCollection(settings);
                });
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupFilter>(new ConnectionInfoStartupFilter(
                        IPAddress.Parse(TrustedProxyIp),
                        ExpectedLocalPort));
                    services.PostConfigure<HttpsRedirectionOptions>(options => options.HttpsPort = 8443);
                });
            });
    }

    private sealed class OidcOffloadUiFactory(IReadOnlyDictionary<string, string?> settings)
        : WebApplicationFactory<DemoModeOptions>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new ConnectionInfoStartupFilter(
                    IPAddress.Parse(TrustedProxyIp),
                    ExpectedLocalPort));
                services.PostConfigure<HttpsRedirectionOptions>(options => options.HttpsPort = 8443);
                services.PostConfigure<OpenIdConnectOptions>(ExternalIdentityClaimTypes.OidcInteractiveScheme, options =>
                {
                    OpenIdConnectConfiguration configuration = new()
                    {
                        Issuer = "https://identity.example.test",
                        AuthorizationEndpoint = "https://identity.example.test/authorize",
                        TokenEndpoint = "https://identity.example.test/token"
                    };
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
            return base.CreateHost(builder);
        }
    }

    private static Dictionary<string, string?> EnabledSettings()
    {
        Dictionary<string, string?> settings = BaseSettings();
        settings["ReverseProxy:HttpsOffload:TrustedProxyIps:0"] = TrustedProxyIp;
        settings["ReverseProxy:HttpsOffload:ExpectedHosts:0"] = ExpectedHost;
        settings["ReverseProxy:HttpsOffload:ExpectedLocalPort"] = ExpectedLocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return settings;
    }

    private static Dictionary<string, string?> BaseSettings()
    {
        return new Dictionary<string, string?>
        {
            ["IdentityLookupApi:BaseAddress"] = "https://api.example.test/",
            ["DemoMode:Enabled"] = "true",
            ["DemoMode:AllowMockAuthentication"] = "true",
            ["ReverseProxy:HttpsOffload:Enabled"] = "true"
        };
    }

    private sealed class ConnectionInfoStartupFilter(IPAddress remoteAddress, int localPort) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return application =>
            {
                application.Use(async (context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = remoteAddress;
                    context.Connection.LocalPort = localPort;
                    await nextMiddleware();
                });
                next(application);
            };
        }
    }
}
