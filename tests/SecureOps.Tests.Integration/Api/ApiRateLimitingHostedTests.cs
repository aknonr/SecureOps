using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Api.Security;

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
        using HttpClient client = factory.CreateApiClient();
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
}
