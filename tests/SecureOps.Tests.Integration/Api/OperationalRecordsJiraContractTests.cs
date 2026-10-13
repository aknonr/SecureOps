using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureOps.Api.Controllers;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Tests.Integration.Api;

/// <summary>Additive OR → Jira read fields: transfer key and save time, issue link, replay, retry preview, unknown dates.</summary>
public sealed class OperationalRecordsJiraContractTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly JiraIntegrationOptions _corporate = new() { Provider = "Corporate", IssueBrowseBaseUrl = "https://jira.contoso.example/browse/" };

    [Fact]
    public async Task GetAsync_ProjectsTransferKey_KeySaveTime_AndServerDerivedLink()
    {
        OperationalRecord record = Saved();

        OperationalRecordResponse response = Ok(await Controller(record, _corporate).GetAsync(record.Id, CancellationToken.None));

        response.TransferIdempotencyKey.Should().Be("SYNTHETICTRANSFERKEY");
        response.JiraIssueKeyPersistedAt.Should().Be(_now.AddMinutes(-5));
        response.JiraIssueUrl.Should().Be("https://jira.contoso.example/browse/SYN-42");
    }

    [Theory]
    [InlineData("Corporate", "")]
    [InlineData("Simulation", "https://jira.contoso.example/browse/")]
    public async Task GetAsync_WithoutCorporateHttpsConfiguration_HasNoLink(string provider, string baseUrl)
    {
        OperationalRecord record = Saved();

        OperationalRecordResponse response = Ok(await Controller(record, new() { Provider = provider, IssueBrowseBaseUrl = baseUrl })
            .GetAsync(record.Id, CancellationToken.None));

        response.JiraIssueUrl.Should().BeNull();
        response.JiraIssueKey.Should().Be("SYN-42");
    }

    [Fact]
    public async Task BrowseAsync_ReportsUnknownSourceDateCount()
    {
        OperationalRecord record = Saved();
        OperationalRecordsController controller = Controller(record, _corporate, unknownDates: 3);

        ActionResult<OperationalRecordPageResponse> result = await controller.BrowseAsync(new(), CancellationToken.None);

        Ok(result).UnknownSourceCreatedAtCount.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsync_ReportsReplayAndLink_FromTheDurableResult(bool replayed)
    {
        OperationalRecord record = Saved();
        StubTransfer transfer = new() { Command = OperationalRecordResult<OperationalRecord>.Success(record) with { IdempotentReplay = replayed } };

        JiraTransferResponse response = Ok(await Controller(record, _corporate, transfer: transfer).CreateAsync(record.Id, null, CancellationToken.None));

        response.Replayed.Should().Be(replayed);
        response.JiraIssueKey.Should().Be("SYN-42");
        response.JiraIssueUrl.Should().Be("https://jira.contoso.example/browse/SYN-42");
    }

    [Fact]
    public async Task RetryPreview_Matching_ReturnsTheDraft_Mismatching_WithholdsIt()
    {
        OperationalRecord record = Saved() with { JiraIssueKey = null, WorkflowState = OperationalRecordWorkflowState.JiraCreateFailed };
        JiraIssueDraft draft = new(record.Id, record.OrCode, "SYN", "Task", "SYN-OR-TEST: Synthetic", "Synthetic body", null,
            "mapping-v1", "SYNTHETICTRANSFERKEY", [], new("", "", "", "", []));
        StubTransfer matching = new() { Preview = OperationalRecordResult<JiraRetryDraft>.Success(new(draft, "mapping-v1", "SYNTHETICTRANSFERKEY", true)) };
        StubTransfer mismatching = new() { Preview = OperationalRecordResult<JiraRetryDraft>.Success(new(draft, "mapping-v0", "OLDKEY", false)) };

        JiraRetryPreviewResponse match = Ok(await Controller(record, _corporate, transfer: matching).RetryPreviewAsync(record.Id, CancellationToken.None));
        JiraRetryPreviewResponse mismatch = Ok(await Controller(record, _corporate, transfer: mismatching).RetryPreviewAsync(record.Id, CancellationToken.None));

        match.MatchesPersistedDraft.Should().BeTrue();
        match.Draft!.Summary.Should().Be("SYN-OR-TEST: Synthetic");
        match.PersistedIdempotencyKey.Should().Be("SYNTHETICTRANSFERKEY");
        mismatch.MatchesPersistedDraft.Should().BeFalse();
        mismatch.Draft.Should().BeNull();
        mismatch.PersistedMappingVersion.Should().Be("mapping-v0");
    }

    [Fact]
    public async Task RetryPreview_InvalidState_IsConflict()
    {
        OperationalRecord record = Saved();
        StubTransfer transfer = new() { Preview = OperationalRecordResult<JiraRetryDraft>.Fail(OperationalErrorCodes.OperationalRecordInvalidState, "retry-preview", false) };

        ActionResult<JiraRetryPreviewResponse> result = await Controller(record, _corporate, transfer: transfer).RetryPreviewAsync(record.Id, CancellationToken.None);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void RetryPreview_RequiresTheRetryCapability_AndIsAReadOnlyGet()
    {
        MethodInfo method = typeof(OperationalRecordsController).GetMethod(nameof(OperationalRecordsController.RetryPreviewAsync))!;

        method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(Policies.CanRetryJira);
        method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{id:guid}/retry-preview");
    }

    [Fact]
    public void NewFields_AreAdditiveCamelCaseJson_WithSafeDefaults()
    {
        JsonSerializerOptions web = new(JsonSerializerDefaults.Web);
        JiraTransferResponse transfer = new(Guid.Empty, "SYN-OR-TEST", OperationalRecordWorkflowState.JiraCreated, "SYN-42", "mapping-v1", "KEY", 0, "corr");
        using var transferJson = JsonDocument.Parse(JsonSerializer.Serialize(transfer, web));
        using var pageJson = JsonDocument.Parse(JsonSerializer.Serialize(new OperationalRecordPageResponse([], 0, 1, 25), web));

        transferJson.RootElement.GetProperty("replayed").GetBoolean().Should().BeFalse();
        transferJson.RootElement.GetProperty("jiraIssueUrl").ValueKind.Should().Be(JsonValueKind.Null);
        pageJson.RootElement.GetProperty("unknownSourceCreatedAtCount").GetInt32().Should().Be(0);
        JsonSerializer.Deserialize<JiraTransferResponse>("""{"operationalRecordId":"00000000-0000-0000-0000-000000000000","orCode":"X","workflowState":7,"mappingVersion":"m","idempotencyKey":"k","retryCount":0,"correlationId":"c"}""", web)!
            .Replayed.Should().BeFalse();
    }

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static OperationalRecordsController Controller(OperationalRecord record, JiraIntegrationOptions jira, int unknownDates = 0, StubTransfer? transfer = null) =>
        new(new StubRecords(record, unknownDates), transfer ?? new StubTransfer(),
            Options.Create(new CommandIdempotencyOptions()),
            Options.Create(new OperationalRecordsOptions { SourceProvider = "Fake" }),
            Options.Create(jira),
            new FixedTime())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-jira-contract" } }
        };

    private static OperationalRecord Saved() => new()
    {
        Id = Guid.NewGuid(),
        SourceRecordId = "synthetic-source",
        OrCode = "SYN-OR-TEST",
        Title = "Synthetic",
        Description = "Synthetic only.",
        Classification = OperationalRecordClassification.ServerRequest,
        JiraEligible = true,
        EligibilityReason = "Synthetic test rule.",
        WorkflowState = OperationalRecordWorkflowState.JiraCreated,
        JiraIssueKey = "SYN-42",
        JiraIssueKeyPersistedAt = _now.AddMinutes(-5),
        MappingVersion = "mapping-v1",
        IdempotencyKey = "SYNTHETICTRANSFERKEY",
        UpdatedAt = _now,
        SourceConcurrencyToken = "synthetic-token",
        Version = 3
    };

    private sealed class StubRecords(OperationalRecord record, int unknownDates) : IOperationalRecordService
    {
        public Task<OperationalRecordPage> BrowseAsync(OperationalRecordQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalRecordPage([record], 1, unknownDates));
        public Task<OperationalRecordResult<IReadOnlyList<OperationalRecord>>> ListAsync(OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Success([record]));
        public Task<OperationalRecordResult<OperationalRecord>> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(OperationalRecordResult<OperationalRecord>.Success(record));
    }

    private sealed class StubTransfer : IJiraTransferService
    {
        public OperationalRecordResult<OperationalRecord> Command { get; init; } =
            OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.WorkflowConflict, "test", false);
        public OperationalRecordResult<JiraRetryDraft> Preview { get; init; } =
            OperationalRecordResult<JiraRetryDraft>.Fail(OperationalErrorCodes.WorkflowConflict, "test", false);

        public Task<OperationalRecordResult<JiraIssueDraft>> ReviewAsync(Guid id, JiraReviewRequest request, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationalRecordResult<JiraIssueDraft>> PreviewAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationalRecordResult<OperationalRecord>> CreateAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Command);
        public Task<OperationalRecordResult<JiraRetryDraft>> RetryPreviewAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Preview);
        public Task<OperationalRecordResult<OperationalRecord>> RetryAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Command);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
