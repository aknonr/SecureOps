using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;

/// <summary>
/// Runs one web usage scan job (ADR-0024 R2-4) on the Worker through the existing Hangfire queue. It claims the job under a lease,
/// re-checks the starter, hands the validated names to the configured collector, records every server as it finishes (document
/// validated by the ADR-0027 parser first), and at the end writes one ordinary 030 evidence record (<c>tool = Jea</c>) linked to each
/// account under the starter's identity. It never interprets a missing answer as "not used" and never retries a server on its own.
/// </summary>
public sealed class UsageScanJobRunner(IUsageScanJobStore? store, IEnumerable<IServiceAccountUsageCollector> collectors,
    UsageScanJobAvailability availability, IUsageScanJobStarterCheck starter, IOptions<ServiceAccountOptions> options, TimeProvider clock,
    ILogger<UsageScanJobRunner> logger)
{
    /// <summary>How often the lease is renewed and the cancel flag read.</summary>
    internal static TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);

    private readonly ServiceAccountUsageScanJobOptions _options = options.Value.UsageScanJobs;
    private readonly IUsageScanJobStore? _store = store;
    private readonly TimeProvider _clock = clock;
    private readonly ILogger<UsageScanJobRunner> _logger = logger;

    /// <summary>Executes one job. Hangfire retries are disabled: a scan is never repeated without a person asking.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (_store is null)
        {
            _logger.LogWarning("Usage scan job not run: the Service Accounts module is not configured on this host. JobId: {JobId}.", jobId);
            return;
        }

        var leaseId = Guid.NewGuid();
        var lease = TimeSpan.FromSeconds(Math.Clamp(_options.LeaseSeconds, 30, 3600));
        DateTimeOffset now = _clock.GetUtcNow();
        if (!await _store.ClaimAsync(jobId, leaseId, now, now + lease, cancellationToken))
        {
            _logger.LogInformation("Usage scan job was not claimable (already claimed, finished or cancelled). JobId: {JobId}.", jobId);
            return;
        }

        UsageScanJobRecord job = (await _store.GetAsync(jobId, cancellationToken))!;
        SaActor actor = new(job.StartedBy, "usage-scan-job:" + jobId.ToString("N"));
        if (!await starter.IsStillAllowedAsync(job.StartedBy, cancellationToken))
        {
            await FinishAsync(job, leaseId, UsageScanJobState.Failed, "accessRevoked", null, actor, cancellationToken);
            return;
        }

        IServiceAccountUsageCollector? collector = availability.CollectorAllowed(job.Collector)
            ? collectors.FirstOrDefault(c => string.Equals(c.Kind, job.Collector, StringComparison.Ordinal)) : null;
        if (collector is null)
        {
            _logger.LogError("Usage scan job not run: collector {Collector} is not available on this host. JobId: {JobId}.", job.Collector, jobId);
            await FinishAsync(job, leaseId, UsageScanJobState.Failed, "collectorUnavailable", null, actor, cancellationToken);
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool cancelRequested = false;
        bool leaseLost = false;
        Task heartbeat = HeartbeatAsync();
        Observer observer = new(this, job, leaseId);
        string? failure = null;
        try
        {
            await collector.CollectAsync(new UsageCollectionRequest(jobId, job.SearchedNames, job.ExpectedAccount,
                [.. job.Servers.OrderBy(s => s.Ordinal).Select(s => s.ServerName)], _options.Limits), observer, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Cancel requested, lease lost or host stopping: servers not reported become NotRun below.
        }
        catch (Exception exception)
        {
            // Never the message: a collector exception may carry remote text.
            _logger.LogError("Usage scan collector stopped unexpectedly. FailureType: {FailureType} JobId: {JobId}.", exception.GetType().Name, jobId);
            failure = "collectorFailed";
        }
        finally
        {
            await stop.CancelAsync();
            await heartbeat;
        }

        if (leaseLost)
        {
            _logger.LogWarning("Usage scan job lease was lost; another path closed it. JobId: {JobId}.", jobId);
            return;
        }

        // A person's cancel is Cancelled; a stopping host is not a person's decision, so it is a failure that keeps what finished.
        failure ??= !cancelRequested && cancellationToken.IsCancellationRequested ? "workerLost" : null;
        UsageScanJobState state = failure is not null ? UsageScanJobState.Failed
            : cancelRequested ? UsageScanJobState.Cancelled : UsageScanJobState.Completed;
        await FinalizeAsync(jobId, leaseId, state, failure, actor, CancellationToken.None);

        async Task HeartbeatAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatInterval, _clock, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    (bool owned, bool cancel) = await _store.HeartbeatAsync(jobId, leaseId, _clock.GetUtcNow() + lease, CancellationToken.None);
                    if (!owned || cancel)
                    {
                        leaseLost = !owned;
                        cancelRequested = cancel;
                        await stop.CancelAsync();
                        return;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One missed renewal is not fatal: the lease is minutes long. The next beat tries again.
                    _logger.LogWarning("Usage scan heartbeat failed. FailureType: {FailureType} JobId: {JobId}.", exception.GetType().Name, jobId);
                }
            }
        }
    }

    /// <summary>
    /// Builds the evidence from every finished server, validates it with the upload parser, links it to each account, and closes the
    /// job. A cancelled job keeps what finished; a job where no server finished stores no evidence.
    /// </summary>
    private async Task FinalizeAsync(Guid jobId, Guid leaseId, UsageScanJobState state, string? failure, SaActor actor, CancellationToken cancellationToken)
    {
        UsageScanJobRecord job = (await _store!.GetAsync(jobId, cancellationToken))!;
        if (!job.Servers.Any(s => s.State == UsageScanJobServerState.Done))
        {
            await FinishAsync(job, leaseId, state, failure, null, actor, cancellationToken);
            return;
        }

        IReadOnlyDictionary<string, byte[]> documents = await _store.DocumentsAsync(jobId, cancellationToken);
        var statuses = job.Servers.ToDictionary(s => s.ServerName,
            s => s.Status ?? UsageCollectionStatus.NotRun, StringComparer.OrdinalIgnoreCase);
        string[] planned = [.. job.Servers.OrderBy(s => s.Ordinal).Select(s => s.ServerName)];
        DateTimeOffset now = _clock.GetUtcNow();
        ParsedUsageScan parsed;
        byte[] bundle;
        try
        {
            bundle = UsageScanJobBundle.Build(job.SearchedNames, job.ExpectedAccount, planned, documents, statuses, now);
            parsed = UsageScanParser.Parse(bundle, int.MaxValue, now);
        }
        catch (UsageScanFileException refused)
        {
            _logger.LogError("Usage scan evidence refused by the parser. Code: {Code} JobId: {JobId}.", refused.Code, jobId);
            await FinishAsync(job, leaseId, UsageScanJobState.Failed, "evidenceRefused", null, actor, cancellationToken);
            return;
        }

        string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bundle)).ToLowerInvariant();
        string statement = UsageScanJobRules.RunStatement(job.Collector, jobId, state == UsageScanJobState.Cancelled);
        Dictionary<Guid, string> outcomes = [];
        Guid? scanId = null;
        foreach (UsageScanJobAccount account in job.Accounts)
        {
            try
            {
                UsageScanUpload upload = new(parsed, bundle, sha, $"web-tarama-{jobId:N}.json", statement, account.SearchedName);
                SaResult<(Guid ScanId, bool Attached)> attached = await _store.AttachEvidenceAsync(account.AccountId, upload, actor, cancellationToken);
                if (attached.Field == "scanTablesMissing")
                {
                    outcomes[account.AccountId] = "Failed";
                    continue;
                }

                scanId = attached.IsSuccess ? attached.Value.ScanId : scanId;
                outcomes[account.AccountId] = !attached.IsSuccess ? "Unavailable" : attached.Value.Attached ? "Attached" : "AlreadyAttached";
            }
            catch (Exception exception) when (exception is System.Data.Common.DbException or InvalidOperationException or TimeoutException)
            {
                _logger.LogError("Usage scan evidence link failed for one account. FailureType: {FailureType} JobId: {JobId}.", exception.GetType().Name, jobId);
                outcomes[account.AccountId] = "Failed";
            }
        }

        await FinishAsync(job, leaseId, state, failure, scanId, actor, cancellationToken, outcomes);
    }

    private async Task FinishAsync(UsageScanJobRecord job, Guid leaseId, UsageScanJobState state, string? failure, Guid? scanId, SaActor actor,
        CancellationToken cancellationToken, IReadOnlyDictionary<Guid, string>? outcomes = null)
    {
        var links = job.Accounts.ToDictionary(a => a.AccountId,
            a => outcomes is not null && outcomes.TryGetValue(a.AccountId, out string? outcome) ? outcome : "NotLinked");
        if (!await _store!.FinishAsync(job.Id, leaseId, new UsageScanJobFinish(state, state == UsageScanJobState.Failed ? failure ?? "collectorFailed" : null,
            scanId, links, _clock.GetUtcNow()), actor, cancellationToken))
        {
            _logger.LogWarning("Usage scan job could not be finished by this runner (lease lost). JobId: {JobId}.", job.Id);
        }
    }

    /// <summary>Per-job observer: validates and records each server once; calls are serialized so finish sequences stay simple.</summary>
    private sealed class Observer(UsageScanJobRunner runner, UsageScanJobRecord job, Guid leaseId) : IUsageCollectionObserver
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, string> _planned = job.Servers.ToDictionary(s => s.ServerName, s => s.ServerName, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);

        public async ValueTask ServerStartingAsync(string serverName, CancellationToken cancellationToken)
        {
            if (_planned.TryGetValue(serverName ?? string.Empty, out string? planned))
            {
                await runner._store!.MarkConnectingAsync(job.Id, leaseId, planned, cancellationToken);
            }
        }

        public async ValueTask ServerCompletedAsync(UsageCollectionServerResult result, CancellationToken cancellationToken)
        {
            if (result is null || !_planned.TryGetValue(result.ServerName ?? string.Empty, out string? server))
            {
                runner._logger.LogWarning("Usage scan collector reported a server that was not requested; ignored. JobId: {JobId}.", job.Id);
                return;
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_done.Add(server))
                {
                    runner._logger.LogWarning("Usage scan collector reported a server twice; the first result stands. JobId: {JobId}.", job.Id);
                    return;
                }

                UsageScanServerCompletion completion = Complete(server, result);
                await runner._store!.CompleteServerAsync(job.Id, leaseId, completion, CancellationToken.None);
            }
            finally
            {
                _gate.Release();
            }
        }

        private UsageScanServerCompletion Complete(string server, UsageCollectionServerResult result)
        {
            DateTimeOffset now = runner._clock.GetUtcNow();
            int elapsed = (int)Math.Clamp(result.Elapsed.TotalMilliseconds, 0, int.MaxValue);
            if (result.Status != UsageCollectionStatus.Ok || result.Document is null)
            {
                UsageCollectionStatus status = result.Status == UsageCollectionStatus.Ok ? UsageCollectionStatus.Malformed : result.Status;
                return new UsageScanServerCompletion(server, status, null, null, [], result.Status == UsageCollectionStatus.Ok ? "scanEmpty" : null, elapsed, now);
            }

            (ParsedUsageScan? parsed, string? refused) = UsageScanJobBundle.Validate(job.SearchedNames, job.ExpectedAccount, server, result.Document,
                runner._options.Limits.MaxDocumentBytes, now);
            if (parsed is null)
            {
                runner._logger.LogWarning("Usage scan document refused. Code: {Code} JobId: {JobId}.", refused, job.Id);
                return new UsageScanServerCompletion(server, UsageCollectionStatus.Malformed, null, null, [], refused, elapsed, now);
            }

            return new UsageScanServerCompletion(server, UsageCollectionStatus.Ok, parsed.Servers[0].Result, result.Document, parsed.Items, null, elapsed, now);
        }
    }
}

/// <summary>Re-checks, before any server is read, that the person who started a job is still approved and holds ServiceAccounts.Work.</summary>
public interface IUsageScanJobStarterCheck
{
    /// <summary>True when the starter may still run the scan; scope is re-checked again inside each evidence link.</summary>
    public Task<bool> IsStillAllowedAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Reads the persisted access record (no claims, no cache).</summary>
public sealed class AccessUsageScanJobStarterCheck(SecureOps.Infrastructure.Access.IAccessRepository access) : IUsageScanJobStarterCheck
{
    /// <inheritdoc />
    public async Task<bool> IsStillAllowedAsync(Guid userId, CancellationToken cancellationToken) =>
        await access.GetUserAsync(userId, cancellationToken) is { Status: SecureOps.Domain.Access.AccessStatus.Approved } user
        && user.Capabilities.Contains(SecureOps.Shared.Contracts.ServiceAccounts.ServiceAccountCapabilities.Work, StringComparer.Ordinal);
}
