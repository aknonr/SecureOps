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
}

/// <summary>Completeness is explicit and cannot be inferred from an empty result.</summary>
public sealed record InUseBatch(IReadOnlyList<InUseSource> Records, bool Complete, string? Issue = null);

/// <summary>Local workflow persistence with atomic audit and concurrency.</summary>
public interface IInUseRepository
{
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
