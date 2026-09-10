using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Deterministic local substitute; audit completes before state is published.</summary>
public sealed class InMemoryInUseRepository(IAuditWriter audit) : IInUseRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<Guid, InUseRecord> _records = [];
    private InUseRefreshState _state = InUseRefreshState.Empty;

    /// <inheritdoc />
    public async Task<InUseOverview> OverviewAsync(AuditEvent evidence, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        { await audit.WriteAsync(evidence, cancellationToken); return InUseProgress.Summarize(_records.Values.ToArray(), _state, DateTimeOffset.UtcNow); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryAcquireRefreshAsync(CancellationToken cancellationToken) =>
        await _refreshGate.WaitAsync(0, cancellationToken) ? new RefreshLease(_refreshGate) : null;

    private sealed class RefreshLease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            { gate.Release(); }
            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public async Task<InUsePage> QueryAsync(InUseQuery query, Guid actorId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            InUseRecord[] rows = [.. _records.Values.Where(r =>
                (string.IsNullOrWhiteSpace(query.Search) || (r.Source.Code + " " + r.Source.Title).Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
                && (query.Status is null || r.Status == query.Status)
                && (query.View == "all" || query.View == "mine" && r.AssigneeId == actorId || query.View == "unassigned" && r.AssigneeId is null))
                .OrderBy(r => r.Source.Code, StringComparer.Ordinal).ThenBy(r => r.Id)];
            return new(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), rows.Length, query.Page, query.PageSize, _state);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<InUseRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        { return _records.GetValueOrDefault(id); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<InUseRefreshState> StateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        { return _state; }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(long expectedVersion, InUseBatch? batch, string? error, AuditEvent evidence, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_state.Version != expectedVersion)
            { return false; }
            InUseRecord[] next = batch is null ? [] : batch.Records.Select(source => InUseState.Merge(
                _records.Values.SingleOrDefault(r => r.Source.Id == source.Id), source, evidence.OccurredAt)).ToArray();
            await audit.WriteAsync(evidence, cancellationToken);
            foreach (InUseRecord record in next)
            { _records[record.Id] = record; }
            _state = InUseState.Refresh(_state, batch, error, evidence.OccurredAt);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public Task<bool> SaveAsync(InUseRecord next, long expectedVersion, AuditEvent evidence, CancellationToken cancellationToken) =>
        WriteAsync(next.Id, expectedVersion, next, evidence, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExportAsync(Guid id, long expectedVersion, AuditEvent evidence, CancellationToken cancellationToken) =>
        WriteAsync(id, expectedVersion, null, evidence, cancellationToken);

    private async Task<bool> WriteAsync(Guid id, long expectedVersion, InUseRecord? next, AuditEvent evidence, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_records.TryGetValue(id, out InUseRecord? current) || current.Version != expectedVersion)
            { return false; }
            await audit.WriteAsync(evidence, cancellationToken);
            if (next is not null)
            { _records[id] = next; }
            return true;
        }
        finally { _gate.Release(); }
    }
}
