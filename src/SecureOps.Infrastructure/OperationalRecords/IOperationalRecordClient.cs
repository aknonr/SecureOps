namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Replaceable bounded integration boundary for the operational-record source.</summary>
public interface IOperationalRecordClient
{
    /// <summary>Gets at most the configured number of active source records.</summary>
    public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken);

    /// <summary>Closes/updates one source record after a Jira issue key is durably persisted.</summary>
    public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken);
}
