namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Local empty operational-record source that performs no external I/O.</summary>
public sealed class FakeOperationalRecordClient : IOperationalRecordClient
{
    /// <inheritdoc />
    public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OperationalRecordSourceItem>>(Array.Empty<OperationalRecordSourceItem>());
    }

    /// <inheritdoc />
    public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
