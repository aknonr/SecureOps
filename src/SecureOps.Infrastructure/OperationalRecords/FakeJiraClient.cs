using System.Collections.Concurrent;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Deterministic in-process Jira fake used only for local development and tests.</summary>
public sealed class FakeJiraClient : IJiraClient
{
    private readonly ConcurrentDictionary<string, string> _issues = new(StringComparer.Ordinal);
    private int _sequence;

    /// <inheritdoc />
    public Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string issueKey = _issues.GetOrAdd(
            draft.IdempotencyKey,
            _ => $"FAKE-{Interlocked.Increment(ref _sequence)}");
        return Task.FromResult(new JiraIssueCreationResult(issueKey));
    }
}
