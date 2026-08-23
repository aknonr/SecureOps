using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
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
        IRequesterResolver resolver,
        string policy,
        string assignmentMode = "ProjectDefault",
        JiraOperatorAssigneeMappingOptions[]? mappings = null) => new(
        resolver,
        Options.Create(new JiraIntegrationOptions
        {
            ProjectKey = "TEST",
            IssueType = "Task",
            MappingVersion = "mapping-v1",
            UnresolvedRequesterPolicy = policy,
            AssignmentMode = assignmentMode,
            OperatorAssigneeMappings = mappings ?? []
        }));

    private sealed class StubResolver(RequesterResolutionResult result) : IRequesterResolver
    {
        public Task<RequesterResolutionResult> ResolveExactAsync(string requester, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
