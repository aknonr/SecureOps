using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class SdmEvaluationTests
{
    private static readonly OperationalRecordCommandContext _context = new("test:operator", "sdm-test", null);
    private static SdmEvaluationInput Input => new(new string('a', 64), ProviderSupported: true,
        Active: true, ValidId: true, ValidCode: true, ValidTitle: true, ValidDescription: true);

    [Fact]
    public void Project_RefreshOutrunsEvaluation_ShowsPriorEvidenceAsStaleWithoutReevaluation()
    {
        OperationalRecord evaluated = SdmEvaluationEvidence.Apply(
            Record() with { SourceConcurrencyToken = OperationalRecordSourceConcurrency.Create(Source()) },
            SdmEvaluationEvidence.FromSource(Source(), true, true), DateTimeOffset.UnixEpoch);
        evaluated.SdmEvaluation!.Result.EvaluationStale.Should().BeFalse();
        OperationalRecord projected = SdmEvaluationEvidence.Project(evaluated with { SourceConcurrencyToken = "changed-token" });
        projected.SdmEvaluation!.Result.EvaluationStale.Should().BeTrue();
        projected.SdmEvaluation.Result.ReasonCodes.Should().Contain(["EvaluationStale", "SourceChanged"]).And.NotContain("EvaluationCurrent");
        projected.SdmEvaluation.Result.InputHash.Should().Be(evaluated.SdmEvaluation!.Result.InputHash);
        projected.SdmEvaluation.EvaluatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        projected.JiraEligible.Should().BeFalse();
    }

    [Fact]
    public void SourceFingerprint_CanonicalOffsetsAndDelimiterBoundaries_AreUnambiguous()
    {
        OperationalRecordSourceItem source = Source() with { CreatedAt = DateTimeOffset.UnixEpoch };
        SdmEvaluationEvidence.FromSource(source, true, true).Should().Be(
            SdmEvaluationEvidence.FromSource(source with { CreatedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(3)) }, true, true));
        string first = SdmEvaluationEvidence.FromSource(source with { Title = "a\nb", Description = "c" }, true, true).SourceFingerprint;
        string second = SdmEvaluationEvidence.FromSource(source with { Title = "a", Description = "b\nc" }, true, true).SourceFingerprint;
        first.Should().NotBe(second);
        SdmEvaluationEvidence.FromSource(source with { Requester = null }, true, true).SourceFingerprint.Should().NotBe(
            SdmEvaluationEvidence.FromSource(source, true, true).SourceFingerprint);
    }

    [Theory]
    [InlineData("synthetic-record")]
    [InlineData("simulation-happy")]
    [InlineData("SIM-OR-1")]
    [InlineData("SYN-OR-1")]
    [InlineData("FAKE-1")]
    public void Source_SyntheticNamespaces_AreExcluded(string identifier)
    {
        SdmEvaluationInput input = SdmEvaluationEvidence.FromSource(Source() with { SourceRecordId = identifier }, true, true);
        SdmEvaluator.Evaluate(input).RecommendedClassification.Should().Be(OperationalRecordClassification.NotJiraEligible);
    }

    [Fact]
    public void Evaluate_UnknownEvidence_RemainsManualWithCanonicalReasons()
    {
        SdmEvaluationResult result = SdmEvaluator.Evaluate(Input);
        result.InputHash.Should().Be("fe2930f6f5dcda3e92cf2beec9a605760933e5d1ccb61ad31446d7937591a782");
        result.RecommendedClassification.Should().Be(OperationalRecordClassification.NeedsManualReview);
        result.JiraEligible.Should().BeFalse();
        result.SdmCandidateRecommended.Should().BeFalse();
        result.ExternalWriteEligible.Should().BeFalse();
        result.ReasonCodes.Should().Equal("ApprovalRequired", "CategoryPolicyPending", "CategoryUnknown", "DccUnproven",
            "EvaluationCurrent", "ExternalWritesDisabled", "GroupUnproven", "InfrastructureReferenceMissing",
            "ReporterUnresolved", "RequesterMissing", "RequesterUnresolved");
        result.BlockingConditions.Should().NotContain(["EvaluationCurrent", "InfrastructureReferenceMissing"]);
        result.Should().BeEquivalentTo(SdmEvaluator.Evaluate(Input));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void Evaluate_CultureChange_PreservesHashAndOrderedDecision(string culture)
    {
        string expected = JsonSerializer.Serialize(SdmEvaluator.Evaluate(Input));
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            JsonSerializer.Serialize(SdmEvaluator.Evaluate(Input)).Should().Be(expected);
            SdmEvaluator.Hash(Input with { SourceFingerprint = new string('b', 64) }).Should().NotBe(SdmEvaluator.Hash(Input));
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    public static IEnumerable<object[]> BlockedInputs()
    {
        yield return [Input with { Synthetic = true }, "SyntheticIdentifier"];
        yield return [Input with { Contradictory = true }, "ContradictoryEvidence"];
        yield return [Input with { AlreadyTransferred = true }, "AlreadyTransferred"];
        yield return [Input with { ReconciliationRequired = true }, "ReconciliationRequired"];
        yield return [Input with { SourceChanged = true }, "SourceChanged"];
        yield return [Input with { EvaluationStale = true }, "EvaluationStale"];
        yield return [Input with { ProviderSupported = false }, "UnsupportedProvider"];
        yield return [Input with { Active = false }, "InactiveSource"];
        yield return [Input with { GroupInScope = false }, "GroupOutOfScope"];
        yield return [Input with { DccAllowed = false }, "ExcludedDcc"];
        yield return [Input with { ValidId = false }, "InvalidSourceId"];
        yield return [Input with { ValidCode = false }, "InvalidOrCode"];
        yield return [Input with { ValidTitle = false }, "InvalidTitle"];
        yield return [Input with { ValidDescription = false }, "InvalidDescription"];
        yield return [Input with { Category = OperationalRecordClassification.NotJiraEligible }, "CategoryUnsupported"];
        yield return [Input with { RequesterAmbiguous = true }, "RequesterAmbiguous"];
        yield return [Input with { RequesterResolved = true }, "ContradictoryEvidence"];
        yield return [Input with { SourceFingerprint = null! }, "ContradictoryEvidence"];
        yield return [Input with { Category = (OperationalRecordClassification)99 }, "ContradictoryEvidence"];
    }

    [Theory]
    [MemberData(nameof(BlockedInputs))]
    public void Evaluate_UnsafeEvidence_HasStableBlocker(SdmEvaluationInput input, string code)
    {
        SdmEvaluationResult result = SdmEvaluator.Evaluate(input);
        result.BlockingConditions.Should().Contain(code).And.BeInAscendingOrder(StringComparer.Ordinal);
        result.ReasonCodes.Should().OnlyHaveUniqueItems().And.BeInAscendingOrder(StringComparer.Ordinal);
        result.ExternalWriteEligible.Should().BeFalse();
        result.JiraEligible.Should().BeFalse();
        result.SdmCandidateRecommended.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_CompleteFacts_DoNotInventPositiveCategoryPolicy()
    {
        SdmEvaluationResult result = SdmEvaluator.Evaluate(Input with
        {
            GroupInScope = true,
            DccAllowed = true,
            Category = OperationalRecordClassification.ServerRequest,
            ServerPresent = true,
            IpPresent = true,
            RequesterPresent = true,
            RequesterResolved = true,
            ReporterResolved = true,
            ApprovalGranted = true,
            WritesDisabled = false
        });
        result.ReasonCodes.Should().Contain(["CategorySupported", "InfrastructureReferencePresent", "ApprovalGranted"]);
        result.BlockingConditions.Should().Equal("CategoryPolicyPending");
        result.RecommendedClassification.Should().Be(OperationalRecordClassification.NeedsManualReview);
        result.ExternalWriteEligible.Should().BeFalse();
    }

    [Theory]
    [InlineData(OperationalRecordWorkflowState.Completed, true, false, "AlreadyTransferred")]
    [InlineData(OperationalRecordWorkflowState.JiraCreateFailed, false, true, "ReconciliationRequired")]
    [InlineData(OperationalRecordWorkflowState.CreatingJira, false, false, "ReconciliationRequired")]
    public void Apply_ProgressedWorkflow_PreservesState(OperationalRecordWorkflowState state, bool jira, bool reconcile, string code)
    {
        OperationalRecord record = Record() with { WorkflowState = state, JiraIssueKey = jira ? "FAKE-1" : null, ReconciliationRequired = reconcile };
        OperationalRecord result = SdmEvaluationEvidence.Apply(record, Input, DateTimeOffset.UnixEpoch);
        result.WorkflowState.Should().Be(state);
        result.JiraIssueKey.Should().Be(record.JiraIssueKey);
        result.ReconciliationRequired.Should().Be(reconcile);
        result.SdmEvaluation!.Result.BlockingConditions.Should().Contain(code);
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedAndConcurrentInput_EmitsOneAuditAndRoundTripsEvidence()
    {
        InMemoryOperationalRecordRepository repository = new();
        InMemoryAuditWriter audit = new();
        OperationalRecord imported = await repository.UpsertImportedAsync(Source(), _context.CorrelationId, default);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repository.EvaluateAsync(imported.Id, Input, _context, audit, default)));
        OperationalRecord evaluated = (await repository.GetAsync(imported.Id, default))!;
        audit.Events.Should().ContainSingle();
        string json = SdmEvaluationEvidence.Serialize(evaluated.SdmEvaluation!);
        JsonSerializer.Deserialize<SdmEvaluationSnapshot>(json).Should().BeEquivalentTo(evaluated.SdmEvaluation);
        json.Should().NotContain(Source().Title).And.NotContain(Source().Description).And.NotContain(Source().Requester!);
        OperationalRecord repeated = await repository.EvaluateAsync(imported.Id, Input, _context, audit, default);
        repeated.Version.Should().Be(evaluated.Version);
        repeated.SdmEvaluation!.EvaluatedAt.Should().Be(evaluated.SdmEvaluation!.EvaluatedAt);
        OperationalRecord changed = await repository.EvaluateAsync(imported.Id, Input with { SourceFingerprint = new string('b', 64) }, _context, audit, default);
        changed.SdmEvaluation!.Result.SourceChanged.Should().BeTrue();
        changed.SdmEvaluation.Result.EvaluationStale.Should().BeTrue();
        (await repository.EvaluateAsync(imported.Id, Input with { SourceFingerprint = new string('b', 64) }, _context, audit, default))
            .Version.Should().Be(changed.Version);
        audit.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task EvaluateAsync_AuditFailure_DoesNotPersistEvaluation()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await repository.UpsertImportedAsync(Source(), _context.CorrelationId, default);
        IAuditWriter audit = Substitute.For<IAuditWriter>();
        audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException()));
        await FluentActions.Invoking(() => repository.EvaluateAsync(record.Id, Input, _context, audit, default)).Should().ThrowAsync<InvalidOperationException>();
        (await repository.GetAsync(record.Id, default))!.SdmEvaluation.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_FourCorporateShapedRows_OnlySourceListCalledAndAllRemainManual()
    {
        IOperationalRecordClient client = Substitute.For<IOperationalRecordClient>();
        client.GetActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Enumerable.Range(1, 4).Select(n => Source() with { SourceRecordId = n.ToString(CultureInfo.InvariantCulture), OrCode = $"OR-{n}" }).ToArray());
        IOperationalRecordClassifier classifier = Substitute.For<IOperationalRecordClassifier>();
        InMemoryAuditWriter audit = new();
        OperationalRecordService service = new(client, classifier, new InMemoryOperationalRecordRepository(), audit,
            Options.Create(new OperationalRecordsOptions { SourceProvider = "TuruncuHat", ReadOnlyIntegrationMode = true }),
            NullLogger<OperationalRecordService>.Instance);
        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await service.ListAsync(_context, default);
        result.Value.Should().HaveCount(4).And.OnlyContain(r => !r.JiraEligible
            && r.Classification == OperationalRecordClassification.NeedsManualReview && !r.SdmEvaluation!.Result.SdmCandidateRecommended);
        await service.ListAsync(_context, default);
        audit.Events.Should().HaveCount(4);
        client.ReceivedCalls().Should().OnlyContain(c => c.GetMethodInfo().Name == nameof(IOperationalRecordClient.GetActiveAsync));
        classifier.ReceivedCalls().Should().BeEmpty();
    }

    private static OperationalRecordSourceItem Source() => new("1", "OR-1", "Fixture title", "Fixture description", "fixture.requester", null, null, null, null);
    private static OperationalRecord Record() => new()
    {
        Id = Guid.Empty,
        SourceRecordId = "1",
        OrCode = "OR-1",
        Title = "Fixture title",
        Description = "Fixture description",
        Classification = OperationalRecordClassification.NeedsManualReview,
        JiraEligible = false,
        EligibilityReason = "Pending",
        WorkflowState = OperationalRecordWorkflowState.Imported,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        SourceConcurrencyToken = "fixture"
    };
}
