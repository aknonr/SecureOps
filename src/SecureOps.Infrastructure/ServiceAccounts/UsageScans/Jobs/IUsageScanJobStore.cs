using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

/// <summary>
/// Persistence of web usage scan jobs (migration 037) plus the evidence write into the existing 030 tables. The SQL implementation is
/// <see cref="SqlServiceAccountRepository"/>; tests use an in-memory one. Every write that belongs to a running job is fenced by the
/// job's lease, so a job whose lease expired (and was closed as <c>workerLost</c>) can no longer be changed by the old runner.
/// </summary>
public interface IUsageScanJobStore
{
    /// <summary>Whether migration 037 is installed.</summary>
    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a Queued job with its accounts (active) and servers (Pending), with history and the <c>UsageScanJobRequested</c> audit row
    /// in the same transaction. Returns null, or the id of an account that already has an active job (nothing is written then).
    /// </summary>
    public Task<Guid?> CreateAsync(NewUsageScanJob job, SaActor actor, CancellationToken cancellationToken);

    /// <summary>The job with its accounts and servers (documents excluded), or null.</summary>
    public Task<UsageScanJobRecord?> GetAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Matched items of servers finished after <paramref name="afterSeq"/>, whole servers only, until <paramref name="maxItems"/> is reached.</summary>
    public Task<IReadOnlyList<UsageScanJobFoundItem>> ItemsAfterAsync(Guid jobId, int afterSeq, int maxItems, CancellationToken cancellationToken);

    /// <summary>The newest jobs of one account (headers and servers), newest first.</summary>
    public Task<IReadOnlyList<UsageScanJobRecord>> ForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken);

    /// <summary>Ids of the active (Queued or Running) jobs holding any of these accounts.</summary>
    public Task<IReadOnlyList<Guid>> ActiveJobsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    /// <summary>
    /// Records a cancel request. A Queued job is closed as Cancelled at once (no server was read); a Running job keeps running until
    /// the runner sees the request. False when the job is already finished.
    /// </summary>
    public Task<bool> RequestCancelAsync(Guid jobId, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Closes jobs among <paramref name="jobIds"/> that can no longer finish: Running with an expired lease (<c>workerLost</c>) or Queued
    /// since before <paramref name="queuedBefore"/> (<c>notStarted</c>). Their accounts are released. Returns how many were closed.
    /// </summary>
    public Task<int> ExpireAsync(IReadOnlyCollection<Guid> jobIds, DateTimeOffset now, DateTimeOffset queuedBefore, CancellationToken cancellationToken);

    /// <summary>Queued → Running under a new lease. False when not claimable (already claimed, finished or cancel requested — then it is closed).</summary>
    public Task<bool> ClaimAsync(Guid jobId, Guid leaseId, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    /// <summary>Extends the lease. Returns whether the lease is still held and whether a cancel was requested.</summary>
    public Task<(bool Owned, bool CancelRequested)> HeartbeatAsync(Guid jobId, Guid leaseId, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    /// <summary>Pending → Connecting for one server (live view only).</summary>
    public Task MarkConnectingAsync(Guid jobId, Guid leaseId, string serverName, CancellationToken cancellationToken);

    /// <summary>Records one server's final result with the next finish sequence. False when the lease is lost or the server already finished.</summary>
    public Task<bool> CompleteServerAsync(Guid jobId, Guid leaseId, UsageScanServerCompletion completion, CancellationToken cancellationToken);

    /// <summary>The stored (validated) documents of the Ok servers, by server name.</summary>
    public Task<IReadOnlyDictionary<string, byte[]>> DocumentsAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Stores the evidence scan (030, append-only) once and links it to one account under the starter, re-checking scope in the write.</summary>
    public Task<SaResult<(Guid ScanId, bool Attached)>> AttachEvidenceAsync(Guid accountId, UsageScanUpload upload, SaActor actor, CancellationToken cancellationToken);

    /// <summary>
    /// Finishes the job (fenced by <paramref name="leaseId"/> when given): unfinished servers become <c>NotRun</c>, accounts are released with
    /// their link outcome, history per account and one audit row are written. False when the job was already finished or the lease lost.
    /// </summary>
    public Task<bool> FinishAsync(Guid jobId, Guid? leaseId, UsageScanJobFinish finish, SaActor actor, CancellationToken cancellationToken);
}

/// <summary>A job to create.</summary>
public sealed record NewUsageScanJob(Guid Id, string Purpose, string? ExpectedAccount, IReadOnlyList<UsageScanJobAccountInput> Accounts,
    IReadOnlyList<string> Servers, string PlanSha256, string Collector, Guid? RetryOfJobId, Guid StartedBy, DateTimeOffset CreatedAt);

/// <summary>One account of a new job and the name the collector searches for it.</summary>
public sealed record UsageScanJobAccountInput(Guid AccountId, string SearchedName);

/// <summary>One account of a stored job.</summary>
public sealed record UsageScanJobAccount(Guid AccountId, string SearchedName, bool IsActive, string? LinkOutcome);

/// <summary>One planned server of a stored job.</summary>
public sealed record UsageScanJobServer(int Ordinal, string ServerName, UsageScanJobServerState State, UsageCollectionStatus? Status,
    ScanServerResult? DocumentResult, int? FormerMatches, int? ExpectedMatches, string? RefusalCode, int? ElapsedMs, DateTimeOffset? FinishedAt, int? FinishSeq);

/// <summary>A stored job.</summary>
public sealed record UsageScanJobRecord(Guid Id, UsageScanJobState State, string Purpose, string? ExpectedAccount, IReadOnlyList<string> SearchedNames,
    int PlannedServers, string PlanSha256, string Collector, Guid? RetryOfJobId, Guid StartedBy, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, DateTimeOffset? CancelRequestedAt, string? FailureCode, Guid? ScanId, DateTimeOffset? LeaseUntil,
    IReadOnlyList<UsageScanJobAccount> Accounts, IReadOnlyList<UsageScanJobServer> Servers);

/// <summary>A matched component of a finished server, with the server's finish sequence.</summary>
public sealed record UsageScanJobFoundItem(int FinishSeq, ParsedScanItem Item);

/// <summary>One server's final result as the runner records it.</summary>
public sealed record UsageScanServerCompletion(string ServerName, UsageCollectionStatus Status, ScanServerResult? DocumentResult, byte[]? Document,
    IReadOnlyList<ParsedScanItem> Items, string? RefusalCode, int ElapsedMs, DateTimeOffset FinishedAt);

/// <summary>How a job ends.</summary>
public sealed record UsageScanJobFinish(UsageScanJobState State, string? FailureCode, Guid? ScanId, IReadOnlyDictionary<Guid, string> LinkOutcomes,
    DateTimeOffset At);
