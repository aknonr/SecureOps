using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Api.Security;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Api;

public sealed class ApiRateLimitingHostedTests
{
    [Fact]
    public async Task IdentityLimit_ReturnsSafeProblemDetailsWithCorrelation()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("DemoAuth:Enabled", "true");
                builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
                builder.UseSetting("Audit:Provider", "InMemory");
                builder.UseSetting("IdentityLookup:Provider", "Mock");
                builder.UseSetting("RateLimiting:IdentityLookup:PermitLimit", "1");
                builder.UseSetting("RateLimiting:IdentityLookup:WindowSeconds", "60");
            });
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);
        object request = new { account = "sample.user", purpose = "Approved synthetic rate-limit test" };

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/v1/identity/lookup", request);
        HttpResponseMessage second = await client.PostAsJsonAsync("/api/v1/identity/lookup", request);

        first.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("RateLimitExceeded");
        problem.RootElement.GetProperty("stage").GetString().Should().Be("rate-limit");
        problem.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GlobalLimit_IsPerActor_SoOneBusyUserDoesNotThrottleAnother()
    {
        using WebApplicationFactory<Program> factory = Factory(("RateLimiting:Global:PermitLimit", "2"));
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);

        (await admin.GetAsync("/api/v1/access/me")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await admin.GetAsync("/api/v1/access/me")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        HttpResponseMessage limited = await admin.GetAsync("/api/v1/access/me");

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("RateLimitExceeded");
        (await lead.GetAsync("/api/v1/access/me")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SessionRevocation_UsesTheAccessAdministrationLimit()
    {
        using WebApplicationFactory<Program> factory = Factory(("RateLimiting:AccessAdministration:PermitLimit", "1"));
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        object request = new { sessionId = Guid.NewGuid(), reason = "Synthetic rate-limit test" };

        HttpResponseMessage first = await admin.PostAsJsonAsync("/api/v1/sessions/revoke", request);
        HttpResponseMessage second = await admin.PostAsJsonAsync("/api/v1/sessions/revoke", request);

        first.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(10, 0)]
    public void Validate_RejectsNonPositiveLimits(int permitLimit, int windowSeconds)
    {
        RateLimitingOptions options = new() { Global = new OperationRateLimitOptions(permitLimit, windowSeconds) };

        Action act = () => ApiRateLimits.Validate(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*RateLimiting:Global*");
        ApiRateLimits.Validate(new RateLimitingOptions());
    }

    private static WebApplicationFactory<Program> Factory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            foreach ((string key, string value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }
}
