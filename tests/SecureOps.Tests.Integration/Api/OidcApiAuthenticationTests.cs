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
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.Sessions;

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
        using WebApplicationFactory<Program> factory = CreateFactory();
        await SeedAdminAsync(factory, stableIdentifier, "oidc");
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

    [Fact]
    public async Task ValidatedOidcClaims_CreateAndUpdateProfileWithoutChangingStableIdentity()
    {
        const string subject = "synthetic-profile-subject";
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient first = Client(factory, Token(subject, "operator.one", displayName: "Operator One", mail: "one@example.test", uid: "uid-100"));

        CurrentAccessResponse firstAccess = (await first.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;
        ApplicationUser persistedFirst = (await UsersAsync(factory)).Should().ContainSingle().Subject;

        using HttpClient second = Client(factory, Token(subject, "operator.one", displayName: "Updated Operator", mail: "updated@example.test", uid: "uid-100"));
        CurrentAccessResponse secondAccess = (await second.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;
        ApplicationUser persistedSecond = (await UsersAsync(factory)).Should().ContainSingle().Subject;

        firstAccess.Profile.Should().Be(new AccessIdentityProfileResponse("Operator One", "operator.one", "one@example.test", null, null, "uid-100"));
        secondAccess.Profile.Should().Be(new AccessIdentityProfileResponse("Updated Operator", "operator.one", "updated@example.test", null, null, "uid-100"));
        persistedSecond.Id.Should().Be(persistedFirst.Id);
        persistedSecond.CorporateIdentity.Should().Be(persistedFirst.CorporateIdentity)
            .And.Be(OidcExternalIdentityNormalizer.StableIdentifier(_issuer, subject));
        persistedSecond.Uid.Should().Be("uid-100");
        persistedSecond.ProfileUpdatedAt.Should().NotBeNull().And.BeOnOrAfter(persistedFirst.ProfileUpdatedAt!.Value);
    }

    [Fact]
    public async Task MissingLaterClaims_PreserveKnownProfileAndOpaqueUserBackfills()
    {
        const string subject = "synthetic-backfill-subject";
        string stableIdentifier = OidcExternalIdentityNormalizer.StableIdentifier(_issuer, subject);
        using WebApplicationFactory<Program> factory = CreateFactory();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IAccessRepository repository = scope.ServiceProvider.GetRequiredService<IAccessRepository>();
            _ = await repository.EnsureUserAsync(new CorporatePrincipal(stableIdentifier, "oidc"), true, TimeSpan.Zero, CancellationToken.None);
        }

        using HttpClient profileClient = Client(factory, Token(subject, "operator.backfill", displayName: "Backfilled User", mail: "backfill@example.test", uid: "uid-backfill"));
        _ = await profileClient.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me");
        using HttpClient missingClaimsClient = Client(factory, Token(subject, loginName: null));
        _ = await missingClaimsClient.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me");

        ApplicationUser user = (await UsersAsync(factory)).Should().ContainSingle().Subject;
        user.CorporateIdentity.Should().Be(stableIdentifier);
        user.LoginName.Should().Be("operator.backfill");
        user.DisplayName.Should().Be("Backfilled User");
        user.Mail.Should().Be("backfill@example.test");
        user.Uid.Should().Be("uid-backfill");
    }

    [Fact]
    public async Task BrowserHeaders_CannotSpoofPersistedOidcProfile()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, Token("synthetic-spoof-subject", "operator.real", displayName: "Validated Name"));
        client.DefaultRequestHeaders.Add("X-SecureOps-DisplayName", "Browser Supplied Admin");
        client.DefaultRequestHeaders.Add("X-SecureOps-LoginName", "browser.admin");

        _ = await client.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me");

        ApplicationUser user = (await UsersAsync(factory)).Should().ContainSingle().Subject;
        user.LoginName.Should().Be("operator.real");
        user.DisplayName.Should().Be("Validated Name");
    }

    [Fact]
    public async Task AdminAccessAndSessionProjections_UsePersistedHumanProfile()
    {
        const string userSubject = "synthetic-projection-user";
        const string adminSubject = "synthetic-projection-admin";
        using WebApplicationFactory<Program> factory = CreateFactory();
        await SeedAdminAsync(factory, OidcExternalIdentityNormalizer.StableIdentifier(_issuer, adminSubject), "oidc");
        using HttpClient user = Client(factory, Token(userSubject, "requester.one", displayName: "Requester One", mail: "requester@example.test", uid: "uid-requester"));
        CurrentAccessResponse requester = (await user.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;
        using HttpClient admin = Client(factory, Token(adminSubject, "admin.one", displayName: "Admin One", mail: "admin@example.test"));
        _ = await admin.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me");

        AccessRequestResponse[] requests = (await admin.GetFromJsonAsync<AccessRequestResponse[]>("/api/v1/access/requests"))!;
        AccessUserResponse[] users = (await admin.GetFromJsonAsync<AccessUserResponse[]>("/api/v1/access/users"))!;
        ActiveApplicationSessionsResponse sessions = (await admin.GetFromJsonAsync<ActiveApplicationSessionsResponse>("/api/v1/sessions/active"))!;

        requests.Single(item => item.UserId == requester.UserId).Profile.Should().Be(
            new AccessIdentityProfileResponse("Requester One", "requester.one", "requester@example.test", null, null, "uid-requester"));
        users.Single(item => item.UserId == requester.UserId).Profile!.DisplayName.Should().Be("Requester One");
        ApplicationSessionResponse session = sessions.Items.Single(item => item.UserId == requester.UserId);
        session.DisplayName.Should().Be("Requester One");
        session.Principal.Should().Be("requester.one");
        session.NormalizedPrincipal.Should().StartWith("oidc:");
        session.Uid.Should().Be("uid-requester");
    }

    private const string _issuer = "https://identity.example.test";
    private const string _audience = "secureops-api-test";

    private static WebApplicationFactory<Program> CreateFactory(bool demoEnabled = false) =>
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
            builder.ConfigureServices(services =>
                services.PostConfigure<JwtBearerOptions>(ExternalIdentityClaimTypes.OidcBearerScheme, options =>
                {
                    OpenIdConnectConfiguration configuration = new() { Issuer = _issuer };
                    configuration.SigningKeys.Add(_signingKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                }));
        });

    private static async Task SeedAdminAsync(WebApplicationFactory<Program> factory, string identity, string authenticationSource)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IAccessRepository repository = scope.ServiceProvider.GetRequiredService<IAccessRepository>();
        EnsureAccessUserResult ensured = await repository.EnsureUserAsync(
            new CorporatePrincipal(identity, authenticationSource),
            createRequest: true,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        _ = await repository.DecideRequestAsync(
            ensured.PendingRequest!.Id,
            SecureOps.Domain.Access.AccessRequestStatus.Approved,
            ensured.PendingRequest.Version,
            "system:test-seed",
            ["Admin"],
            "Synthetic persisted authorization fixture.",
            CancellationToken.None);
    }

    private static async Task<IReadOnlyList<ApplicationUser>> UsersAsync(WebApplicationFactory<Program> factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAccessRepository>().ListUsersAsync(CancellationToken.None);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(
        string subject,
        string? loginName,
        string? roleEvidence = null,
        string? audience = null,
        string? issuer = null,
        string? displayName = null,
        string? mail = null,
        string? uid = null)
    {
        List<System.Security.Claims.Claim> claims =
        [
            new("sub", subject)
        ];
        AddClaim(claims, "loginname", loginName);
        AddClaim(claims, "displayname", displayName);
        AddClaim(claims, "mail", mail);
        AddClaim(claims, "uid", uid);
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

    private static void AddClaim(List<System.Security.Claims.Claim> claims, string type, string? value)
    {
        if (value is not null)
        {
            claims.Add(new(type, value));
        }
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
