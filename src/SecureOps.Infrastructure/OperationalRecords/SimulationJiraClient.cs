using System.Collections.Concurrent;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>In-process TEST simulation of deterministic Jira outcomes. It performs no network I/O.</summary>
public sealed class SimulationJiraClient : IJiraClient
{
    private readonly ConcurrentDictionary<string, string> _issues = new(StringComparer.Ordinal);
    private int _sequence;

    /// <inheritdoc />
    public Task<JiraIssueCreationResult> CreateIssueAsync(
        JiraIssueDraft draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(draft.OrCode, "SIM-OR-300", StringComparison.Ordinal))
        {
            return Task.FromException<JiraIssueCreationResult>(new ExternalIntegrationException(
                OperationalErrorCodes.JiraCreateFailed,
                retryable: true));
        }

        if (string.Equals(draft.OrCode, "SIM-OR-400", StringComparison.Ordinal))
        {
            return Task.FromException<JiraIssueCreationResult>(new ExternalIntegrationException(
                OperationalErrorCodes.JiraCreateFailed,
                retryable: false,
                outcomeUnknown: true));
        }

        string key = _issues.GetOrAdd(
            draft.IdempotencyKey,
            _ => $"SIM-{Interlocked.Increment(ref _sequence)}");
        return Task.FromResult(new JiraIssueCreationResult(key));
    }
}
