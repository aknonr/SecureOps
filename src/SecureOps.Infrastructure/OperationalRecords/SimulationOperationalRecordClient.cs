using System.Collections.Concurrent;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Deterministic, in-process workflow source used only by the explicit TEST simulation provider.</summary>
public sealed class SimulationOperationalRecordClient : IOperationalRecordClient
{
    /// <summary>Safe operator-facing warning attached to simulation API responses.</summary>
    public const string OperatorNotice = "Simulation mode: no real Jira issue will be created.";
    internal const string Requester = "synthetic.requester";
    internal const string HappySourceId = "simulation-happy";
    internal const string StaleSourceId = "simulation-source-stale";
    internal const string JiraFailureSourceId = "simulation-jira-failure";
    internal const string UnknownOutcomeSourceId = "simulation-jira-unknown";
    internal const string CloseFailureSourceId = "simulation-source-close-failure";

    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly ConcurrentDictionary<string, OperationalRecordSourceItem> _records;
    private int _closeFailureAttempts;

    /// <summary>Initializes a fresh deterministic simulation dataset.</summary>
    public SimulationOperationalRecordClient()
    {
        _records = new(StringComparer.Ordinal)
        {
            [HappySourceId] = Create(HappySourceId, "SIM-OR-100", "Synthetic happy path", "happy-v1"),
            [JiraFailureSourceId] = Create(JiraFailureSourceId, "SIM-OR-300", "Synthetic Jira failure", "jira-failure-v1"),
            [UnknownOutcomeSourceId] = Create(UnknownOutcomeSourceId, "SIM-OR-400", "Synthetic unknown Jira outcome", "jira-unknown-v1"),
            [CloseFailureSourceId] = Create(CloseFailureSourceId, "SIM-OR-500", "Synthetic source completion failure", "close-failure-v1")
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OperationalRecordSourceItem stale = Create(
            StaleSourceId,
            "SIM-OR-200",
            "Synthetic changed source",
            "stale-v1");
        IReadOnlyList<OperationalRecordSourceItem> result = _records.Values
            .Where(item => item.IsOpen)
            .Append(stale)
            .OrderBy(item => item.OrCode, StringComparer.Ordinal)
            .Take(maximumCount)
            .ToArray();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<OperationalRecordSourceItem?> GetByIdAsync(
        string sourceRecordId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(sourceRecordId, StaleSourceId, StringComparison.Ordinal))
        {
            return Task.FromResult<OperationalRecordSourceItem?>(Create(
                StaleSourceId,
                "SIM-OR-200",
                "Synthetic changed source",
                "stale-v2") with
            {
                Description = "Synthetic source content changed after preview.",
                LastModifiedAt = CreatedAt.AddHours(2)
            });
        }

        return Task.FromResult(_records.GetValueOrDefault(sourceRecordId));
    }

    /// <inheritdoc />
    public Task CloseAsync(
        string sourceRecordId,
        string orCode,
        string jiraIssueKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(sourceRecordId, CloseFailureSourceId, StringComparison.Ordinal)
            && Interlocked.Increment(ref _closeFailureAttempts) == 1)
        {
            return Task.FromException(new ExternalIntegrationException(
                OperationalErrorCodes.OperationalRecordCloseFailed,
                retryable: true));
        }

        if (!_records.TryGetValue(sourceRecordId, out OperationalRecordSourceItem? record)
            || !string.Equals(record.OrCode, orCode, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(jiraIssueKey))
        {
            return Task.FromException(new ExternalIntegrationException(
                OperationalErrorCodes.OperationalRecordCloseFailed,
                retryable: false));
        }

        _records[sourceRecordId] = record with
        {
            IsOpen = false,
            LastModifiedAt = CreatedAt.AddHours(3)
        };
        return Task.CompletedTask;
    }

    internal static bool IsSimulationSourceId(string sourceRecordId) => sourceRecordId is
        HappySourceId or StaleSourceId or JiraFailureSourceId or UnknownOutcomeSourceId or CloseFailureSourceId;

    private static OperationalRecordSourceItem Create(
        string sourceRecordId,
        string orCode,
        string title,
        string versionToken) => new(
            sourceRecordId,
            orCode,
            title,
            "Synthetic operational record for deterministic TEST simulation.",
            Requester,
            CreatedAt,
            "SIMULATION",
            "synthetic-server",
            "synthetic-application",
            true,
            versionToken,
            CreatedAt.AddHours(1));
}
