using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// In-memory <see cref="IUsageScanJobStore"/> with the same rules as migration 037 and its SQL implementation: one active job per
/// account, lease-fenced writes, finished servers never change, unfinished servers become NotRun at the end, accounts released with
/// their link outcome. The SQL behaviour itself is proven by <c>sa-037-guards.sql</c> and the integration tests.
/// </summary>
internal sealed class InMemoryUsageScanJobStore : IUsageScanJobStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Job> _jobs = [];

    public List<(Guid AccountId, UsageScanUpload Upload, SaActor Actor)> Evidence { get; } = [];

    public HashSet<Guid> RefuseLinkFor { get; } = [];

    public bool Installed { get; set; } = true;

    public bool LoseLease { get; set; }

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);

    public Task<Guid?> CreateAsync(NewUsageScanJob job, SaActor actor, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Guid? busy = job.Accounts.Select(a => a.AccountId).FirstOrDefault(id => _jobs.Values.Any(j => j.Accounts.Any(a => a.AccountId == id && a.IsActive)));
            if (busy is { } b && b != Guid.Empty)
            {
                return Task.FromResult<Guid?>(b);
            }

            _jobs[job.Id] = new Job
            {
                Id = job.Id,
                State = UsageScanJobState.Queued,
                Purpose = job.Purpose,
                Expected = job.ExpectedAccount,
                Names = [.. job.Accounts.Select(a => a.SearchedName)],
                Collector = job.Collector,
                RetryOf = job.RetryOfJobId,
                StartedBy = job.StartedBy,
                CreatedAt = job.CreatedAt,
                Accounts = [.. job.Accounts.Select(a => new UsageScanJobAccount(a.AccountId, a.SearchedName, true, null))],
                Servers = [.. job.Servers.Select((s, i) => new Server { Ordinal = i + 1, Name = s })]
            };
            return Task.FromResult<Guid?>(null);
        }
    }

    public Task<UsageScanJobRecord?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_jobs.TryGetValue(jobId, out Job? job) ? job.Record() : null);
        }
    }

    public Task<IReadOnlyList<UsageScanJobFoundItem>> ItemsAfterAsync(Guid jobId, int afterSeq, int maxItems, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            List<UsageScanJobFoundItem> items = [];
            foreach (Server server in _jobs[jobId].Servers.Where(s => s.Seq > afterSeq && s.Items.Count > 0).OrderBy(s => s.Seq))
            {
                if (items.Count > 0 && items.Count + server.Items.Count > maxItems)
                {
                    break;
                }

                items.AddRange(server.Items.Select(i => new UsageScanJobFoundItem(server.Seq!.Value, i)));
            }

            return Task.FromResult<IReadOnlyList<UsageScanJobFoundItem>>(items);
        }
    }

    public Task<IReadOnlyList<UsageScanJobRecord>> ForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<UsageScanJobRecord>>([.. _jobs.Values.Where(j => j.Accounts.Any(a => a.AccountId == accountId))
                .OrderByDescending(j => j.CreatedAt).Take(take).Select(j => j.Record())]);
        }
    }

    public Task<IReadOnlyList<Guid>> ActiveJobsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Guid>>([.. _jobs.Values.Where(j => j.Accounts.Any(a => a.IsActive && accountIds.Contains(a.AccountId)))
                .Select(j => j.Id)]);
        }
    }

    public Task<bool> RequestCancelAsync(Guid jobId, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Job job = _jobs[jobId];
            if (!UsageScanJobRules.IsActive(job.State))
            {
                return Task.FromResult(false);
            }

            job.CancelRequestedAt ??= now;
            if (job.State == UsageScanJobState.Queued)
            {
                Finish(job, new UsageScanJobFinish(UsageScanJobState.Cancelled, null, null, new Dictionary<Guid, string>(), now));
            }

            return Task.FromResult(true);
        }
    }

    public Task<int> ExpireAsync(IReadOnlyCollection<Guid> jobIds, DateTimeOffset now, DateTimeOffset queuedBefore, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            int closed = 0;
            foreach (Job job in jobIds.Select(id => _jobs[id]))
            {
                string? code = job.State == UsageScanJobState.Running && job.LeaseUntil < now ? "workerLost"
                    : job.State == UsageScanJobState.Queued && job.CreatedAt < queuedBefore ? "notStarted" : null;
                if (code is not null)
                {
                    Finish(job, new UsageScanJobFinish(UsageScanJobState.Failed, code, null, new Dictionary<Guid, string>(), now));
                    closed++;
                }
            }

            return Task.FromResult(closed);
        }
    }

    public Task<bool> ClaimAsync(Guid jobId, Guid leaseId, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Job job = _jobs[jobId];
            if (job.State != UsageScanJobState.Queued)
            {
                return Task.FromResult(false);
            }

            if (job.CancelRequestedAt is not null)
            {
                Finish(job, new UsageScanJobFinish(UsageScanJobState.Cancelled, null, null, new Dictionary<Guid, string>(), now));
                return Task.FromResult(false);
            }

            (job.State, job.StartedAt, job.LeaseId, job.LeaseUntil) = (UsageScanJobState.Running, now, leaseId, leaseUntil);
            return Task.FromResult(true);
        }
    }

    public Task<(bool Owned, bool CancelRequested)> HeartbeatAsync(Guid jobId, Guid leaseId, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Job job = _jobs[jobId];
            if (LoseLease || job.State != UsageScanJobState.Running || job.LeaseId != leaseId)
            {
                return Task.FromResult((false, false));
            }

            job.LeaseUntil = leaseUntil;
            return Task.FromResult((true, job.CancelRequestedAt is not null));
        }
    }

    public Task MarkConnectingAsync(Guid jobId, Guid leaseId, string serverName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Job job = _jobs[jobId];
            Server server = job.Servers.Single(s => s.Name == serverName);
            if (Owns(job, leaseId) && server.State == UsageScanJobServerState.Pending)
            {
                server.State = UsageScanJobServerState.Connecting;
            }

            return Task.CompletedTask;
        }
    }

    public Task<bool> CompleteServerAsync(Guid jobId, Guid leaseId, UsageScanServerCompletion completion, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Job job = _jobs[jobId];
            Server server = job.Servers.Single(s => s.Name == completion.ServerName);
            if (!Owns(job, leaseId) || server.State == UsageScanJobServerState.Done)
            {
                return Task.FromResult(false);
            }

            bool ok = completion.Status == UsageCollectionStatus.Ok;
            server.State = UsageScanJobServerState.Done;
            server.Status = completion.Status;
            server.Result = ok ? completion.DocumentResult : null;
            server.Document = ok ? completion.Document : null;
            server.Items = ok ? [.. completion.Items] : [];
            server.Refusal = completion.RefusalCode;
            server.Elapsed = completion.ElapsedMs;
            server.FinishedAt = completion.FinishedAt;
            server.Seq = job.Servers.Max(s => s.Seq ?? 0) + 1;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyDictionary<string, byte[]>> DocumentsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyDictionary<string, byte[]>>(_jobs[jobId].Servers.Where(s => s.Document is not null)
                .ToDictionary(s => s.Name, s => s.Document!, StringComparer.OrdinalIgnoreCase));
        }
    }

    public Task<SaResult<(Guid ScanId, bool Attached)>> AttachEvidenceAsync(Guid accountId, UsageScanUpload upload, SaActor actor, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (RefuseLinkFor.Contains(accountId))
            {
                return Task.FromResult(SaResult<(Guid, bool)>.Fail(SaErrors.NotFound));
            }

            Evidence.Add((accountId, upload, actor));
            return Task.FromResult(new SaResult<(Guid, bool)>((ScanIdFor(upload.Sha256), true)));
        }
    }

    public Task<bool> FinishAsync(Guid jobId, Guid? leaseId, UsageScanJobFinish finish, SaActor actor, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Job job = _jobs[jobId];
            if (!UsageScanJobRules.IsActive(job.State) || leaseId is { } lease && job.LeaseId != lease)
            {
                return Task.FromResult(false);
            }

            Finish(job, finish);
            return Task.FromResult(true);
        }
    }

    /// <summary>Test helper: moves the clock of a job (lease expiry or queue timeout).</summary>
    public void Age(Guid jobId, TimeSpan by)
    {
        lock (_gate)
        {
            Job job = _jobs[jobId];
            job.CreatedAt -= by;
            job.LeaseUntil -= by;
        }
    }

    internal static Guid ScanIdFor(string sha) => new(Convert.FromHexString(sha)[..16]);

    private bool Owns(Job job, Guid leaseId) => !LoseLease && job.State == UsageScanJobState.Running && job.LeaseId == leaseId;

    private static void Finish(Job job, UsageScanJobFinish finish)
    {
        int seq = job.Servers.Max(s => s.Seq ?? 0);
        foreach (Server server in job.Servers.Where(s => s.State != UsageScanJobServerState.Done).OrderBy(s => s.Ordinal))
        {
            (server.State, server.Status, server.FinishedAt, server.Seq) = (UsageScanJobServerState.Done, UsageCollectionStatus.NotRun, finish.At, ++seq);
        }

        job.Accounts = [.. job.Accounts.Select(a => a with
        {
            IsActive = false,
            LinkOutcome = finish.LinkOutcomes.TryGetValue(a.AccountId, out string? o) ? o : "NotLinked"
        })];
        (job.State, job.FailureCode, job.ScanId, job.FinishedAt, job.LeaseId, job.LeaseUntil) =
            (finish.State, finish.FailureCode, finish.ScanId, finish.At, null, null);
    }

    private sealed class Job
    {
        public Guid Id { get; init; }

        public UsageScanJobState State { get; set; }

        public string Purpose { get; init; } = "Discovery";

        public string? Expected { get; init; }

        public string[] Names { get; init; } = [];

        public string Collector { get; init; } = "Fake";

        public Guid? RetryOf { get; init; }

        public Guid StartedBy { get; init; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public DateTimeOffset? CancelRequestedAt { get; set; }

        public string? FailureCode { get; set; }

        public Guid? ScanId { get; set; }

        public Guid? LeaseId { get; set; }

        public DateTimeOffset? LeaseUntil { get; set; }

        public List<UsageScanJobAccount> Accounts { get; set; } = [];

        public List<Server> Servers { get; init; } = [];

        public UsageScanJobRecord Record() => new(Id, State, Purpose, Expected, Names, Servers.Count, new string('a', 64), Collector, RetryOf, StartedBy,
            CreatedAt, StartedAt, FinishedAt, CancelRequestedAt, FailureCode, ScanId, LeaseUntil, [.. Accounts],
            [.. Servers.Select(s => new UsageScanJobServer(s.Ordinal, s.Name, s.State, s.Status, s.Result,
                s.Status == UsageCollectionStatus.Ok ? s.Items.Count(i => i.Role == "Former") : null,
                s.Status == UsageCollectionStatus.Ok ? s.Items.Count(i => i.Role == "Expected") : null, s.Refusal, s.Elapsed, s.FinishedAt, s.Seq))]);
    }

    private sealed class Server
    {
        public int Ordinal { get; init; }

        public string Name { get; init; } = string.Empty;

        public UsageScanJobServerState State { get; set; }

        public UsageCollectionStatus? Status { get; set; }

        public ScanServerResult? Result { get; set; }

        public byte[]? Document { get; set; }

        public List<ParsedScanItem> Items { get; set; } = [];

        public string? Refusal { get; set; }

        public int? Elapsed { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public int? Seq { get; set; }
    }
}
