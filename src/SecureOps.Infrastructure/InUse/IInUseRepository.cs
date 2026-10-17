using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Separate, bounded read-only source boundary; no write methods.</summary>
public interface IInUseSourceClient
{
    /// <summary>Reads one explicitly scoped bounded batch.</summary>
    public Task<InUseBatch> DiscoverAsync(CancellationToken cancellationToken);
    /// <summary>Explicit diagnostic only; providers without an evidenced query refuse it.</summary>
    public Task<System.Text.Json.JsonElement> DiagnoseAsync(string sourceId, CancellationToken token) =>
        throw new InvalidOperationException("Relationship diagnostics are unavailable for this provider.");
    /// <summary>
    /// Re-checks already known records by exact source id, read-only and bounded (TH-11): discovery is filtered by the
    /// WASAS group, so a record that left it is never rediscovered. Providers without such a read return no patches and
    /// zero unchecked; a record the source could not prove is counted unchecked and keeps what it had.
    /// </summary>
    public Task<InUseRecheckResult> RecheckAsync(IReadOnlyList<string> sourceIds, CancellationToken cancellationToken) =>
        Task.FromResult(new InUseRecheckResult(new Dictionary<string, InUseEvidencePatch>(), 0));
}

/// <summary>Server-classified evidence of one re-checked record; a null field is unproven and leaves the stored value.</summary>
public sealed record InUseEvidencePatch(InUseEvidence? WasasActivity, InUseEvidence? Lifecycle, InUseEvidence? CurrentStage)
{
    /// <summary>Nothing was proven.</summary>
    public bool IsEmpty => WasasActivity is null && Lifecycle is null && CurrentStage is null;

    /// <summary>Overlays only the proven fields.</summary>
    public InUseSource Apply(InUseSource source) => source with
    {
        WasasActivity = WasasActivity ?? source.WasasActivity,
        Lifecycle = Lifecycle ?? source.Lifecycle,
        CurrentStage = CurrentStage ?? source.CurrentStage
    };
}

/// <summary>Patches by source id and how many requested records could not be checked (over the bound, budget or unreadable).</summary>
public sealed record InUseRecheckResult(IReadOnlyDictionary<string, InUseEvidencePatch> Patches, int Unchecked);

/// <summary>Completeness is explicit and cannot be inferred from an empty result.</summary>
public sealed record InUseBatch(IReadOnlyList<InUseSource> Records, bool Complete, string? Issue = null)
{
    /// <summary>Evidence of known records that discovery did not return, applied to those retained records only (TH-11).</summary>
    public IReadOnlyDictionary<string, InUseEvidencePatch>? Rechecked { get; init; }
    /// <summary>Known records that were not re-checked or could not be proven; they stay listed (fail closed).</summary>
    public int RecheckUnchecked { get; init; }
}

/// <summary>Local workflow persistence with atomic audit and concurrency.</summary>
public interface IInUseRepository
{
    /// <summary>Stored reviews of one verified source identity, with filtering before count and paging.</summary>
    public Task<(IReadOnlyList<InUseServerReview> Items, int Total)> HistoryAsync(string identityKey, string? search, int page, int pageSize, CancellationToken token);
    /// <summary>Reads one immutable review for server-side reuse validation.</summary>
    public Task<InUseServerReview?> ReviewAsync(Guid reviewId, CancellationToken token);
    /// <summary>Aggregates a consistent stored snapshot; never calls the source.</summary>
    public Task<InUseOverview> OverviewAsync(AuditEvent audit, CancellationToken cancellationToken);
    /// <summary>Exclusively owns the discovery scope until disposal; null means another refresh is running.</summary>
    public Task<IAsyncDisposable?> TryAcquireRefreshAsync(CancellationToken cancellationToken);
    /// <summary>Reads a bounded persisted page, without source calls.</summary>
    public Task<InUsePage> QueryAsync(InUseQuery query, Guid actorId, CancellationToken cancellationToken);
    /// <summary>Gets one persisted record.</summary>
    public Task<InUseRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Reads refresh metadata.</summary>
    public Task<InUseRefreshState> StateAsync(CancellationToken cancellationToken);
    /// <summary>Applies only a newer refresh, retaining all absent records.</summary>
    public Task<bool> RefreshAsync(long expectedVersion, InUseBatch? batch, string? error, AuditEvent audit, CancellationToken cancellationToken);
    /// <summary>Commits an exact-version local change and audit together.</summary>
    public Task<bool> SaveAsync(InUseRecord next, long expectedVersion, AuditEvent audit, CancellationToken cancellationToken);
    /// <summary>Audits report preparation only if its reviewed aggregate is still current.</summary>
    public Task<bool> ExportAsync(Guid id, long expectedVersion, AuditEvent audit, CancellationToken cancellationToken);
}

/// <summary>Safe application result without source payloads or exception messages.</summary>
public sealed record InUseResult<T>(T? Value, string? Error = null, string? Detail = null)
{
    /// <summary>Creates an expected failure.</summary>
    public static InUseResult<T> Fail(string code) => new(default, code);
}
