using FluentAssertions;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class DisabledJiraClientTests
{
    [Fact]
    public async Task CreateIssueAsync_FailsClosedWithoutCallingAnExternalSystem()
    {
        DisabledJiraClient client = new();

        Func<Task> act = async () => await client.CreateIssueAsync(null!, CancellationToken.None);

        ExternalIntegrationException exception = (await act.Should().ThrowAsync<ExternalIntegrationException>()).Which;
        exception.ErrorCode.Should().Be(OperationalErrorCodes.JiraUnavailable);
        exception.Retryable.Should().BeFalse();
    }
}
