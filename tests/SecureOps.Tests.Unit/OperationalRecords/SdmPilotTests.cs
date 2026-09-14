using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class SdmPilotTests
{
    [Fact]
    public async Task SimulationHappyFixture_IsServerRequestWithoutBroadeningOtherRecords()
    {
        var source = new SimulationOperationalRecordClient();
        var classifier = new SimulationOperationalRecordClassifier();
        OperationalRecordSourceItem happy = (await source.GetActiveAsync(10, default)).Single(item => item.OrCode == "SIM-OR-100");
        classifier.Classify(happy).Classification.Should().Be(OperationalRecordClassification.ServerRequest);
        classifier.Classify(happy with { SourceRecordId = "unapproved-synthetic" }).JiraEligible.Should().BeFalse();
    }

    [Fact]
    public async Task PositivePreview_PersistsHumanDecision_CreateAndReplayKeepSourceOpen()
    {
        Fixture f = await Fixture.Create();
        OperationalRecordResult<JiraIssueDraft> preview = await f.Service.PreviewAsync(f.Record.Id, f.Context, default);
        preview.IsSuccess.Should().BeTrue();
        preview.Value!.SourceCloseRequested.Should().BeFalse();
        OperationalRecord stored = (await f.Repository.GetAsync(f.Record.Id, default))!;
        stored.JiraEligible.Should().BeTrue();
        stored.SdmEvaluation!.Result.ExternalWriteEligible.Should().BeFalse();
        f.Audit.Events.Should().Contain(e => e.Action == SdmEvaluationEvidence.AuditAction && e.Actor == f.Context.Actor);
        (await f.Service.CreateAsync(f.Record.Id, f.Context, default)).Failure!.Code.Should().Be(OperationalErrorCodes.ExternalWritesDisabled);
        // Only synthetic substitutes execute; no corporate transport is constructed.
        f.Options.ReadOnlyIntegrationMode = false;
        f.Options.ControlledTestWritesEnabled = true;
        OperationalRecordResult<OperationalRecord> result = await f.Service.CreateAsync(f.Record.Id, f.Context, default);
        result.IsSuccess.Should().BeTrue();
        result.Value!.JiraIssueKey.Should().Be("TEST-901");
        result.Value.WorkflowState.Should().Be(OperationalRecordWorkflowState.JiraCreated);
        (await f.Service.CreateAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeTrue();
        await f.Jira.Received(1).CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>());
        await f.Source.DidNotReceiveWithAnyArgs().CloseAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData("expiry", "PilotPolicyExpired")]
    [InlineData("fingerprint", "PilotRecordMismatch")]
    [InlineData("scope", "PilotScopeUnproven")]
    [InlineData("mapping", "JiraMappingPending")]
    [InlineData("retirement", "TypeMappingPending")]
    [InlineData("approval", "CategoryPolicyPending")]
    [InlineData("close", "PilotMustRemainSourceOpen")]
    public async Task PolicyChange_BlocksPreviewBeforeJira(string change, string reason)
    {
        Fixture f = await Fixture.Create();
        switch (change)
        {
            case "expiry":
                f.Options.Pilot.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                break;
            case "fingerprint":
                f.Options.Pilot.SourceFingerprint = new string('0', 64);
                break;
            case "scope":
                f.SourceOptions.ExcludedDccIds = [99];
                break;
            case "mapping":
                f.JiraOptions.MappingVersion = "changed";
                break;
            case "retirement":
                f.Options.Pilot.RequestType = OperationalRecordClassification.ServerRetirement;
                break;
            case "approval":
                f.Options.Pilot.ApprovalReference = "";
                break;
            case "close":
                f.Options.SourceCloseEnabled = true;
                break;
        }
        SdmPilotPolicy.Blockers(f.Record, f.Options, f.JiraOptions, f.SourceOptions, DateTimeOffset.UtcNow).Should().Contain(reason);
        (await f.Service.PreviewAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeFalse();
        await f.Jira.DidNotReceiveWithAnyArgs().CreateIssueAsync(default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedPolicyOrChangedSource_AfterPreviewCannotCreate(bool sourceChange)
    {
        Fixture f = await Fixture.Create();
        (await f.Service.PreviewAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeTrue();
        f.Options.ReadOnlyIntegrationMode = false;
        if (sourceChange)
        {
            f.Source.GetByIdAsync("901", Arg.Any<CancellationToken>()).Returns(f.Item with { Description = "Changed" });
        }
        else
        {
            f.Options.Pilot.ApprovalReference = "";
        } (await f.Service.CreateAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeFalse();
        await f.Jira.DidNotReceiveWithAnyArgs().CreateIssueAsync(default!, default);
    }

    [Fact]
    public async Task AmbiguousIdentityAndUnknownCreate_AreNotRetried()
    {
        Fixture f = await Fixture.Create();
        f.Resolver.ResolveExactAsync("sample.requester", Arg.Any<CancellationToken>()).Returns(RequesterResolutionResult.Ambiguous());
        (await f.Service.PreviewAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeFalse();
        f.Resolver.ResolveExactAsync("sample.requester", Arg.Any<CancellationToken>()).Returns(RequesterResolutionResult.Found("requester-account"));
        (await f.Service.PreviewAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeTrue();
        f.Options.ReadOnlyIntegrationMode = false;
        f.Jira.CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>()).Returns<Task<JiraIssueCreationResult>>(_ => throw new ExternalIntegrationException(OperationalErrorCodes.JiraUnavailable, true, outcomeUnknown: true));
        (await f.Service.CreateAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeFalse();
        (await f.Repository.GetAsync(f.Record.Id, default))!.ReconciliationRequired.Should().BeTrue();
        (await f.Service.RetryAsync(f.Record.Id, f.Context, default)).IsSuccess.Should().BeFalse();
        await f.Jira.Received(1).CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetirementReview_HasNoMappingApprovalOrCreateToken()
    {
        Fixture f = await Fixture.Create();
        JiraIssueDraft review = f.Drafts.BuildReview(f.Record, OperationalRecordClassification.ServerRetirement);
        review.ReviewOnly.Should().BeTrue();
        review.BlockingConditions.Should().Contain("RetirementMappingPending");
        review.FieldMapping!.Labels.Should().BeEmpty();
        SdmEvaluationInput input = SdmPilotPolicy.ConfirmedInput(f.Record, review, f.Options);
        SdmPilotEvaluator.Evaluate(input).JiraEligible.Should().BeFalse();
        SdmEvaluator.Evaluate(input).JiraEligible.Should().BeFalse();
    }

    private sealed class Fixture
    {
        public InMemoryOperationalRecordRepository Repository { get; } = new();
        public InMemoryAuditWriter Audit { get; } = new();
        public IOperationalRecordClient Source { get; } = Substitute.For<IOperationalRecordClient>();
        public IJiraClient Jira { get; } = Substitute.For<IJiraClient>();
        public IJiraUserResolver Resolver { get; } = Substitute.For<IJiraUserResolver>();
        public OperationalRecordSourceItem Item { get; } = TestRecord.SourceItem("901", "OR-901");
        public OperationalRecordCommandContext Context { get; } = new("sample.operator", "pilot-test", null);
        public OperationalRecordsOptions Options { get; } = new() { SourceProvider = "TuruncuHat", SourceCloseEnabled = false, ReadOnlyIntegrationMode = true };
        public TuruncuHatOptions SourceOptions { get; } = new() { SourceBaseObject = "SMSS_oRFF", RelatedGroupId = 68, ExcludedDccIds = [4241] };
        public JiraIntegrationOptions JiraOptions { get; } = new() { ProjectKey = "TEST", IssueTypeId = "1", MappingVersion = "fixture-v1", TeamCustomField = "customfield_101", TeamValue = "team", RequesterWatcherCustomField = "customfield_102", Labels = ["fixture"], ReporterMode = "AuthenticatedOperator", UnresolvedRequesterPolicy = "Block" };
        public OperationalRecord Record { get; private set; } = null!;
        public JiraIssueDraftService Drafts { get; private set; } = null!;
        public JiraTransferService Service { get; private set; } = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Record = await f.Repository.UpsertImportedAsync(f.Item, "seed", default);
            f.Record = await f.Repository.EvaluateAsync(f.Record.Id, SdmEvaluationEvidence.FromSource(f.Item, true, true), f.Context, f.Audit, default);
            f.Options.Pilot = new() { RuleSetVersion = SdmPilotEvaluator.RuleSetVersion, ApprovalReference = "synthetic-decision", TrackingReason = "Track agreed provisioning outcome", SourceRecordId = "901", SourceFingerprint = SdmPilotPolicy.Fingerprint(f.Record), SourceScope = "SMSS_oRFF:68:4241", MappingVersion = "fixture-v1", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), RequestType = OperationalRecordClassification.ServerRequest };
            f.Source.GetByIdAsync("901", Arg.Any<CancellationToken>()).Returns(f.Item);
            f.Resolver.ResolveExactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RequesterResolutionResult.Found("fixture-account"));
            f.Jira.CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>()).Returns(new JiraIssueCreationResult("TEST-901"));
            f.Drafts = new(f.Resolver, new IdentityAccountNormalizer(Microsoft.Extensions.Options.Options.Create(new IdentityLookupOptions())), Microsoft.Extensions.Options.Options.Create(f.JiraOptions), Microsoft.Extensions.Options.Options.Create(f.Options), Microsoft.Extensions.Options.Options.Create(f.SourceOptions));
            f.Service = new(f.Repository, f.Drafts, f.Jira, f.Source, new InMemoryCommandIdempotencyStore(TimeProvider.System), f.Audit, Microsoft.Extensions.Options.Options.Create(f.Options), Microsoft.Extensions.Options.Options.Create(new CommandIdempotencyOptions()), NullLogger<JiraTransferService>.Instance);
            return f;
        }
    }
}
