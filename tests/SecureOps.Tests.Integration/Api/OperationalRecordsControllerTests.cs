using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureOps.Api.Controllers;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Tests.Integration.Api;

public sealed class OperationalRecordsControllerTests
{
    [Fact]
    public async Task GetAsync_SdmEvaluation_ExposesAdditiveNumericContractAndSafeDefaults()
    {
        OperationalRecord record = Record();
        OperationalRecordResponse absent = await GetResponseAsync(CreateController(record), record.Id);
        absent.RecommendedClassification.Should().BeNull();
        absent.RuleSetVersion.Should().BeNull();
        absent.EvaluatedAt.Should().BeNull();
        absent.EvaluationStale.Should().BeTrue();
        absent.ExternalWriteEligible.Should().BeFalse();
        record = SdmEvaluationEvidence.Apply(record, new SdmEvaluationInput(new string('a', 64),
            ProviderSupported: true, Active: true, ValidId: true, ValidCode: true, ValidTitle: true, ValidDescription: true), Now);
        OperationalRecordResponse response = await GetResponseAsync(CreateController(record, true), record.Id);
        response.RuleSetVersion.Should().Be(SdmEvaluator.RuleSetVersion);
        response.ReasonCodes.Should().Equal(record.SdmEvaluation!.Result.ReasonCodes);
        response.BlockingConditions.Should().Equal(record.SdmEvaluation.Result.BlockingConditions);
        response.EvaluatedAt.Should().Be(Now);
        response.ReadOnlyIntegrationMode.Should().BeTrue();
        response.SdmCandidateRecommended.Should().BeFalse();
        response.JiraEligible.Should().BeFalse();
        System.Text.Json.JsonSerializerOptions options = new(System.Text.Json.JsonSerializerDefaults.Web);
        string json = System.Text.Json.JsonSerializer.Serialize(response, options);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        document.RootElement.GetProperty("recommendedClassification").GetInt32().Should().Be(6);
        document.RootElement.GetProperty("externalWriteEligible").GetBoolean().Should().BeFalse();
        System.Text.Json.JsonSerializer.Deserialize<OperationalRecordResponse>(json, options).Should().BeEquivalentTo(response);
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PreviewAsync_WhenRequesterIsAmbiguous_ReturnsSafeProblemDetails()
    {
        OperationalRecordsController controller = CreateController(
            OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.RequesterResolutionAmbiguous, "requester-resolution", false));

        ActionResult<JiraPreviewResponse> result = await controller.PreviewAsync(Guid.NewGuid(), CancellationToken.None);

        ObjectResult response = result.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        ProblemDetails problem = response.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(OperationalErrorCodes.RequesterResolutionAmbiguous);
        problem.Extensions["stage"].Should().Be("requester-resolution");
        problem.Extensions["retryable"].Should().Be(false);
        problem.Extensions["correlationId"].Should().Be("trace-operational-test");
        problem.Extensions.Should().NotContainKey("exception").And.NotContainKey("response");
    }

    [Fact]
    public async Task PreviewAsync_WhenRequesterResolutionFails_ReturnsObserved422Contract()
    {
        OperationalRecordsController controller = CreateController(
            OperationalRecordResult<JiraIssueDraft>.Fail(
                OperationalErrorCodes.RequesterResolutionFailed,
                "requester-resolution",
                false));

        ActionResult<JiraPreviewResponse> result = await controller.PreviewAsync(Guid.NewGuid(), CancellationToken.None);

        ObjectResult response = result.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        ProblemDetails problem = response.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(OperationalErrorCodes.RequesterResolutionFailed);
        problem.Extensions["stage"].Should().Be("requester-resolution");
        problem.Extensions["retryable"].Should().Be(false);
    }

    [Fact]
    public async Task PreviewAsync_InReadOnlyMode_ExposesVerifiedAuthenticatedOperatorReporter()
    {
        JiraIssueDraft draft = new(
            Guid.NewGuid(),
            "OR-SYNTHETIC",
            "SAFE",
            "Task",
            "Synthetic summary",
            "Synthetic description",
            "jira-requester",
            "mapping-v1",
            new string('a', 64),
            [],
            new JiraIssueFieldMapping(
                "3",
                "customfield_team",
                "WASAS",
                "customfield_requester",
                ["SunucuTalep"]),
            ReporterUsername: "jira-operator");
        OperationalRecordsController controller = CreateController(
            OperationalRecordResult<JiraIssueDraft>.Success(draft),
            readOnlyIntegrationMode: true);

        ActionResult<JiraPreviewResponse> result = await controller.PreviewAsync(draft.OperationalRecordId, CancellationToken.None);

        JiraPreviewResponse response = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<JiraPreviewResponse>().Subject;
        response.ReadOnlyIntegrationMode.Should().BeTrue();
        response.ReporterUsername.Should().Be("jira-operator");
        response.RequesterAccountId.Should().Be("jira-requester");
        response.IssueTypeId.Should().Be("3");
        response.TeamCustomField.Should().Be("customfield_team");
        response.TeamValue.Should().Be("WASAS");
        response.RequesterWatcherCustomField.Should().Be("customfield_requester");
        response.Labels.Should().Equal("SunucuTalep");
    }

    [Fact]
    public async Task PreviewAsync_WhenOperatorReporterResolutionFails_ReturnsStable422Contract()
    {
        OperationalRecordsController controller = CreateController(
            OperationalRecordResult<JiraIssueDraft>.Fail(
                OperationalErrorCodes.OperatorReporterResolutionFailed,
                "operator-reporter-resolution",
                false));

        ActionResult<JiraPreviewResponse> result = await controller.PreviewAsync(Guid.NewGuid(), CancellationToken.None);

        ObjectResult response = result.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        ProblemDetails problem = response.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(OperationalErrorCodes.OperatorReporterResolutionFailed);
        problem.Extensions["stage"].Should().Be("operator-reporter-resolution");
    }

    [Fact]
    public async Task PreviewAsync_UsesAuthenticatedPrincipalAsAuthoritativeReporterInput()
    {
        JiraIssueDraft draft = new(
            Guid.NewGuid(), "OR-SYNTHETIC", "SAFE", "Task", "Summary", "Description",
            null, "mapping-v1", new string('a', 64), [],
            new JiraIssueFieldMapping("3", "customfield_team", "WASAS", "customfield_requester", ["SunucuTalep"]));
        CapturingTransferService transfer = new(OperationalRecordResult<JiraIssueDraft>.Success(draft));
        DefaultHttpContext httpContext = new()
        {
            TraceIdentifier = "trace-operational-test",
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "SYNTHETIC\\operator.one")],
                "Synthetic"))
        };
        OperationalRecordsController controller = new(
            new EmptyRecordService(),
            transfer,
            Options.Create(new CommandIdempotencyOptions()),
            Options.Create(new OperationalRecordsOptions()),
            Options.Create(new JiraIntegrationOptions()),
            new FixedTimeProvider(Now))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        _ = await controller.PreviewAsync(draft.OperationalRecordId, CancellationToken.None);

        transfer.Context.Should().NotBeNull();
        transfer.Context!.Actor.Should().Be("SYNTHETIC\\operator.one");
        typeof(OperationalRecordsController).GetMethod(nameof(OperationalRecordsController.PreviewAsync))!
            .GetParameters().Select(parameter => parameter.Name)
            .Should().NotContain("reporterUsername");
    }

    [Fact]
    public async Task WriteEndpoints_InReadOnlyIntegrationMode_ReturnStableConflictBeforeTransferService()
    {
        OperationalRecordsController controller = CreateController(Record(), readOnlyIntegrationMode: true);

        ActionResult<JiraTransferResponse> create = await controller.CreateAsync(
            Guid.NewGuid(),
            null,
            CancellationToken.None);
        ActionResult<JiraTransferResponse> retry = await controller.RetryAsync(
            Guid.NewGuid(),
            null,
            CancellationToken.None);

        foreach (ObjectResult response in new[]
                 {
                     create.Result.Should().BeOfType<ObjectResult>().Subject,
                     retry.Result.Should().BeOfType<ObjectResult>().Subject
                 })
        {
            response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
            ProblemDetails problem = response.Value.Should().BeOfType<ProblemDetails>().Subject;
            problem.Extensions["code"].Should().Be(OperationalErrorCodes.ExternalWritesDisabled);
            problem.Extensions["stage"].Should().Be("external-write-fence");
            problem.Extensions["retryable"].Should().Be(false);
        }
    }

    [Fact]
    public async Task GetAsync_InReadOnlyIntegrationMode_ExposesOperatorModeContract()
    {
        OperationalRecord record = Record();
        OperationalRecordsController controller = CreateController(record, readOnlyIntegrationMode: true);

        OperationalRecordResponse response = await GetResponseAsync(controller, record.Id);

        response.ReadOnlyIntegrationMode.Should().BeTrue();
        response.ReadOnlyNotice.Should().Be(ExternalIntegrationNotices.RealDataReadOnly);
        response.SimulationMode.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithNoClaim_ProjectsUnclaimedState()
    {
        OperationalRecord record = Record();
        OperationalRecordsController controller = CreateController(record);

        OperationalRecordResponse response = await GetResponseAsync(controller, record.Id);

        response.Claimed.Should().BeFalse();
        response.ClaimedBy.Should().BeNull();
        response.ClaimedAt.Should().BeNull();
        response.ClaimExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WithActiveChangedClaim_ProjectsLatestOwnerAndLease()
    {
        OperationalRecord record = Record() with
        {
            ClaimedBy = "operator-b",
            ClaimedAt = Now.AddMinutes(-1),
            ClaimExpiresAt = Now.AddMinutes(1),
            Version = 7
        };
        OperationalRecordsController controller = CreateController(record);

        OperationalRecordResponse response = await GetResponseAsync(controller, record.Id);

        response.Claimed.Should().BeTrue();
        response.ClaimedBy.Should().Be("operator-b");
        response.ClaimedAt.Should().Be(Now.AddMinutes(-1));
        response.ClaimExpiresAt.Should().Be(Now.AddMinutes(1));
        response.Version.Should().Be(7);
    }

    [Fact]
    public async Task GetAsync_WithExpiredClaim_PreservesOwnerMetadataButMarksClaimInactive()
    {
        OperationalRecord record = Record() with
        {
            ClaimedBy = "operator-a",
            ClaimedAt = Now.AddMinutes(-3),
            ClaimExpiresAt = Now.AddSeconds(-1)
        };
        OperationalRecordsController controller = CreateController(record);

        OperationalRecordResponse response = await GetResponseAsync(controller, record.Id);

        response.Claimed.Should().BeFalse();
        response.ClaimedBy.Should().Be("operator-a");
        response.ClaimExpiresAt.Should().Be(Now.AddSeconds(-1));
    }

    [Fact]
    public async Task GetAsync_JiraOnly_IsSourceOpenNotCompletedOrInFlight()
    {
        OperationalRecord record = Record() with
        {
            WorkflowState = OperationalRecordWorkflowState.JiraCreated,
            JiraIssueKey = "FAKE-1",
            SourceCloseRequested = false
        };
        OperationalRecordResponse response = await GetResponseAsync(CreateController(record), record.Id);
        response.PresentationState.Should().Be(OperationalRecordPresentationStates.SourceOpen);
        response.RetryEligible.Should().BeFalse();
    }

    [Theory]
    [InlineData(OperationalRecordWorkflowState.JiraCreateFailed, null, false, true, false)]
    [InlineData(OperationalRecordWorkflowState.JiraCreateFailed, null, true, false, false)]
    [InlineData(OperationalRecordWorkflowState.OperationalRecordCloseFailed, "FAKE-1", false, true, true)]
    [InlineData(OperationalRecordWorkflowState.ClosingOperationalRecord, "FAKE-1", false, true, true)]
    [InlineData(OperationalRecordWorkflowState.Completed, "FAKE-1", false, false, true)]
    public async Task GetAsync_ProjectsAuthoritativeRetryReconciliationAndJiraState(
        OperationalRecordWorkflowState state,
        string? jiraIssueKey,
        bool reconciliationRequired,
        bool retryEligible,
        bool jiraExists)
    {
        OperationalRecord record = Record() with
        {
            WorkflowState = state,
            JiraIssueKey = jiraIssueKey,
            SourceCloseRequested = true,
            ReconciliationRequired = reconciliationRequired
        };
        OperationalRecordsController controller = CreateController(record);

        OperationalRecordResponse response = await GetResponseAsync(controller, record.Id);

        response.ReconciliationRequired.Should().Be(reconciliationRequired);
        response.RetryEligible.Should().Be(retryEligible);
        response.JiraExists.Should().Be(jiraExists);
    }

    private static OperationalRecordsController CreateController(
        OperationalRecordResult<JiraIssueDraft> previewResult,
        bool readOnlyIntegrationMode = false)
    {
        DefaultHttpContext httpContext = new() { TraceIdentifier = "trace-operational-test" };
        return new OperationalRecordsController(
            new EmptyRecordService(),
            new StubTransferService(previewResult),
            Options.Create(new CommandIdempotencyOptions()),
            Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnlyIntegrationMode }),
            Options.Create(new JiraIntegrationOptions()),
            new FixedTimeProvider(Now))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static OperationalRecordsController CreateController(
        OperationalRecord record,
        bool readOnlyIntegrationMode = false)
    {
        DefaultHttpContext httpContext = new() { TraceIdentifier = "trace-operational-test" };
        return new OperationalRecordsController(
            new StubRecordService(record),
            new StubTransferService(OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.WorkflowConflict, "test", false)),
            Options.Create(new CommandIdempotencyOptions()),
            Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnlyIntegrationMode, SourceCloseEnabled = true }),
            Options.Create(new JiraIntegrationOptions()),
            new FixedTimeProvider(Now))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async Task<OperationalRecordResponse> GetResponseAsync(OperationalRecordsController controller, Guid id)
    {
        ActionResult<OperationalRecordResponse> result = await controller.GetAsync(id, CancellationToken.None);
        return result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<OperationalRecordResponse>().Subject;
    }

    private static OperationalRecord Record() => new()
    {
        Id = Guid.NewGuid(),
        SourceRecordId = "synthetic-source",
        OrCode = "SYN-OR-TEST",
        Title = "Synthetic record",
        Description = "Synthetic description.",
        CreatedAt = Now.AddDays(-1),
        Classification = OperationalRecordClassification.OperationalSupport,
        JiraEligible = true,
        EligibilityReason = "Synthetic test rule.",
        WorkflowState = OperationalRecordWorkflowState.Eligible,
        UpdatedAt = Now,
        SourceConcurrencyToken = "synthetic-token",
        Version = 1
    };

    private sealed class EmptyRecordService : IOperationalRecordService
    {
        public Task<OperationalRecordResult<IReadOnlyList<OperationalRecord>>> ListAsync(OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Success(Array.Empty<OperationalRecord>()));

        public Task<OperationalRecordResult<OperationalRecord>> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false));
    }

    private sealed class StubRecordService(OperationalRecord record) : IOperationalRecordService
    {
        public Task<OperationalRecordResult<IReadOnlyList<OperationalRecord>>> ListAsync(OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Success([record]));

        public Task<OperationalRecordResult<OperationalRecord>> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == record.Id
                ? OperationalRecordResult<OperationalRecord>.Success(record)
                : OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false));
    }

    private sealed class StubTransferService(OperationalRecordResult<JiraIssueDraft> previewResult) : IJiraTransferService
    {
        public Task<OperationalRecordResult<JiraIssueDraft>> ReviewAsync(Guid id, JiraReviewRequest request, OperationalRecordCommandContext context, CancellationToken cancellationToken) => Task.FromResult(previewResult);
        public Task<OperationalRecordResult<JiraIssueDraft>> PreviewAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) => Task.FromResult(previewResult);
        public Task<OperationalRecordResult<OperationalRecord>> CreateAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationalRecordResult<OperationalRecord>> RetryAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturingTransferService(OperationalRecordResult<JiraIssueDraft> previewResult) : IJiraTransferService
    {
        public Task<OperationalRecordResult<JiraIssueDraft>> ReviewAsync(Guid id, JiraReviewRequest request, OperationalRecordCommandContext context, CancellationToken cancellationToken) => Task.FromResult(previewResult);
        public OperationalRecordCommandContext? Context { get; private set; }

        public Task<OperationalRecordResult<JiraIssueDraft>> PreviewAsync(
            Guid id,
            OperationalRecordCommandContext context,
            CancellationToken cancellationToken)
        {
            Context = context;
            return Task.FromResult(previewResult);
        }

        public Task<OperationalRecordResult<OperationalRecord>> CreateAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationalRecordResult<OperationalRecord>> RetryAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }


    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
