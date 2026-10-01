using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Deterministic local substitute; audit completes before state is published.</summary>
public sealed class InMemoryInUseRepository(IAuditWriter audit) : IInUseRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<Guid, InUseRecord> _records = [];
    private readonly List<InUseServerReview> _reviews = [];
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
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var matches = _records.Values.Where(r =>
                Matches(r.Source, query.Search)
                && (query.Status == "Discarded" ? r.Discarded : !r.Discarded && (query.Status is null || r.Status == query.Status))
                && (query.View == "all" || query.View == "mine" && r.AssigneeId == actorId || query.View == "unassigned" && r.AssigneeId is null
                    || query.View == "tracking" && r.TrackingOnly || query.View == "review" && !r.TrackingOnly
                    || query.View == "pending" && r.ActivityStatus == "Pending"
                    || query.View == "verification" && r.ActivityStatus == "VerificationPending"))
                .Select(r => new { Record = r, Created = query.Sort == "code" ? null : InUseProgress.Created(r.Source, now) });
            var knownFirst = matches.OrderBy(r => r.Created is null);
            var ordered = query.Sort == "newest" ? knownFirst.ThenByDescending(r => r.Created) : knownFirst.ThenBy(r => r.Created);
            InUseRecord[] rows = ordered.ThenBy(r => r.Record.Source.Code, StringComparer.Ordinal)
                .ThenBy(r => r.Record.Id.ToString("D"), StringComparer.Ordinal).Select(r => r.Record).ToArray();
            return new(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), rows.Length, query.Page, query.PageSize, _state);
        }
        finally { _gate.Release(); }
    }

    private static bool Matches(InUseSource source, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        { return true; }
        string term = search.Trim();
        bool Match(string? value, bool turkish = false) => value is not null && (turkish
            ? System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo.IndexOf(value, term, System.Globalization.CompareOptions.IgnoreCase) >= 0
            : value.Contains(term, StringComparison.OrdinalIgnoreCase));
        return Match(source.Code) || Match(source.Title, true) || source.Servers.Any(server =>
            Match(server.Fields.GetValueOrDefault("HOSTNAME")?.Value)
            || (server.RelatedRequestReporter is { State: "ExactMatch" or "Stale" } reporter
                && ((reporter.DisplayState == "Returned" && Match(reporter.Display, true))
                    || (reporter.ReferenceState == "Returned" && Match(reporter.UserReference)))));
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
            InUseRecord[] next = (batch?.Records ?? []).Select(source => InUseState.Merge(
                _records.Values.SingleOrDefault(r => r.Source.Id == source.Id), source, evidence.OccurredAt))
                .Concat(_records.Values.Where(r => !(batch?.Records.Any(s => s.Id == r.Source.Id) ?? false))
                    .Select(InUseState.RetainUnobserved)).ToArray();
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
            if (evidence.Operation?.Action == "InUseDraftSaved" && (next?.Draft?.Answers ?? []).Any(a =>
                a.Origin?.ReviewId is Guid reviewId && (_reviews.SingleOrDefault(r => r.Id == reviewId) is not { } review || WithInvalidation(review).Invalidated)))
            { return false; }
            await audit.WriteAsync(evidence, cancellationToken);
            if (next is not null)
            {
                _records[id] = next;
                if (evidence.Operation?.Action == "InUseDraftSaved")
                { _reviews.AddRange(InUseReviewHistory.Snapshots(next)); }
            }
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<InUseServerReview> Items, int Total)> HistoryAsync(string identityKey, string? search,
        int page, int pageSize, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            InUseServerReview[] rows = _reviews.Where(r => r.IdentityKey == identityKey && (string.IsNullOrWhiteSpace(search)
                || System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo.IndexOf(InUseReviewHistory.SearchText(r),
                    search.Trim(), System.Globalization.CompareOptions.IgnoreCase) >= 0))
                .OrderByDescending(r => r.ReviewedAt).ThenBy(r => r.Id).ToArray();
            return (rows.Skip((page - 1) * pageSize).Take(pageSize).Select(WithInvalidation).ToArray(), rows.Length);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<InUseServerReview?> ReviewAsync(Guid reviewId, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        { return _reviews.SingleOrDefault(r => r.Id == reviewId) is { } review ? WithInvalidation(review) : null; }
        finally { _gate.Release(); }
    }

    private InUseServerReview WithInvalidation(InUseServerReview review) => review with
    { Invalidated = !_records.TryGetValue(review.RecordId, out InUseRecord? record) || record.Discarded || review.RecordVersion <= record.InvalidatedReviewsThrough };
}
