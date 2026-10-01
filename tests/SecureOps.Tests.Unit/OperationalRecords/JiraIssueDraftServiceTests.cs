using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class JiraIssueDraftServiceTests
{
    [Theory]
    [InlineData(OperationalRecordClassification.ServerRequest)]
    [InlineData(OperationalRecordClassification.SoftwareInstallation)]
    [InlineData(OperationalRecordClassification.ServerRetirement)]
    public async Task BuildReview_ConfirmedTypes_RemainDeclarationsWithoutEligibilityOrIdentityLookup(OperationalRecordClassification type)
    {
        OperationalRecord record = await TestRecord.SeedEligibleAsync(new InMemoryOperationalRecordRepository());
        record = record with
        {
            JiraEligible = false,
            Classification = OperationalRecordClassification.NeedsManualReview,
            WorkflowState = OperationalRecordWorkflowState.NeedsManualReview
        };
        var resolver = new StubResolver(RequesterResolutionResult.Found("jira-requester"));
        JiraIssueDraftService service = CreateService(resolver, "Block");
        JiraIssueDraft review = service.BuildReview(record, type);
        review.ReviewOnly.Should().BeTrue();
        review.RequestType.Should().Be(type);
        review.RecordVersion.Should().Be(record.Version);
        review.BlockingConditions.Should().Contain(["CategoryPolicyPending", "OperatorDeclarationOnly", "RequesterUnresolved"]);
        if (type != OperationalRecordClassification.ServerRequest)
        {
            review.FieldMapping.Labels.Should().BeEmpty();
            review.BlockingConditions.Should().Contain(type == OperationalRecordClassification.SoftwareInstallation
                ? "ApplicationMappingPending" : "RetirementMappingPending");
        }
        else
        {
            review.FieldMapping.Labels.Should().NotBeEmpty();
        }
        resolver.Identities.Should().BeEmpty();
        (await service.BuildAsync(record, "synthetic", CancellationToken.None)).IsSuccess.Should().BeFalse();
        service.BuildReview(record with { SourceConcurrencyToken = "source:changed" }, type).IdempotencyKey.Should().NotBe(review.IdempotencyKey);
        service.BuildReview(record, type == OperationalRecordClassification.ServerRequest
            ? OperationalRecordClassification.SoftwareInstallation : OperationalRecordClassification.ServerRequest).IdempotencyKey.Should().NotBe(review.IdempotencyKey);
    }

    [Fact]
    public async Task BuildAsync_InstallationEvenWhenEligible_DoesNotFallBackToServerLabels()
    {
        OperationalRecord record = await TestRecord.SeedEligibleAsync(new InMemoryOperationalRecordRepository());
        JiraIssueDraftService service = CreateService(new StubResolver(RequesterResolutionResult.Found("synthetic")), "Block");
        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(record with
        { Classification = OperationalRecordClassification.SoftwareInstallation }, "synthetic", CancellationToken.None);
        result.Failure!.Stage.Should().Be("application-mapping");
        Action unsupported = () => service.BuildReview(record, OperationalRecordClassification.EnvironmentRequest);
        unsupported.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BuildAsync_MissingRequester_BlockPolicyFailsClosed(string? requester)
    {
        OperationalRecord record = await TestRecord.SeedEligibleAsync(new InMemoryOperationalRecordRepository());
        var resolver = new StubResolver(RequesterResolutionResult.Found("jira-requester"));
        OperationalRecordResult<JiraIssueDraft> result = await CreateService(resolver, "Block").BuildAsync(record with { Requester = requester }, "test:operator", CancellationToken.None);
        result.Failure!.Code.Should().Be(OperationalErrorCodes.RequesterResolutionFailed);
        resolver.Identities.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_EmptyResolvedIdentity_BlockPolicyFailsClosed()
    {
        OperationalRecord record = await TestRecord.SeedEligibleAsync(new InMemoryOperationalRecordRepository());
        OperationalRecordResult<JiraIssueDraft> result = await CreateService(new StubResolver(RequesterResolutionResult.Found("")), "Block")
            .BuildAsync(record, "test:operator", CancellationToken.None);
        result.Failure!.Code.Should().Be(OperationalErrorCodes.RequesterResolutionFailed);
    }

    [Fact]
    public async Task BuildAsync_SourceRefreshAfterPreview_CannotAcquireCreateWithChangedContent()
    {
        var repository = new InMemoryOperationalRecordRepository();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService service = CreateService(new StubResolver(RequesterResolutionResult.Found("jira-requester")), "Block");
        JiraIssueDraft first = (await service.BuildAsync(record, "test:operator", CancellationToken.None)).Value!;
        await repository.MarkPreviewedAsync(record.Id, first.MappingVersion, first.IdempotencyKey, "test:operator", "synthetic", CancellationToken.None);
        foreach (OperationalRecord changed in new[] { record with { Title = "Changed title" }, record with { Description = "Changed description" }, record with { SourceConcurrencyToken = "source:changed" } })
        {
            JiraIssueDraft draft = (await service.BuildAsync(changed, "test:operator", CancellationToken.None)).Value!;
            draft.IdempotencyKey.Should().NotBe(first.IdempotencyKey);
            await repository.TryClaimAsync(record.Id, "test:operator", TimeSpan.FromMinutes(2), "synthetic", CancellationToken.None);
            WorkflowAcquireResult acquired = await repository.TryAcquireCreateAsync(record.Id, draft.MappingVersion, draft.IdempotencyKey, "test:operator", "synthetic", CancellationToken.None);
            acquired.Disposition.Should().Be(WorkflowAcquireDisposition.Conflict);
        }
    }

    [Fact]
    public async Task BuildAsync_WithExactRequesterMatch_ReturnsConfiguredPreview()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService service = CreateService(new StubResolver(RequesterResolutionResult.Found("jira-account-100")), "Block");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(record, "test:operator", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        JiraIssueDraft draft = result.Value!;
        draft.RequesterAccountId.Should().Be("jira-account-100");
        draft.ProjectKey.Should().Be("TEST");
        draft.FieldMapping.Should().BeEquivalentTo(new JiraIssueFieldMapping(
            "3",
            "customfield_team",
            "WASAS",
            "customfield_requester",
            ["SunucuTalep"]));
        draft.IdempotencyKey.Should().HaveLength(64);
    }

    [Fact]
    public async Task BuildAsync_WithAmbiguousRequester_FailsClosed()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService service = CreateService(new StubResolver(RequesterResolutionResult.Ambiguous()), "ProceedUnassigned");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(record, "test:operator", CancellationToken.None);

        result.Failure!.Code.Should().Be(OperationalErrorCodes.RequesterResolutionAmbiguous);
    }

    [Fact]
    public async Task BuildAsync_WhenPolicyAllowsUnassigned_AddsSafeWarning()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService service = CreateService(new StubResolver(RequesterResolutionResult.NotFound()), "ProceedUnassigned");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(record, "test:operator", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        JiraIssueDraft draft = result.Value!;
        draft.RequesterAccountId.Should().BeNull();
        draft.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task BuildAsync_WithProjectDefaultAssignment_DoesNotInferAssignee()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService service = CreateService(
            new StubResolver(RequesterResolutionResult.Found("jira-requester")),
            "Block");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(
            record,
            "EXAMPLE\\operator",
            CancellationToken.None);

        result.Value!.AssigneeUsername.Should().BeNull();
        result.Value.ReporterUsername.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithAuthenticatedOperator_ResolvesExactServerActorAsReporter()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        StubResolver resolver = new(
            RequesterResolutionResult.Found("jira-requester"),
            RequesterResolutionResult.Found("jira-operator"));
        JiraIssueDraftService service = CreateService(
            resolver,
            "Block",
            reporterMode: "AuthenticatedOperator");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(
            record,
            "SYNTHETIC\\Operator.One",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequesterAccountId.Should().Be("jira-requester");
        result.Value.ReporterUsername.Should().Be("jira-operator");
        result.Value.ReporterUsername.Should().NotBe(result.Value.RequesterAccountId);
        resolver.Identities.Should().Equal(record.Requester!, "operator.one");
    }

    [Fact]
    public async Task BuildAsync_WithNormalizedOidcActor_UsesLoginNameForReporterResolution()
    {
        OidcExternalIdentityNormalizer normalizer = new(Options.Create(new OidcOptions()));
        ClaimsPrincipal oidcPrincipal = normalizer.Normalize(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://identity.example.test"),
            new Claim("sub", "synthetic-subject-100"),
            new Claim("loginname", "operator.oidc")
        ], "synthetic-oidc"))).Principal!;
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        StubResolver resolver = new(
            RequesterResolutionResult.Found("jira-requester"),
            RequesterResolutionResult.Found("jira-operator"));
        JiraIssueDraftService service = CreateService(resolver, "Block", reporterMode: "AuthenticatedOperator");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(
            record,
            oidcPrincipal.Identity!.Name!,
            CancellationToken.None);

        result.Value!.ReporterUsername.Should().Be("jira-operator");
        resolver.Identities.Should().Equal(record.Requester!, "operator.oidc");
    }

    [Theory]
    [InlineData(RequesterResolutionStatus.NotFound, false)]
    [InlineData(RequesterResolutionStatus.Ambiguous, false)]
    [InlineData(RequesterResolutionStatus.Failed, true)]
    public async Task BuildAsync_WhenAuthenticatedOperatorIsNotUniquelyResolved_FailsClosed(
        RequesterResolutionStatus status,
        bool retryable)
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        RequesterResolutionResult operatorResult = status switch
        {
            RequesterResolutionStatus.NotFound => RequesterResolutionResult.NotFound(),
            RequesterResolutionStatus.Ambiguous => RequesterResolutionResult.Ambiguous(),
            _ => RequesterResolutionResult.Failed()
        };
        JiraIssueDraftService service = CreateService(
            new StubResolver(RequesterResolutionResult.Found("jira-requester"), operatorResult),
            "Block",
            reporterMode: "AuthenticatedOperator");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(
            record,
            "SYNTHETIC\\operator.one",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.Code.Should().Be(OperationalErrorCodes.OperatorReporterResolutionFailed);
        result.Failure.Stage.Should().Be("operator-reporter-resolution");
        result.Failure.Retryable.Should().Be(retryable);
    }

    [Fact]
    public async Task BuildAsync_WithVerifiedExactOperatorMapping_SelectsOnlyMappedAssignee()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService service = CreateService(
            new StubResolver(RequesterResolutionResult.Found("jira-requester")),
            "Block",
            "VerifiedOperatorMapping",
            [new JiraOperatorAssigneeMappingOptions
            {
                SecureOpsActor = "EXAMPLE\\operator",
                JiraUsername = "verified.operator"
            }]);

        OperationalRecordResult<JiraIssueDraft> mapped = await service.BuildAsync(
            record,
            "example\\OPERATOR",
            CancellationToken.None);
        OperationalRecordResult<JiraIssueDraft> unknown = await service.BuildAsync(
            record,
            "EXAMPLE\\unknown",
            CancellationToken.None);

        mapped.Value!.AssigneeUsername.Should().Be("verified.operator");
        unknown.Value!.AssigneeUsername.Should().BeNull();
        unknown.Value.Warnings.Should().ContainSingle(message => message.Contains("project default", StringComparison.OrdinalIgnoreCase));
        mapped.Value.IdempotencyKey.Should().NotBe(unknown.Value.IdempotencyKey);
    }

    [Fact]
    public async Task BuildAsync_WhenCreateMappingChanges_ChangesTransferFingerprint()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService original = CreateService(
            new StubResolver(RequesterResolutionResult.Found("jira-requester")),
            "Block",
            teamValue: "WASAS");
        JiraIssueDraftService changed = CreateService(
            new StubResolver(RequesterResolutionResult.Found("jira-requester")),
            "Block",
            teamValue: "Different Reviewed Team");

        JiraIssueDraft first = (await original.BuildAsync(record, "test:operator", CancellationToken.None)).Value!;
        JiraIssueDraft second = (await changed.BuildAsync(record, "test:operator", CancellationToken.None)).Value!;

        first.MappingVersion.Should().Be(second.MappingVersion);
        first.IdempotencyKey.Should().NotBe(second.IdempotencyKey);
    }

    private static JiraIssueDraftService CreateService(
        IJiraUserResolver resolver,
        string policy,
        string assignmentMode = "ProjectDefault",
        JiraOperatorAssigneeMappingOptions[]? mappings = null,
        string reporterMode = "ProjectDefault",
        string teamValue = "WASAS") => new(
        resolver,
        new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions())),
        Options.Create(new JiraIntegrationOptions
        {
            ProjectKey = "TEST",
            IssueType = "Task",
            IssueTypeId = "3",
            MappingVersion = "mapping-v1",
            UnresolvedRequesterPolicy = policy,
            TeamCustomField = "customfield_team",
            TeamValue = teamValue,
            RequesterWatcherCustomField = "customfield_requester",
            Labels = ["SunucuTalep"],
            AssignmentMode = assignmentMode,
            OperatorAssigneeMappings = mappings ?? [],
            ReporterMode = reporterMode
        }));

    private sealed class StubResolver(params RequesterResolutionResult[] results) : IRequesterResolver
    {
        private readonly Queue<RequesterResolutionResult> _results = new(results);

        public List<string> Identities { get; } = [];

        public Task<RequesterResolutionResult> ResolveExactAsync(string identity, CancellationToken cancellationToken)
        {
            Identities.Add(identity);
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        }
    }
}
