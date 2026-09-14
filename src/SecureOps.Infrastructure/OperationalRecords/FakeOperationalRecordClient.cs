using System.Collections.Concurrent;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Deterministic synthetic operational-record source for Development, Demo, and Test.</summary>
public sealed class FakeOperationalRecordClient : IOperationalRecordClient
{
    internal const string EligibleSourceId = "synthetic-or-eligible";
    internal const string StaleSourceId = "synthetic-or-stale";
    internal const string ClosedSourceId = "synthetic-or-closed";
    internal const string MissingSourceId = "synthetic-or-missing";
    internal const string Requester = "synthetic.requester";

    private static readonly DateTimeOffset _createdAt = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly ConcurrentDictionary<string, OperationalRecordSourceItem> _stableRecords = new(StringComparer.Ordinal);

    /// <summary>Initializes a fresh deterministic source dataset.</summary>
    public FakeOperationalRecordClient()
    {
        OperationalRecordSourceItem eligible = Create(EligibleSourceId, "SYN-OR-100", "Synthetic eligible request", "eligible-v1");
        _stableRecords[eligible.SourceRecordId] = eligible;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<OperationalRecordSourceItem> available = [];
        if (_stableRecords.TryGetValue(EligibleSourceId, out OperationalRecordSourceItem? eligible) && eligible.IsOpen)
        {
            available.Add(eligible);
        }

        available.AddRange([
            Create(StaleSourceId, "SYN-OR-200", "Synthetic changed request", "stale-v1"),
            Create(ClosedSourceId, "SYN-OR-300", "Synthetic closed request", "closed-v1"),
            Create(MissingSourceId, "SYN-OR-400", "Synthetic missing request", "missing-v1")
        ]);
        IReadOnlyList<OperationalRecordSourceItem> records = available
            .Take(maximumCount)
            .ToArray();
        return Task.FromResult(records);
    }

    /// <inheritdoc />
    public Task<OperationalRecordSourceItem?> GetByIdAsync(string sourceRecordId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_stableRecords.TryGetValue(sourceRecordId, out OperationalRecordSourceItem? stable))
        {
            return Task.FromResult<OperationalRecordSourceItem?>(stable);
        }

        OperationalRecordSourceItem? record = sourceRecordId switch
        {
            StaleSourceId => Create(StaleSourceId, "SYN-OR-200", "Synthetic changed request", "stale-v2") with
            {
                Description = "Synthetic source content changed after import.",
                LastModifiedAt = _createdAt.AddHours(2)
            },
            ClosedSourceId => Create(ClosedSourceId, "SYN-OR-300", "Synthetic closed request", "closed-v1") with { IsOpen = false },
            MissingSourceId => null,
            _ => null
        };
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_stableRecords.TryGetValue(sourceRecordId, out OperationalRecordSourceItem? record)
            || !string.Equals(record.OrCode, orCode, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(jiraIssueKey))
        {
            return Task.FromException(new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, false));
        }

        _stableRecords[sourceRecordId] = record with { IsOpen = false, LastModifiedAt = _createdAt.AddHours(3) };
        return Task.CompletedTask;
    }

    internal static bool IsSyntheticSourceId(string sourceRecordId) =>
        sourceRecordId is EligibleSourceId or StaleSourceId or ClosedSourceId or MissingSourceId;

    private static OperationalRecordSourceItem Create(string sourceRecordId, string orCode, string title, string versionToken) => new(
        sourceRecordId,
        orCode,
        title,
        "Synthetic operational record for local workflow verification.",
        Requester,
        _createdAt,
        "SYNTHETIC",
        "synthetic-server",
        "synthetic-application",
        true,
        versionToken,
        _createdAt.AddHours(1));
}
