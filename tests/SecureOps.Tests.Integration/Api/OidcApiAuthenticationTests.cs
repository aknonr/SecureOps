using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Tests.Integration.Api;

public sealed class OidcApiAuthenticationTests
{
    private static readonly RsaSecurityKey _signingKey = new(RSA.Create(2048));

    [Fact]
    public async Task UnknownAuthenticatedOidcUser_IsPendingAndCannotUseCapabilities()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, Token("synthetic-subject-unknown", "operator.unknown", roleEvidence: "Administrator"));

        CurrentAccessResponse access = (await client.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;
        HttpResponseMessage protectedResponse = await client.GetAsync("/api/v1/access/users");

        access.AccessStatus.Should().Be("Pending");
        access.Roles.Should().BeEmpty();
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PersistedAuthorizedOidcUser_IsAdmittedWithoutClaimRoleAuthority()
    {
        const string subject = "synthetic-subject-authorized";
        string stableIdentifier = OidcExternalIdentityNormalizer.StableIdentifier(_issuer, subject);
        using WebApplicationFactory<Program> factory = CreateFactory(stableIdentifier);
        using HttpClient client = Client(factory, Token(subject, "operator.authorized"));

        CurrentAccessResponse access = (await client.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;

        access.AccessStatus.Should().Be("Approved");
        access.Roles.Should().ContainSingle("Admin");
        access.AuthenticationSource.Should().Be("oidc");
    }

    [Fact]
    public async Task OversizedOidcClaim_IsRejectedBeforeAccessResolution()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, Token("synthetic-subject", new string('x', 1100)));

        HttpResponseMessage response = await client.GetAsync("/api/v1/access/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OidcAndDemoSchemes_CoexistDuringReadinessPeriod()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(demoEnabled: true);
        using HttpClient oidc = Client(factory, Token("synthetic-subject", "operator.one"));
        using HttpClient demo = factory.CreateClient();
        demo.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        IAuthenticationSchemeProvider schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        AuthenticationScheme? defaultScheme = await schemes.GetDefaultAuthenticateSchemeAsync();

        defaultScheme!.Name.Should().Be(ExternalIdentityClaimTypes.CompositeApiScheme);
        (await oidc.GetAsync("/api/v1/access/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.GetAsync("/api/v1/access/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidAudience_IsRejected()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, Token("synthetic-subject", "operator.one", audience: "wrong-audience"));

        HttpResponseMessage response = await client.GetAsync("/api/v1/access/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidIssuer_IsRejectedAndExplicitMetadataAddressIsConfigured()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        JwtBearerOptions options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(ExternalIdentityClaimTypes.OidcBearerScheme);
        using HttpClient client = Client(factory, Token(
            "synthetic-subject", "operator.one", issuer: "https://different-issuer.example.test"));

        HttpResponseMessage response = await client.GetAsync("/api/v1/access/me");

        options.MetadataAddress.Should().Be(_issuer + "/idp/.well-known/openid-configurations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NonRs256Token_IsRejected()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, HmacToken());

        HttpResponseMessage response = await client.GetAsync("/api/v1/access/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private const string _issuer = "https://identity.example.test";
    private const string _audience = "secureops-api-test";

    private static WebApplicationFactory<Program> CreateFactory(
        string? bootstrapAdministrator = null,
        bool demoEnabled = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("Oidc:Enabled", "true");
            builder.UseSetting("Oidc:Authority", _issuer);
            builder.UseSetting("Oidc:MetadataAddress", _issuer + "/idp/.well-known/openid-configurations");
            builder.UseSetting("Oidc:ApiAudience", _audience);
            builder.UseSetting("Oidc:RequireHttpsMetadata", "true");
            builder.UseSetting("DemoAuth:Enabled", demoEnabled ? "true" : "false");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "false");
            if (bootstrapAdministrator is not null)
            {
                builder.UseSetting("Access:BootstrapAdministrators:0", bootstrapAdministrator);
            }
            builder.ConfigureServices(services =>
                services.PostConfigure<JwtBearerOptions>(ExternalIdentityClaimTypes.OidcBearerScheme, options =>
                {
                    OpenIdConnectConfiguration configuration = new() { Issuer = _issuer };
                    configuration.SigningKeys.Add(_signingKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                }));
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(
        string subject,
        string loginName,
        string? roleEvidence = null,
        string? audience = null,
        string? issuer = null)
    {
        List<System.Security.Claims.Claim> claims =
        [
            new("sub", subject),
            new("loginname", loginName)
        ];
        if (roleEvidence is not null)
        {
            claims.Add(new("uygulama-role", roleEvidence));
        }

        JwtSecurityToken token = new(
            issuer: issuer ?? _issuer,
            audience: audience ?? _audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HmacToken()
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes("synthetic-hmac-key-with-at-least-32-bytes"));
        JwtSecurityToken token = new(
            issuer: _issuer,
            audience: _audience,
            claims: [new("sub", "synthetic-subject"), new("loginname", "operator.one")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
