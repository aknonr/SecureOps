using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Tests.Integration.Api;

public sealed class ManagementReportingHostedTests
{
    [Fact]
    public async Task Summary_RequiresSeparateManagementReportingCapability()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient anonymous = factory.CreateClient();
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage anonymousResponse = await anonymous.GetAsync("/api/v1/reporting/management/summary?window=7d", TestContext.Current.CancellationToken);
        HttpResponseMessage leadResponse = await lead.GetAsync("/api/v1/reporting/management/summary?window=7d", TestContext.Current.CancellationToken);
        HttpResponseMessage adminResponse = await admin.GetAsync("/api/v1/reporting/management/summary?window=7d", TestContext.Current.CancellationToken);

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertProblemAsync(leadResponse, HttpStatusCode.Forbidden, "AccessDenied");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ManagementReportResponse report = (await adminResponse.Content.ReadFromJsonAsync<ManagementReportResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        report.IdentityLookup.TotalLookups.Should().Be(0);
        report.SecurityAndQuality.RateLimitEvents.Should().BeNull();
        report.Coverage.CoverageComplete.Should().BeTrue();
        report.Limitations.Should().Contain(item =>
            item.Code == ManagementReportingLimitationCodes.RateLimitRejectionsUnavailable);
    }

    [Fact]
    public async Task Summary_InvalidCustomWindow_ReturnsStableValidationProblem()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await admin.GetAsync("/api/v1/reporting/management/summary?window=custom", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "ReportingValidationFailed");
    }

    [Fact]
    public async Task Operators_UsesBoundedServerPagination()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await admin.GetAsync("/api/v1/reporting/management/operators?window=30d&page=3&pageSize=100", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        OperatorActivityPageResponse page = (await response.Content.ReadFromJsonAsync<OperatorActivityPageResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        page.Page.Should().Be(3);
        page.PageSize.Should().Be(100);
        page.TotalItems.Should().Be(250);
        page.Coverage.CoverageComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Operators_RejectsPageSizeAboveServerMaximum()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await admin.GetAsync("/api/v1/reporting/management/operators?pageSize=101", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "ReportingValidationFailed");
    }

    [Fact]
    public async Task Summary_WhenEvidenceIsInMemory_ReturnsSummaryMarkedNonDurable()
    {
        using WebApplicationFactory<Program> factory = CreateUnconfiguredFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await admin.GetAsync("/api/v1/reporting/management/summary?window=7d", TestContext.Current.CancellationToken);
        JsonNode report = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        report["limitations"]!.AsArray().Select(item => item!["code"]!.GetValue<string>())
            .Should().Contain("NonDurableReportingSource");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("OperationalRecords:SourceProvider", "Disabled");
            builder.UseSetting("OperationalRecords:RepositoryProvider", "InMemory");
            builder.UseSetting("Jira:Provider", "Disabled");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IManagementReportingRepository>();
                services.AddSingleton<IManagementReportingRepository, StubReportingRepository>();
            });
        });

    private static WebApplicationFactory<Program> CreateUnconfiguredFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Access:RepositoryProvider", "InMemory");
            builder.UseSetting("SessionSecurity:RepositoryProvider", "InMemory");
            builder.UseSetting("DataProtection:Mode", "Ephemeral");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("OperationalRecords:SourceProvider", "Disabled");
            builder.UseSetting("OperationalRecords:RepositoryProvider", "InMemory");
            builder.UseSetting("Jira:Provider", "Disabled");
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Extensions["code"]!.ToString().Should().Be(code);
    }

    private sealed class StubReportingRepository : IManagementReportingRepository
    {
        public Task<ManagementReportingData> GetSummaryAsync(ReportingWindow window, CancellationToken cancellationToken) =>
            Task.FromResult(new ManagementReportingData(
                [], [], 0, new ReportingActiveUsers(0, 0, 0, 0), 0,
                new ReportingRetryOutcomes(0, 0, 0), [], window.FromInclusiveUtc.AddTicks(-1)));

        public Task<OperatorActivityDataPage> GetOperatorActivityAsync(
            ReportingWindow window,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OperatorActivityDataPage(250, [], window.FromInclusiveUtc.AddTicks(-1)));
    }
}
