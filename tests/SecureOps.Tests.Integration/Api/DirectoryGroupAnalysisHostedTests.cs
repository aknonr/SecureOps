using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Api.Security;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Tests.Integration.Api;

public sealed class DirectoryGroupAnalysisHostedTests
{
    [Fact]
    public async Task AdminCanAnalyzeAndExportButLeadCannotCrossMemberBoundary()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        var analysisRequest = new { group = "dist-universal" };
        var exportRequest = new { group = "dist-universal", mode = "DirectMembers", format = "Csv" };

        HttpResponseMessage analysis = await admin.PostAsJsonAsync(
            "/api/v1/directory/groups/analysis", analysisRequest);
        HttpResponseMessage export = await admin.PostAsJsonAsync(
            "/api/v1/directory/groups/export", exportRequest);
        HttpResponseMessage forbiddenAnalysis = await lead.PostAsJsonAsync(
            "/api/v1/directory/groups/analysis", analysisRequest);
        HttpResponseMessage forbiddenExport = await lead.PostAsJsonAsync(
            "/api/v1/directory/groups/export", exportRequest);

        analysis.StatusCode.Should().Be(HttpStatusCode.OK);
        DirectoryGroupAnalysisResponse? response = await analysis.Content.ReadFromJsonAsync<DirectoryGroupAnalysisResponse>();
        response!.Overview.Category.Should().Be("Distribution");
        response.DirectMembers.Should().BeEmpty();
        response.DirectMembersIncludePrimaryGroupMembers.Should().BeFalse();
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        forbiddenAnalysis.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbiddenExport.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GroupAnalysisHasIndependentActorRateLimit()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(analysisLimit: 1);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage first = await admin.PostAsJsonAsync(
            "/api/v1/directory/groups/analysis", new { group = "dist-universal", purpose = "first" });
        HttpResponseMessage second = await admin.PostAsJsonAsync(
            "/api/v1/directory/groups/analysis", new { group = "dist-universal", purpose = "different" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var problem = JsonNode.Parse(await second.Content.ReadAsStringAsync());
        problem!["code"]!.GetValue<string>().Should().Be(OperationalErrorCodes.RateLimitExceeded);
    }

    [Fact]
    public async Task InvalidExportModeReturnsStableValidationProblem()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/directory/groups/export",
            new { group = "dist-universal", mode = "RecursiveMaybe", format = "Csv" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        problem!["code"]!.GetValue<string>().Should().Be(OperationalErrorCodes.DirectoryInvalidInput);
    }

    [Fact]
    public async Task OpenApiContainsAdditiveGroupAnalysisContracts()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(swagger: true);
        using HttpClient client = factory.CreateApiClient();

        string openApi = await client.GetStringAsync("/swagger/v1/swagger.json");

        openApi.Should().Contain("/api/v1/directory/groups/analysis")
            .And.Contain("/api/v1/directory/groups/export")
            .And.Contain(nameof(DirectoryGroupAnalysisResponse))
            .And.Contain(nameof(DirectoryGroupExportRequest))
            .And.Contain("Deprecated optional legacy event reference");
    }

    private static WebApplicationFactory<Program> CreateFactory(int analysisLimit = 100, bool swagger = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("IdentityLookup:EnableUpnLookup", "true");
            builder.UseSetting("RateLimiting:DirectoryGroupAnalysis:PermitLimit", analysisLimit.ToString());
            builder.UseSetting("RateLimiting:DirectoryGroupExport:PermitLimit", "100");
            builder.UseSetting("Swagger:Enabled", swagger ? "true" : "false");
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }
}
