namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Replaceable server-side Jira integration boundary.</summary>
public interface IJiraClient
{
    /// <summary>Creates one issue from a validated draft.</summary>
    public Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken);
}
