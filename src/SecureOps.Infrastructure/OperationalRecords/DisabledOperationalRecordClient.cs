using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Fail-closed source used until an approved external adapter is configured.</summary>
public sealed class DisabledOperationalRecordClient : IOperationalRecordClient
{
    /// <inheritdoc />
    public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<OperationalRecordSourceItem>>(Unavailable(cancellationToken));

    /// <inheritdoc />
    public Task<OperationalRecordSourceItem?> GetByIdAsync(string sourceRecordId, CancellationToken cancellationToken) =>
        Task.FromException<OperationalRecordSourceItem?>(Unavailable(cancellationToken));

    /// <inheritdoc />
    public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken) =>
        Task.FromException(Unavailable(cancellationToken));

    private static Exception Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceUnavailable, false);
    }
}
