using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class JiraIssueDraftServiceTests
{
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

    private static JiraIssueDraftService CreateService(
        IJiraUserResolver resolver,
        string policy,
        string assignmentMode = "ProjectDefault",
        JiraOperatorAssigneeMappingOptions[]? mappings = null,
        string reporterMode = "ProjectDefault") => new(
        resolver,
        new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions())),
        Options.Create(new JiraIntegrationOptions
        {
            ProjectKey = "TEST",
            IssueType = "Task",
            MappingVersion = "mapping-v1",
            UnresolvedRequesterPolicy = policy,
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
