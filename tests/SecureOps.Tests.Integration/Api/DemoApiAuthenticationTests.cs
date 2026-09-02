using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Persistence;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Api;

public sealed class DemoApiAuthenticationTests
{
    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Demo", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Demo", false, false)]
    [InlineData("Test", true, true)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    public void IsEnabled_RespectsEnvironmentAndExplicitFlag(string environment, bool flag, bool expected)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoAuth:Enabled"] = flag ? "true" : "false"
            })
            .Build();

        DemoApiAuthentication.IsEnabled(environment, configuration).Should().Be(expected);
    }

    [Fact]
    public async Task Demo_WithDemoAuthEnabled_RegistersDemoSchemeAndNotNegotiate()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true);
        _ = factory.CreateClient();

        IAuthenticationSchemeProvider schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        AuthenticationScheme? defaultScheme = await schemes.GetDefaultAuthenticateSchemeAsync();

        defaultScheme!.Name.Should().Be(DemoApiAuthentication.SchemeName);
        (await schemes.GetSchemeAsync(DemoApiAuthentication.SchemeName)).Should().NotBeNull();
        (await schemes.GetSchemeAsync(NegotiateDefaults.AuthenticationScheme)).Should().BeNull();
    }

    [Fact]
    public async Task Demo_WithDemoActor_CanCallProtectedHealthEndpoint()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Demo_PersistenceHealth_DistinguishesProcessFromUnconfiguredSql()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        SqlPersistenceHealthResponse response = (await client.GetFromJsonAsync<SqlPersistenceHealthResponse>("/api/v1/health/persistence"))!;

        response.Should().Be(new SqlPersistenceHealthResponse("NotConfigured", false, null));
    }

    [Fact]
    public async Task Demo_SqlOutage_LeavesProcessLivenessIndependentFromPersistenceReadiness()
    {
        using WebApplicationFactory<Program> factory = CreateSqlOutageFactory();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage process = await client.GetAsync("/api/v1/health");
        HttpResponseMessage persistence = await client.GetAsync("/api/v1/health/persistence");
        SqlPersistenceHealthResponse response = (await persistence.Content.ReadFromJsonAsync<SqlPersistenceHealthResponse>())!;

        process.StatusCode.Should().Be(HttpStatusCode.OK);
        persistence.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Should().Be(new SqlPersistenceHealthResponse("Unhealthy", true, OperationalErrorCodes.PersistenceUnavailable));
    }

    [Fact]
    public async Task DemoActor_ReceivesApplicationRoleThroughAccessCompatibilityPath()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        CurrentAccessResponse access = (await client.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;

        access.Should().NotBeNull();
        access.AccessStatus.Should().Be("Approved");
        access.Roles.Should().ContainSingle("Admin");
        access.AuthenticationSource.Should().Be("demo-api-bridge");
    }

    [Fact]
    public async Task Demo_WithoutDemoActor_IsUnauthorized()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NonDemo_WithDemoAuthFlagStillUsesNegotiate()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Staging", demoAuthEnabled: true);
        _ = factory.CreateClient();

        IAuthenticationSchemeProvider schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        AuthenticationScheme? defaultScheme = await schemes.GetDefaultAuthenticateSchemeAsync();

        defaultScheme!.Name.Should().Be(NegotiateDefaults.AuthenticationScheme);
        (await schemes.GetSchemeAsync(NegotiateDefaults.AuthenticationScheme)).Should().NotBeNull();
        (await schemes.GetSchemeAsync(DemoApiAuthentication.SchemeName)).Should().BeNull();
    }

    [Fact]
    public async Task Demo_WithSwaggerExplicitlyEnabled_ExposesAuthenticatedOpenApiWithDemoActorScheme()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true, swaggerEnabled: true);
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string document = await response.Content.ReadAsStringAsync();
        document.Should().Contain("DemoActor").And.Contain("X-SecureOps-Demo-Actor");
    }

    [Fact]
    public async Task Test_WithSwaggerExplicitlyEnabled_ExposesSwaggerIndexAndRequiredJsonPath()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Test", demoAuthEnabled: true, swaggerEnabled: true);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage index = await client.GetAsync("/swagger/index.html");
        HttpResponseMessage document = await client.GetAsync("/swagger/v1/swagger.json");

        index.StatusCode.Should().Be(HttpStatusCode.OK);
        document.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_OpenApiDocument_MatchesCheckedInUiContractSnapshot()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Test", demoAuthEnabled: true, swaggerEnabled: true);
        using HttpClient client = factory.CreateClient();

        string actualJson = await client.GetStringAsync("/swagger/v1/swagger.json");
        string expectedJson = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "contracts", "secureops-api-v1.openapi.json"));

        JsonNode.DeepEquals(JsonNode.Parse(actualJson), JsonNode.Parse(expectedJson)).Should().BeTrue();
    }

    [Fact]
    public async Task Test_WithSwaggerDisabled_DoesNotExposeSwaggerRoutes()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Test", demoAuthEnabled: true, swaggerEnabled: false);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        (await client.GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_WithAnonymousSwaggerDocument_StillProtectsApiOperations()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Test", demoAuthEnabled: true, swaggerEnabled: true);
        using HttpClient client = factory.CreateClient();

        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/identity/lookup", new { account = "sample.user", purpose = "Approved operational lookup" })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AccessUsers_RequiresManageUsersCapability()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true);
        using HttpClient anonymous = factory.CreateClient();
        using HttpClient lead = factory.CreateClient();
        lead.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.TeamLeadActor);
        using HttpClient admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        (await anonymous.GetAsync("/api/v1/access/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        HttpResponseMessage forbidden = await lead.GetAsync("/api/v1/access/users");
        await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, "AccessDenied", "authorization", retryable: false);
        (await admin.GetAsync("/api/v1/access/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AccessMutations_ReturnDistinctValidationConcurrencyAndLifecycleProblems()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(
            "Demo",
            demoAuthEnabled: true,
            demoCompatibilityEnabled: false);
        await SeedAdminAsync(factory, "demo:platform-admin", "demo-api-bridge");
        using HttpClient subject = factory.CreateClient();
        subject.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.TeamLeadActor);
        CurrentAccessResponse pending = (await subject.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;
        using HttpClient admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);
        _ = await admin.GetAsync("/api/v1/access/me");

        HttpResponseMessage validation = await admin.PostAsJsonAsync(
            $"/api/v1/access/requests/{pending.LatestRequest!.Id}/approve",
            new AccessDecisionRequest("Approved for test.", ["Lead"], ExpectedVersion: 0));
        HttpResponseMessage concurrency = await admin.PostAsJsonAsync(
            $"/api/v1/access/requests/{pending.LatestRequest.Id}/approve",
            new AccessDecisionRequest("Approved for test.", ["Lead"], pending.LatestRequest.Version + 1));
        HttpResponseMessage approved = await admin.PostAsJsonAsync(
            $"/api/v1/access/requests/{pending.LatestRequest.Id}/approve",
            new AccessDecisionRequest("Approved for test.", ["Lead"], pending.LatestRequest.Version));
        HttpResponseMessage lifecycle = await admin.PostAsJsonAsync(
            $"/api/v1/access/requests/{pending.LatestRequest.Id}/reject",
            new AccessDecisionRequest("Rejected too late.", null, pending.LatestRequest.Version));

        await AssertProblemAsync(validation, HttpStatusCode.BadRequest, "AccessValidationFailed", "validation", retryable: false);
        await AssertProblemAsync(concurrency, HttpStatusCode.Conflict, "AccessConcurrencyConflict", "concurrency", retryable: true);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertProblemAsync(lifecycle, HttpStatusCode.Conflict, "AccessRequestAlreadyDecided", "lifecycle", retryable: false);
    }

    [Fact]
    public async Task AccessRequestFilter_RejectsUndefinedNumericStatusAsValidation()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", demoAuthEnabled: true);
        using HttpClient admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await admin.GetAsync("/api/v1/access/requests?status=99");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "AccessValidationFailed", "validation", retryable: false);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool demoAuthEnabled,
        bool swaggerEnabled = false,
        bool demoCompatibilityEnabled = true)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("DemoAuth:Enabled", demoAuthEnabled ? "true" : "false");
                builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
                builder.UseSetting("Access:DemoCompatibilityEnabled", demoCompatibilityEnabled ? "true" : "false");
                builder.UseSetting("Audit:Provider", "InMemory");
                builder.UseSetting("IdentityLookup:Provider", "Mock");
                builder.UseSetting("RateLimiting:IdentityLookup:PermitLimit", "100");
                builder.UseSetting("Swagger:Enabled", swaggerEnabled ? "true" : "false");
            });
    }

    private static async Task SeedAdminAsync(
        WebApplicationFactory<Program> factory,
        string identity,
        string authenticationSource)
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
            AccessRequestStatus.Approved,
            ensured.PendingRequest.Version,
            "system:test-seed",
            ["Admin"],
            "Synthetic persisted authorization fixture.",
            CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateSqlOutageFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("Access:RepositoryProvider", "SqlServer");
            builder.UseSetting("SessionSecurity:RepositoryProvider", "SqlServer");
            builder.UseSetting("ConnectionStrings:SecureOpsDb", "Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout=15");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISqlPersistenceProbe>();
                services.AddSingleton<ISqlPersistenceProbe, UnavailableSqlPersistenceProbe>();
            });
        });

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string stage, bool retryable)
    {
        response.StatusCode.Should().Be(status);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Extensions["code"]!.ToString().Should().Be(code);
        problem.Extensions["stage"]!.ToString().Should().Be(stage);
        bool.Parse(problem.Extensions["retryable"]!.ToString()!).Should().Be(retryable);
        problem.Extensions["correlationId"].Should().NotBeNull();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class UnavailableSqlPersistenceProbe : ISqlPersistenceProbe
    {
        public Task ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Synthetic unavailable SQL fixture."));
    }
}
