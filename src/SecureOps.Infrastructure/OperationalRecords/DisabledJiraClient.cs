using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Fail-closed Jira adapter used when no approved external integration exists.</summary>
public sealed class DisabledJiraClient : IJiraClient
{
    /// <inheritdoc />
    public Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken) =>
        Task.FromException<JiraIssueCreationResult>(new ExternalIntegrationException(
            OperationalErrorCodes.JiraUnavailable,
            retryable: false));
}
