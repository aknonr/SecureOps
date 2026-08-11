using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Api.Security;

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
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.TeamLeadActor);

        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string document = await response.Content.ReadAsStringAsync();
        document.Should().Contain("DemoActor").And.Contain("X-SecureOps-Demo-Actor");
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, bool demoAuthEnabled, bool swaggerEnabled = false)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("DemoAuth:Enabled", demoAuthEnabled ? "true" : "false");
                builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
                builder.UseSetting("Audit:Provider", "InMemory");
                builder.UseSetting("IdentityLookup:Provider", "Mock");
                builder.UseSetting("IdentityLookup:RateLimit:PermitLimit", "100");
                builder.UseSetting("Swagger:Enabled", swaggerEnabled ? "true" : "false");
            });
    }
}
