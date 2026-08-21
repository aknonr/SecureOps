using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Api.Security;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Tests.Integration.Api;

public sealed class OperationalRecordWorkflowHostedTests
{
    [Fact]
    public void SourceProviderConfiguration_SelectsExplicitImplementation()
    {
        using ServiceProvider fakeServices = Services("Fake");
        using ServiceProvider disabledServices = Services("Disabled");

        fakeServices.GetRequiredService<IOperationalRecordClient>().Should().BeOfType<FakeOperationalRecordClient>();
        fakeServices.GetRequiredService<IOperationalRecordClassifier>().Should().BeOfType<FakeOperationalRecordClassifier>();
        fakeServices.GetRequiredService<IRequesterResolver>().Should().BeOfType<FakeRequesterResolver>();
        disabledServices.GetRequiredService<IOperationalRecordClient>().Should().BeOfType<DisabledOperationalRecordClient>();
        disabledServices.GetRequiredService<IOperationalRecordClassifier>().Should().BeOfType<ManualReviewOperationalRecordClassifier>();
        disabledServices.GetRequiredService<IRequesterResolver>().Should().BeOfType<UnresolvedRequesterResolver>();
    }

    [Fact]
    public async Task FakeSource_ListDetailPreviewCreate_UsesRealWorkflowStateMachine()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient anonymous = factory.CreateClient();
        using HttpClient admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);

        (await anonymous.GetAsync("/api/v1/operational-records")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        OperationalRecordResponse[] records = (await admin.GetFromJsonAsync<OperationalRecordResponse[]>("/api/v1/operational-records"))!;
        records.Should().HaveCount(4);
        OperationalRecordResponse eligible = records.Single(record => record.OrCode == "SYN-OR-100");
        eligible.WorkflowState.Should().Be(OperationalRecordWorkflowState.Eligible);
        eligible.JiraEligible.Should().BeTrue();
        eligible.Claimed.Should().BeFalse();
        eligible.ReconciliationRequired.Should().BeFalse();

        OperationalRecordResponse detail = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{eligible.Id}"))!;
        detail.Should().BeEquivalentTo(eligible);

        HttpResponseMessage preview = await admin.PostAsync($"/api/v1/operational-records/{eligible.Id}/jira-preview", null);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpRequestMessage createRequest = new(HttpMethod.Post, $"/api/v1/operational-records/{eligible.Id}/jira");
        createRequest.Headers.Add("Idempotency-Key", "synthetic-e2e-create");
        HttpResponseMessage create = await admin.SendAsync(createRequest);
        create.StatusCode.Should().Be(HttpStatusCode.OK);

        OperationalRecordResponse completed = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{eligible.Id}"))!;
        completed.WorkflowState.Should().Be(OperationalRecordWorkflowState.Completed);
        completed.JiraExists.Should().BeTrue();
        completed.JiraIssueKey.Should().Be("FAKE-1");
        completed.RetryEligible.Should().BeFalse();
        completed.Claimed.Should().BeFalse();

        using HttpRequestMessage duplicateRequest = new(HttpMethod.Post, $"/api/v1/operational-records/{eligible.Id}/jira");
        duplicateRequest.Headers.Add("Idempotency-Key", "synthetic-e2e-duplicate");
        HttpResponseMessage duplicate = await admin.SendAsync(duplicateRequest);
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "WorkflowAlreadyCompleted");
    }

    [Fact]
    public async Task FakeSource_ChangedRecord_IsRejectedBeforeJiraCreate()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);
        OperationalRecordResponse[] records = (await admin.GetFromJsonAsync<OperationalRecordResponse[]>("/api/v1/operational-records"))!;
        OperationalRecordResponse stale = records.Single(record => record.OrCode == "SYN-OR-200");
        (await admin.PostAsync($"/api/v1/operational-records/{stale.Id}/jira-preview", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage create = await admin.PostAsync($"/api/v1/operational-records/{stale.Id}/jira", null);

        await AssertProblemAsync(create, HttpStatusCode.Conflict, "OperationalRecordChanged");
        OperationalRecordResponse detail = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{stale.Id}"))!;
        detail.JiraExists.Should().BeFalse();
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("OperationalRecords:SourceProvider", "Fake");
            builder.UseSetting("OperationalRecords:RepositoryProvider", "InMemory");
            builder.UseSetting("RateLimiting:OperationalRecordRefresh:PermitLimit", "100");
            builder.UseSetting("RateLimiting:JiraPreview:PermitLimit", "100");
            builder.UseSetting("RateLimiting:JiraCreate:PermitLimit", "100");
        });

    private static ServiceProvider Services(string sourceProvider)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = sourceProvider,
            ["Audit:Provider"] = "InMemory"
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecureOpsInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Extensions["code"]!.ToString().Should().Be(code);
    }
}
