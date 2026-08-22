using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureOps.Api.Security;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.Api;
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
        fakeServices.GetRequiredService<IJiraClient>().Should().BeOfType<FakeJiraClient>();
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

    [Theory]
    [InlineData("SYN-OR-300")]
    [InlineData("SYN-OR-400")]
    public async Task FakeSource_ClosedOrMissingRecord_IsRejectedBeforeJiraCreate(string orCode)
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        OperationalRecordResponse record = await GetRecordAsync(admin, orCode);
        (await admin.PostAsync($"/api/v1/operational-records/{record.Id}/jira-preview", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage create = await PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/jira", $"synthetic-{orCode}-create");

        await AssertProblemAsync(create, HttpStatusCode.Conflict, OperationalErrorCodes.OperationalRecordNoLongerOpen);
    }

    [Fact]
    public async Task JiraUnknownOutcome_RemainsVisibleAndBlocksCreateReplayAndRetry()
    {
        ScriptedJiraClient jira = new(JiraTestOutcome.Unknown);
        using WebApplicationFactory<Program> factory = CreateFactory(jiraClient: jira);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        OperationalRecordResponse record = await GetRecordAsync(admin, "SYN-OR-100");
        (await admin.PostAsync($"/api/v1/operational-records/{record.Id}/jira-preview", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage first = await PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/jira", "synthetic-unknown-create");
        ProblemDetails firstProblem = await AssertProblemAsync(first, HttpStatusCode.ServiceUnavailable, OperationalErrorCodes.JiraUnavailable);
        OperationalRecordResponse detail = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{record.Id}"))!;
        OperationalRecordResponse refreshed = (await admin.GetFromJsonAsync<OperationalRecordResponse[]>("/api/v1/operational-records"))!
            .Single(item => item.Id == record.Id);
        HttpResponseMessage replay = await PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/jira", "synthetic-unknown-create");
        HttpResponseMessage secondCreate = await PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/jira", "synthetic-unknown-new-create");
        HttpResponseMessage retry = await PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/retry", "synthetic-unknown-retry");

        bool.Parse(firstProblem.Extensions["retryable"]!.ToString()!).Should().BeFalse();
        detail.WorkflowState.Should().Be(OperationalRecordWorkflowState.JiraCreateFailed);
        detail.ReconciliationRequired.Should().BeTrue();
        detail.RetryEligible.Should().BeFalse();
        detail.JiraExists.Should().BeFalse();
        refreshed.WorkflowState.Should().Be(OperationalRecordWorkflowState.JiraCreateFailed);
        refreshed.ReconciliationRequired.Should().BeTrue();
        await AssertProblemAsync(replay, HttpStatusCode.ServiceUnavailable, OperationalErrorCodes.JiraUnavailable);
        await AssertProblemAsync(secondCreate, HttpStatusCode.Conflict, OperationalErrorCodes.WorkflowConflict);
        await AssertProblemAsync(retry, HttpStatusCode.Conflict, OperationalErrorCodes.WorkflowAlreadyInProgress);
        jira.Calls.Should().Be(1);
    }

    [Fact]
    public async Task JiraSafeRetryableFailure_RetryCompletesWithoutBypassingWorkflow()
    {
        ScriptedJiraClient jira = new(JiraTestOutcome.RetryableFailure, JiraTestOutcome.Success);
        using WebApplicationFactory<Program> factory = CreateFactory(jiraClient: jira);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        OperationalRecordResponse record = await GetRecordAsync(admin, "SYN-OR-100");
        (await admin.PostAsync($"/api/v1/operational-records/{record.Id}/jira-preview", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage first = await PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/jira", "synthetic-retryable-create");
        ProblemDetails firstProblem = await AssertProblemAsync(first, HttpStatusCode.ServiceUnavailable, OperationalErrorCodes.JiraUnavailable);
        OperationalRecordResponse failed = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{record.Id}"))!;
        HttpResponseMessage retry = await PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/retry", "synthetic-permitted-retry");
        OperationalRecordResponse completed = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{record.Id}"))!;

        bool.Parse(firstProblem.Extensions["retryable"]!.ToString()!).Should().BeTrue();
        failed.WorkflowState.Should().Be(OperationalRecordWorkflowState.JiraCreateFailed);
        failed.ReconciliationRequired.Should().BeFalse();
        failed.RetryEligible.Should().BeTrue();
        failed.JiraExists.Should().BeFalse();
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        completed.WorkflowState.Should().Be(OperationalRecordWorkflowState.Completed);
        completed.JiraIssueKey.Should().Be("FAKE-1");
        completed.JiraExists.Should().BeTrue();
        completed.ReconciliationRequired.Should().BeFalse();
        jira.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentCreate_SecondActorCannotOverwriteActiveClaim()
    {
        CoordinatedJiraClient jira = new();
        using WebApplicationFactory<Program> factory = CreateFactory(jiraClient: jira);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        OperationalRecordResponse record = await GetRecordAsync(admin, "SYN-OR-100");
        (await admin.PostAsync($"/api/v1/operational-records/{record.Id}/jira-preview", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        Task<HttpResponseMessage> firstTask = PostCommandAsync(admin, $"/api/v1/operational-records/{record.Id}/jira", "synthetic-overlap-a");
        OperationalRecordResponse during;
        HttpResponseMessage conflict;
        OperationalRecordResponse afterConflict;
        try
        {
            await jira.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            during = (await lead.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{record.Id}"))!;
            conflict = await PostCommandAsync(lead, $"/api/v1/operational-records/{record.Id}/jira", "synthetic-overlap-b");
            afterConflict = (await lead.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{record.Id}"))!;
        }
        finally
        {
            jira.Release.TrySetResult();
        }

        HttpResponseMessage first = await firstTask;

        during.Claimed.Should().BeTrue();
        during.ClaimedBy.Should().Be("demo:platform-admin");
        during.ClaimExpiresAt.Should().NotBeNull();
        during.WorkflowState.Should().Be(OperationalRecordWorkflowState.CreatingJira);
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict, OperationalErrorCodes.WorkflowAlreadyInProgress);
        afterConflict.ClaimedBy.Should().Be(during.ClaimedBy);
        afterConflict.ClaimExpiresAt.Should().Be(during.ClaimExpiresAt);
        afterConflict.Version.Should().Be(during.Version);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        jira.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ExpiredClaim_IsInactiveAndCanBeRecoveredByAnotherActor()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));
        using WebApplicationFactory<Program> factory = CreateFactory(timeProvider: time);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        OperationalRecordResponse record = await GetRecordAsync(admin, "SYN-OR-100");
        IOperationalRecordRepository repository = factory.Services.GetRequiredService<IOperationalRecordRepository>();
        WorkflowClaimResult first = await repository.TryClaimAsync(record.Id, "demo:platform-admin", TimeSpan.FromSeconds(30), "synthetic-claim-a", CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(31));

        OperationalRecordResponse expired = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{record.Id}"))!;
        WorkflowClaimResult recovered = await repository.TryClaimAsync(record.Id, "demo:team-lead", TimeSpan.FromSeconds(30), "synthetic-claim-b", CancellationToken.None);
        OperationalRecordResponse active = (await admin.GetFromJsonAsync<OperationalRecordResponse>($"/api/v1/operational-records/{record.Id}"))!;

        first.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        expired.Claimed.Should().BeFalse();
        expired.ClaimedBy.Should().Be("demo:platform-admin");
        recovered.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        active.Claimed.Should().BeTrue();
        active.ClaimedBy.Should().Be("demo:team-lead");
        active.Version.Should().BeGreaterThan(expired.Version);
    }

    [Fact]
    public async Task DisabledSourceProvider_ReturnsFailClosedProblem()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(sourceProvider: "Disabled");
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await admin.GetAsync("/api/v1/operational-records");

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, OperationalErrorCodes.OperationalSourceUnavailable);
    }

    [Fact]
    public async Task UnsupportedRealSourceProvider_FailsStartup()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(sourceProvider: "TuruncuHat");

        Func<Task> act = async () =>
        {
            using HttpClient client = factory.CreateClient();
            _ = await client.GetAsync("/api/v1/health");
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SourceProvider*not implemented*");
    }

    [Fact]
    public async Task OperationalRecordEnums_AreSerializedWithFrozenV1NumericValues()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        using var document = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/operational-records"));
        JsonElement record = document.RootElement.EnumerateArray().Single(item => item.GetProperty("orCode").GetString() == "SYN-OR-100");

        record.GetProperty("classification").GetInt32().Should().Be(4);
        record.GetProperty("workflowState").GetInt32().Should().Be(3);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string sourceProvider = "Fake",
        IJiraClient? jiraClient = null,
        TimeProvider? timeProvider = null) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("OperationalRecords:SourceProvider", sourceProvider);
            builder.UseSetting("OperationalRecords:RepositoryProvider", "InMemory");
            builder.UseSetting("Jira:Provider", "Fake");
            builder.UseSetting("RateLimiting:OperationalRecordRefresh:PermitLimit", "100");
            builder.UseSetting("RateLimiting:JiraPreview:PermitLimit", "100");
            builder.UseSetting("RateLimiting:JiraCreate:PermitLimit", "100");
            builder.UseSetting("RateLimiting:WorkflowRetry:PermitLimit", "100");
            if (jiraClient is not null || timeProvider is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    if (jiraClient is not null)
                    {
                        services.RemoveAll<IJiraClient>();
                        services.AddSingleton(jiraClient);
                    }

                    if (timeProvider is not null)
                    {
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton(timeProvider);
                    }
                });
            }
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }

    private static async Task<OperationalRecordResponse> GetRecordAsync(HttpClient client, string orCode) =>
        (await client.GetFromJsonAsync<OperationalRecordResponse[]>("/api/v1/operational-records"))!.Single(record => record.OrCode == orCode);

    private static async Task<HttpResponseMessage> PostCommandAsync(HttpClient client, string path, string idempotencyKey)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static ServiceProvider Services(string sourceProvider)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OperationalRecords:SourceProvider"] = sourceProvider,
            ["Jira:Provider"] = sourceProvider == "Fake" ? "Fake" : "Disabled",
            ["Audit:Provider"] = "InMemory"
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecureOpsInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<ProblemDetails> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Extensions["code"]!.ToString().Should().Be(code);
        return problem;
    }

    private enum JiraTestOutcome
    {
        Success,
        RetryableFailure,
        Unknown
    }

    private sealed class ScriptedJiraClient(params JiraTestOutcome[] outcomes) : IJiraClient
    {
        private readonly Queue<JiraTestOutcome> _outcomes = new(outcomes);
        private readonly object _gate = new();

        public int Calls { get; private set; }

        public Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JiraTestOutcome outcome;
            lock (_gate)
            {
                Calls++;
                outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : JiraTestOutcome.Success;
            }

            return outcome switch
            {
                JiraTestOutcome.Success => Task.FromResult(new JiraIssueCreationResult("FAKE-1")),
                JiraTestOutcome.RetryableFailure => Task.FromException<JiraIssueCreationResult>(
                    new ExternalIntegrationException(OperationalErrorCodes.JiraUnavailable, true)),
                JiraTestOutcome.Unknown => Task.FromException<JiraIssueCreationResult>(
                    new ExternalIntegrationException(OperationalErrorCodes.JiraUnavailable, true, outcomeUnknown: true)),
                _ => throw new InvalidOperationException("Unsupported synthetic Jira test outcome.")
            };
        }
    }

    private sealed class CoordinatedJiraClient : IJiraClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new JiraIssueCreationResult("FAKE-1");
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
