using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

/// <summary>
/// Reads where service accounts run on a bounded list of servers, read-only, for one module scan job. This is the seam between
/// the Service Accounts module (job, storage, UI) and the Worker's JEA runner (connection, timeouts, parallelism); the
/// full contract, including what an implementation must never do, is <c>docs/service-accounts/USAGE-SCAN-JOB-CONTRACT.md</c>.
/// <para>
/// The module validates every input before calling (1–20 account names and 1–500 server names, each matching the
/// <c>service-account-usage-scan-v1</c> patterns, no duplicates), so an implementation never builds a command from free text.
/// The module validates every document after the call with the same parser as an uploaded scan (ADR-0027 §2).
/// </para>
/// </summary>
public interface IServiceAccountUsageCollector
{
    /// <summary>Stable kind matched against <c>ServiceAccounts:UsageScanJobs:Collector</c>: <see cref="UsageCollectorKinds.Fake"/> or <see cref="UsageCollectorKinds.Jea"/>.</summary>
    public string Kind { get; }

    /// <summary>
    /// Collects every server of <paramref name="request"/> and reports each one exactly once through
    /// <see cref="IUsageCollectionObserver.ServerCompletedAsync"/> (any order, possibly concurrently), then returns. Per-server
    /// failures are results, not exceptions. On cancellation it stops starting servers, may abandon running ones and returns
    /// (or throws <see cref="OperationCanceledException"/>); servers it did not report become <see cref="UsageCollectionStatus.NotRun"/>.
    /// Any other exception ends the whole job: unreported servers become <see cref="UsageCollectionStatus.NotRun"/>.
    /// </summary>
    public Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken);
}

/// <summary>
/// Receives per-server progress from a collector. Thread-safe: an implementation may call it from parallel workers. Calls are
/// awaited by the collector; a call may throw <see cref="OperationCanceledException"/> when the job is being cancelled.
/// </summary>
public interface IUsageCollectionObserver
{
    /// <summary>Optional: the collector starts connecting to <paramref name="serverName"/> (the UI shows "bağlanıyor").</summary>
    public ValueTask ServerStartingAsync(string serverName, CancellationToken cancellationToken);

    /// <summary>Required once per server: the final result for one requested server.</summary>
    public ValueTask ServerCompletedAsync(UsageCollectionServerResult result, CancellationToken cancellationToken);
}

/// <summary>One collection run. Names are already validated and de-duplicated by the module.</summary>
/// <param name="JobId">Module job id (correlation only; never sent to a server).</param>
/// <param name="Accounts">1–20 account names to search, as the endpoint function receives them (<c>[DOMAIN\]name[$]</c>).</param>
/// <param name="ExpectedAccount">Expected gMSA (ends with <c>$</c>) for a gMSA check, otherwise null; the document must then carry its verification block.</param>
/// <param name="Servers">1–500 planned server names (host or FQDN).</param>
/// <param name="Limits">Bounds the implementation must honour.</param>
public sealed record UsageCollectionRequest(Guid JobId, IReadOnlyList<string> Accounts, string? ExpectedAccount, IReadOnlyList<string> Servers,
    UsageCollectionLimits Limits);

/// <summary>Bounds of one run (ADR-0024 R2-4 defaults).</summary>
/// <param name="MaxParallel">Most servers in flight at once.</param>
/// <param name="OpenTimeout">Connection open timeout per server.</param>
/// <param name="OperationTimeout">Whole-call timeout per server after the connection opened.</param>
/// <param name="MaxDocumentBytes">Largest document accepted per server; a larger answer is <see cref="UsageCollectionStatus.Malformed"/>.</param>
public sealed record UsageCollectionLimits(int MaxParallel, TimeSpan OpenTimeout, TimeSpan OperationTimeout, int MaxDocumentBytes)
{
    /// <summary>32 in parallel, 15 s open, 180 s operation, 512 KiB per document.</summary>
    public static UsageCollectionLimits Default { get; } = new(32, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(180), 512 * 1024);
}

/// <summary>The final answer for one server.</summary>
/// <param name="ServerName">Exactly one of the requested names (case-insensitive); other names are ignored by the module.</param>
/// <param name="Status">What happened.</param>
/// <param name="Document">UTF-8 JSON of one <c>service-account-usage-v1</c> document when <paramref name="Status"/> is Ok; otherwise null. Never logged by the module.</param>
/// <param name="Elapsed">Time spent on this server (shown and used for the remaining-time estimate).</param>
public sealed record UsageCollectionServerResult(string ServerName, UsageCollectionStatus Status, byte[]? Document, TimeSpan Elapsed);

/// <summary>Collector kinds known to the module configuration.</summary>
public static class UsageCollectorKinds
{
    /// <summary>No collector: web scans are off (default; fail closed).</summary>
    public const string None = "None";

    /// <summary>Deterministic fake for tests and the local demo only (Development/Demo/Test environments); never contacts a server.</summary>
    public const string Fake = "Fake";

    /// <summary>The Worker's JEA runner (TARAMA-C, Codex); not part of the module.</summary>
    public const string Jea = "Jea";
}
