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

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(record, CancellationToken.None);

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

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(record, CancellationToken.None);

        result.Failure!.Code.Should().Be(OperationalErrorCodes.RequesterResolutionAmbiguous);
    }

    [Fact]
    public async Task BuildAsync_WhenPolicyAllowsUnassigned_AddsSafeWarning()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        JiraIssueDraftService service = CreateService(new StubResolver(RequesterResolutionResult.NotFound()), "ProceedUnassigned");

        OperationalRecordResult<JiraIssueDraft> result = await service.BuildAsync(record, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        JiraIssueDraft draft = result.Value!;
        draft.RequesterAccountId.Should().BeNull();
        draft.Warnings.Should().ContainSingle();
    }

    private static JiraIssueDraftService CreateService(IRequesterResolver resolver, string policy) => new(
        resolver,
        Options.Create(new JiraIntegrationOptions
        {
            ProjectKey = "TEST",
            IssueType = "Task",
            MappingVersion = "mapping-v1",
            UnresolvedRequesterPolicy = policy
        }));

    private sealed class StubResolver(RequesterResolutionResult result) : IRequesterResolver
    {
        public Task<RequesterResolutionResult> ResolveExactAsync(string requester, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
