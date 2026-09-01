using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Ui.Hosting;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Integration.Ui;

public sealed partial class UiOidcAuthenticationTests
{
    private static readonly SymmetricSecurityKey _signingKey = new(
        Encoding.UTF8.GetBytes("synthetic-oidc-signing-key-with-at-least-32-bytes"));

    [Fact]
    public async Task SignIn_UsesAuthorizationCodePkceAndPublicHttpsCallback()
    {
        using OidcUiFactory factory = new();
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage challenge = await ChallengeAsync(client);

        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Dictionary<string, string> query = Query(challenge.Headers.Location!);
        query["response_type"].Should().Be("code");
        query["code_challenge"].Should().NotBeNullOrWhiteSpace();
        query["code_challenge_method"].Should().Be("S256");
        query["redirect_uri"].Should().Be("https://wasasyonetim.thy.com/signin-oidc");
        query["scope"].Split(' ').Should().Contain("openid");
    }

    [Fact]
    public void OidcOptions_KeepTokensServerSideAndRequireSecureCallbackCookies()
    {
        using OidcUiFactory factory = new();
        OpenIdConnectOptions options = factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(ExternalIdentityClaimTypes.OidcInteractiveScheme);

        options.ResponseType.Should().Be(OpenIdConnectResponseType.Code);
        options.UsePkce.Should().BeTrue();
        options.SaveTokens.Should().BeFalse();
        options.CorrelationCookie.HttpOnly.Should().BeTrue();
        options.CorrelationCookie.SameSite.Should().Be(SameSiteMode.None);
        options.CorrelationCookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        options.NonceCookie.HttpOnly.Should().BeTrue();
        options.NonceCookie.SameSite.Should().Be(SameSiteMode.None);
        options.NonceCookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    [Fact]
    public async Task Callback_WithValidStateAndCorrelation_CreatesNormalizedLocalSession()
    {
        using OidcUiFactory factory = new();
        using HttpClient client = CreateClient(factory);
        HttpResponseMessage challenge = await ChallengeAsync(client);
        Dictionary<string, string> query = Query(challenge.Headers.Location!);
        factory.Provider.Nonce = query["nonce"];

        HttpResponseMessage callback = await client.GetAsync($"/signin-oidc?code=synthetic-code&state={UrlEncoder.Default.Encode(query["state"])}");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/dashboard");
        callback.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.Contains("__Host-SecureOpsUi.Session", StringComparison.Ordinal));
        factory.Provider.TokenRequests.Should().Be(1);
    }

    [Fact]
    public async Task Callback_WithoutCorrelationCookie_FailsSafely()
    {
        using OidcUiFactory factory = new();
        using HttpClient challengeClient = CreateClient(factory);
        HttpResponseMessage challenge = await ChallengeAsync(challengeClient);
        Dictionary<string, string> query = Query(challenge.Headers.Location!);
        factory.Provider.Nonce = query["nonce"];
        using HttpClient callbackClient = CreateClient(factory);

        HttpResponseMessage callback = await callbackClient.GetAsync($"/signin-oidc?code=synthetic-code&state={UrlEncoder.Default.Encode(query["state"])}");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/login?error=sign-in-failed");
        factory.Provider.TokenRequests.Should().Be(0);
    }

    [Fact]
    public async Task Callback_WithTamperedState_FailsSafely()
    {
        using OidcUiFactory factory = new();
        using HttpClient client = CreateClient(factory);
        HttpResponseMessage challenge = await ChallengeAsync(client);
        Dictionary<string, string> query = Query(challenge.Headers.Location!);
        factory.Provider.Nonce = query["nonce"];

        HttpResponseMessage callback = await client.GetAsync(
            $"/signin-oidc?code=synthetic-code&state={UrlEncoder.Default.Encode(query["state"] + "tampered")}");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/login?error=sign-in-failed");
        factory.Provider.TokenRequests.Should().Be(0);
    }

    [Fact]
    public async Task Callback_MissingOptionalClaims_StillSucceeds()
    {
        using OidcUiFactory factory = new(includeOptionalClaims: false);
        using HttpClient client = CreateClient(factory);
        HttpResponseMessage challenge = await ChallengeAsync(client);
        Dictionary<string, string> query = Query(challenge.Headers.Location!);
        factory.Provider.Nonce = query["nonce"];

        HttpResponseMessage callback = await client.GetAsync($"/signin-oidc?code=synthetic-code&state={UrlEncoder.Default.Encode(query["state"])}");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.Contains("__Host-SecureOpsUi.Session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_WithOversizedReviewedClaim_FailsClosed()
    {
        using OidcUiFactory factory = new(oversizedClaim: true);
        using HttpClient client = CreateClient(factory);
        HttpResponseMessage challenge = await ChallengeAsync(client);
        Dictionary<string, string> query = Query(challenge.Headers.Location!);
        factory.Provider.Nonce = query["nonce"];

        HttpResponseMessage callback = await client.GetAsync(
            $"/signin-oidc?code=synthetic-code&state={UrlEncoder.Default.Encode(query["state"])}");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/login?error=sign-in-failed");
        callback.Headers.GetValues("Set-Cookie").Should().NotContain(value =>
            value.Contains("__Host-SecureOpsUi.Session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logout_EndsApiSessionBeforeOptionalProviderSignOut()
    {
        RecordingAccessApiClient accessApi = new();
        using OidcUiFactory factory = new(accessApi: accessApi, enableRemoteSignOut: true);
        using HttpClient client = CreateClient(factory);
        HttpResponseMessage challenge = await ChallengeAsync(client);
        Dictionary<string, string> query = Query(challenge.Headers.Location!);
        factory.Provider.Nonce = query["nonce"];
        _ = await client.GetAsync($"/signin-oidc?code=synthetic-code&state={UrlEncoder.Default.Encode(query["state"])}");

        HttpResponseMessage logout = await client.GetAsync("/auth/sign-out");

        accessApi.LogoutCalls.Should().Be(1);
        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logout.Headers.Location!.Host.Should().Be("identity.example.test");
        Query(logout.Headers.Location!)["post_logout_redirect_uri"].Should().Be(
            "https://wasasyonetim.thy.com/signout-callback-oidc");
    }

    private static HttpClient CreateClient(OidcUiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://wasasyonetim.thy.com")
        });

    private static async Task<HttpResponseMessage> ChallengeAsync(HttpClient client)
    {
        string html = await client.GetStringAsync("/login");
        string token = WebUtility.HtmlDecode(AntiforgeryTokenRegex().Match(html).Groups[1].Value);
        return await client.PostAsync("/auth/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = string.Empty
        }));
    }

    private static Dictionary<string, string> Query(Uri uri) => uri.Query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            pair => Uri.UnescapeDataString(pair[0]),
            pair => Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : string.Empty),
            StringComparer.Ordinal);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenRegex();

    private sealed class OidcUiFactory : WebApplicationFactory<HttpsOffloadOptions>
    {
        private readonly IAccessApiClient? _accessApi;
        private readonly bool _enableRemoteSignOut;

        public OidcUiFactory(
            bool includeOptionalClaims = true,
            IAccessApiClient? accessApi = null,
            bool enableRemoteSignOut = false,
            bool oversizedClaim = false)
        {
            Provider = new FakeOidcProvider(includeOptionalClaims, oversizedClaim);
            _accessApi = accessApi;
            _enableRemoteSignOut = enableRemoteSignOut;
        }

        public FakeOidcProvider Provider { get; }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoMode:Enabled"] = "true",
                ["DemoMode:AllowMockAuthentication"] = "true",
                ["IdentityLookupApi:BaseAddress"] = "https://api.example.test/",
                ["ReverseProxy:HttpsOffload:Enabled"] = "false",
                ["Oidc:Enabled"] = "true",
                ["Oidc:Authority"] = "https://identity.example.test",
                ["Oidc:ClientId"] = "secureops-ui-test",
                ["Oidc:ClientAuthenticationMethod"] = "None",
                ["Oidc:ApiAudience"] = "secureops-api-test",
                ["Oidc:Scopes:0"] = "openid",
                ["Oidc:RequireHttpsMetadata"] = "true",
                ["Oidc:UsePkce"] = "true",
                ["Oidc:EnableRemoteSignOut"] = _enableRemoteSignOut ? "true" : "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<OpenIdConnectOptions>(ExternalIdentityClaimTypes.OidcInteractiveScheme, options =>
                {
                    options.ConfigurationManager = Provider.ConfigurationManager;
                    options.BackchannelHttpHandler = Provider;
                    options.Backchannel = new HttpClient(Provider, disposeHandler: false);
                });
                if (_accessApi is not null)
                {
                    services.AddScoped(_ => _accessApi);
                }
            });
            return base.CreateHost(builder);
        }
    }

    private sealed class FakeOidcProvider : HttpMessageHandler
    {
        private readonly bool _includeOptionalClaims;
        private readonly bool _oversizedClaim;

        public FakeOidcProvider(bool includeOptionalClaims, bool oversizedClaim)
        {
            _includeOptionalClaims = includeOptionalClaims;
            _oversizedClaim = oversizedClaim;
            OpenIdConnectConfiguration configuration = new()
            {
                Issuer = "https://identity.example.test",
                AuthorizationEndpoint = "https://identity.example.test/authorize",
                TokenEndpoint = "https://identity.example.test/token",
                EndSessionEndpoint = "https://identity.example.test/logout"
            };
            configuration.SigningKeys.Add(_signingKey);
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        }

        public IConfigurationManager<OpenIdConnectConfiguration> ConfigurationManager { get; }

        public string? Nonce { get; set; }

        public int TokenRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TokenRequests++;
            List<System.Security.Claims.Claim> claims =
            [
                new("sub", "synthetic-subject-100"),
                new("nonce", Nonce ?? string.Empty),
                new(
                    JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    System.Security.Claims.ClaimValueTypes.Integer64)
            ];
            if (_includeOptionalClaims)
            {
                claims.Add(new("loginname", "operator.one"));
                claims.Add(new("displayname", _oversizedClaim ? new string('x', 513) : "Operator One"));
                claims.Add(new("mail", "operator.one@example.test"));
                claims.Add(new("uid", "uid-100"));
                claims.Add(new("uygulama-role", "Administrator"));
            }

            JwtSecurityToken token = new(
                issuer: "https://identity.example.test",
                audience: "secureops-ui-test",
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));
            string body = JsonSerializer.Serialize(new
            {
                access_token = "synthetic-access-token",
                token_type = "Bearer",
                expires_in = 300,
                id_token = new JwtSecurityTokenHandler().WriteToken(token)
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecordingAccessApiClient : IAccessApiClient
    {
        public int LogoutCalls { get; private set; }

        public Task<LogoutResponse> LogoutAsync(CancellationToken cancellationToken)
        {
            LogoutCalls++;
            return Task.FromResult(new LogoutResponse("ApplicationSessionEnded", "synthetic"));
        }

        public Task<CurrentAccessResponse> GetCurrentAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
