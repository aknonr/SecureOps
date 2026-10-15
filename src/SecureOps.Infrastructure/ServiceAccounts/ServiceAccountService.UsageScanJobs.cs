using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Web usage scan services the module service needs (null store when the module is disabled).</summary>
/// <param name="Store">Job persistence.</param>
/// <param name="Availability">Configuration-derived availability.</param>
/// <param name="Dispatcher">Queue hand-over.</param>
public sealed record UsageScanJobServices(IUsageScanJobStore? Store, UsageScanJobAvailability Availability, IUsageScanJobDispatcher Dispatcher);

public sealed partial class ServiceAccountService
{
    /// <summary>
    /// Whether a web usage scan can start here (ADR-0024 R2-4, USAGE-SCAN-JOB-CONTRACT.md). Fail closed: without a configured collector,
    /// an allowed environment for a fake, a queue and migration 037 the answer is "not available" with an honest Turkish reason.
    /// </summary>
    public Task<SaResult<UsageScanJobAvailabilityView>> UsageScanJobAvailabilityAsync(ClaimsPrincipal principal, AccessOperationContext context,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            string? reason = await JobsUnavailableReasonAsync(cancellationToken);
            string collector = jobs?.Availability.Collector ?? "None";
            return new SaResult<UsageScanJobAvailabilityView>(new UsageScanJobAvailabilityView(reason is null, reason, JobsReasonText(reason), collector,
                collector == "Fake", UsageScanJobRules.MaxAccounts, UsageScanJobRules.MaxServers, caller.Can(ServiceAccountCapabilities.Work)));
        }, cancellationToken);

    /// <summary>
    /// Starts a web usage scan: Work plus the responsible basis (organization scope or owner team) on every account, 1–20 accounts and
    /// 1–500 planned servers, at most one queued or running scan per account. Out of scope is indistinguishable from missing (404). The
    /// job is stored first and then handed to the existing Hangfire queue; nothing here contacts a server.
    /// </summary>
    public Task<SaResult<UsageScanJobView>> StartUsageScanJobAsync(ClaimsPrincipal principal, AccessOperationContext context, StartUsageScanJobRequest? request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, caller =>
            StartJobAsync(caller, request?.AccountIds, request?.Servers, request?.ExpectedAccount, null, context, cancellationToken), cancellationToken);

    /// <summary>
    /// Live state of a scan, visible to a caller whose scope covers every account of it. <paramref name="after"/> is the last finish sequence
    /// the caller already has, so only newly found items come back. A job whose Worker lease expired is closed here as <c>workerLost</c>.
    /// </summary>
    public Task<SaResult<UsageScanJobView>> UsageScanJobAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid jobId, int after,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            if (after < 0)
            {
                return SaResult<UsageScanJobView>.Fail(SaErrors.Invalid, "after");
            }

            (UsageScanJobRecord? job, SaResult<UsageScanJobView>? refused) = await VisibleJobAsync(caller, jobId, cancellationToken);
            return refused ?? await JobViewAsync(caller, job!, after, cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// Asks a running scan to stop (the starter with Work, or an administrator whose scope covers its accounts). Servers that finished stay
    /// as evidence; the others are recorded as "çalışmadı". A queued scan is closed at once.
    /// </summary>
    public Task<SaResult<UsageScanJobView>> CancelUsageScanJobAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid jobId,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            (UsageScanJobRecord? job, SaResult<UsageScanJobView>? refused) = await VisibleJobAsync(caller, jobId, cancellationToken);
            if (refused is not null)
            {
                return refused;
            }

            if (!CanCancel(caller, job!))
            {
                return SaResult<UsageScanJobView>.Fail(SaErrors.Forbidden, "cancel");
            }

            if (!await jobs!.Store!.RequestCancelAsync(jobId, caller.Actor, clock.GetUtcNow(), cancellationToken))
            {
                return SaResult<UsageScanJobView>.Fail(SaErrors.UsageScanJobState, "state");
            }

            return await JobViewAsync(caller, (await jobs.Store.GetAsync(jobId, cancellationToken))!, 0, cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// Retries a finished scan with only the servers that were not read completely (any status but Ok, or a Partial/Failed document), as a
    /// new scan under the same rules as a start. Complete reads are never repeated by a retry.
    /// </summary>
    public Task<SaResult<UsageScanJobView>> RetryUsageScanJobAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid jobId,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            (UsageScanJobRecord? job, SaResult<UsageScanJobView>? refused) = await VisibleJobAsync(caller, jobId, cancellationToken);
            if (refused is not null)
            {
                return refused;
            }

            if (UsageScanJobRules.IsActive(job!.State))
            {
                return SaResult<UsageScanJobView>.Fail(SaErrors.UsageScanJobState, "state");
            }

            IReadOnlyList<string> servers = UsageScanJobViews.RetryServers(job);
            return servers.Count == 0
                ? SaResult<UsageScanJobView>.Fail(SaErrors.Invalid, "nothingToRetry")
                : await StartJobAsync(caller, [.. job.Accounts.Select(a => a.AccountId)], servers, job.ExpectedAccount, job.Id, context, cancellationToken);
        }, cancellationToken);

    /// <summary>The newest web scans of one account (same scope rule as the account detail).</summary>
    public Task<SaResult<IReadOnlyList<UsageScanJobSummaryView>>> AccountUsageScanJobsAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid accountId, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            if (!await AccountVisibleAsync(caller, accountId, cancellationToken))
            {
                return SaResult<IReadOnlyList<UsageScanJobSummaryView>>.Fail(SaErrors.NotFound);
            }

            if (jobs?.Store is not { } store || !await store.IsInstalledAsync(cancellationToken))
            {
                return SaResult<IReadOnlyList<UsageScanJobSummaryView>>.Fail(SaErrors.UsageScanJobsNotInstalled);
            }

            IReadOnlyList<UsageScanJobRecord> list = await store.ForAccountAsync(accountId, 5, cancellationToken);
            if (await ExpireStaleAsync([.. list.Where(j => UsageScanJobRules.IsActive(j.State))], cancellationToken))
            {
                list = await store.ForAccountAsync(accountId, 5, cancellationToken);
            }

            return new SaResult<IReadOnlyList<UsageScanJobSummaryView>>([.. list.Select(j => UsageScanJobViews.Summary(j, caller.User.Id))]);
        }, cancellationToken);

    private async Task<SaResult<UsageScanJobView>> StartJobAsync(SaCaller caller, IReadOnlyList<Guid>? accountIds, IReadOnlyList<string>? serverLines,
        string? expectedAccount, Guid? retryOf, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (jobs?.Availability.Reason is { } off)
        {
            return SaResult<UsageScanJobView>.Fail(SaErrors.UsageScanJobsUnavailable, off);
        }

        Guid[] ids = [.. accountIds ?? []];
        if (ids.Length is 0 or > UsageScanJobRules.MaxAccounts || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length)
        {
            return SaResult<UsageScanJobView>.Fail(SaErrors.Invalid, "accountIds");
        }

        (IReadOnlyList<string> servers, string? serverError, int? line) = UsageScanJobRules.ParseServers(serverLines);
        if (serverError is not null)
        {
            return SaResult<UsageScanJobView>.Fail(SaErrors.Invalid, serverError, line is null ? null : new { line });
        }

        string? expected = string.IsNullOrWhiteSpace(expectedAccount) ? null : expectedAccount.Trim();
        if (expected is not null && (!UsageScanJobRules.IsAccountName(expected) || !expected.EndsWith('$')))
        {
            return SaResult<UsageScanJobView>.Fail(SaErrors.Invalid, "expectedAccount");
        }

        IUsageScanJobStore store = jobs!.Store!;
        if (!await store.IsInstalledAsync(cancellationToken))
        {
            return SaResult<UsageScanJobView>.Fail(SaErrors.UsageScanJobsNotInstalled);
        }

        List<UsageScanJobAccountInput> accounts = [];
        foreach (Guid id in ids)
        {
            (AccountScopeAnchor Anchor, string Version, string Name, string? Domain)? read = await repository!.AnchorAsync(id, cancellationToken);
            if (read is not { } account || !caller.Scope.Covers(account.Anchor))
            {
                return SaResult<UsageScanJobView>.Fail(SaErrors.NotFound, "accountIds");
            }

            if (!Responsible(caller.Scope, account.Anchor))
            {
                return SaResult<UsageScanJobView>.Fail(SaErrors.Forbidden, "accountIds");
            }

            string? searched = UsageScanJobRules.SearchName(account.Name, account.Domain);
            if (searched is null)
            {
                return SaResult<UsageScanJobView>.Fail(SaErrors.Invalid, "accountName", new { accountId = id });
            }

            accounts.Add(new UsageScanJobAccountInput(id, searched));
        }

        HashSet<string> names = new(accounts.Select(a => a.SearchedName), StringComparer.OrdinalIgnoreCase);
        if (names.Count != accounts.Count || expected is not null && names.Contains(expected))
        {
            return SaResult<UsageScanJobView>.Fail(SaErrors.Invalid, names.Count != accounts.Count ? "accountIds" : "expectedAccount");
        }

        // A scan whose Worker vanished must not hold its accounts forever; it is closed honestly before the new one is created.
        await ExpireStaleAsync(await store.ActiveJobsAsync(ids, cancellationToken), cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        NewUsageScanJob job = new(Guid.NewGuid(), expected is null ? "Discovery" : "GmsaCheck", expected, accounts, servers,
            PlanSha256(accounts.Select(a => a.SearchedName), expected, servers), jobs.Availability.Collector, retryOf, caller.User.Id, now);
        if (await store.CreateAsync(job, caller.Actor, cancellationToken) is { } busy)
        {
            return SaResult<UsageScanJobView>.Fail(SaErrors.UsageScanAlreadyRunning, "accountIds", new { accountId = busy });
        }

        try
        {
            jobs.Dispatcher.Enqueue(job.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Usage scan job could not be queued. FailureType: {FailureType} CorrelationId={CorrelationId}", exception.GetType().Name,
                context.CorrelationId);
            await store.FinishAsync(job.Id, null, new UsageScanJobFinish(UsageScanJobState.Failed, "dispatchFailed", null, new Dictionary<Guid, string>(),
                clock.GetUtcNow()), caller.Actor, cancellationToken);
            return SaResult<UsageScanJobView>.Fail(SaErrors.UsageScanJobsUnavailable, "queueNotConfigured");
        }

        return await JobViewAsync(caller, (await store.GetAsync(job.Id, cancellationToken))!, 0, cancellationToken);
    }

    private async Task<(UsageScanJobRecord? Job, SaResult<UsageScanJobView>? Refused)> VisibleJobAsync(SaCaller caller, Guid jobId,
        CancellationToken cancellationToken)
    {
        if (jobs?.Store is not { } store || !await store.IsInstalledAsync(cancellationToken))
        {
            return (null, SaResult<UsageScanJobView>.Fail(SaErrors.UsageScanJobsNotInstalled));
        }

        UsageScanJobRecord? job = await store.GetAsync(jobId, cancellationToken);
        if (job is null || !(await CoversAllAsync(caller, job, cancellationToken)).Visible)
        {
            return (null, SaResult<UsageScanJobView>.Fail(SaErrors.NotFound));
        }

        if (UsageScanJobRules.IsActive(job.State) && await ExpireStaleAsync([job], cancellationToken))
        {
            job = (await store.GetAsync(jobId, cancellationToken))!;
        }

        return (job, null);
    }

    private async Task<SaResult<UsageScanJobView>> JobViewAsync(SaCaller caller, UsageScanJobRecord job, int after, CancellationToken cancellationToken)
    {
        IReadOnlyList<UsageScanJobFoundItem> items = await jobs!.Store!.ItemsAfterAsync(job.Id, after, UsageScanJobViews.MaxItemsPerPoll, cancellationToken);
        int last = items.Count == 0 ? after : items.Max(i => i.FinishSeq);
        bool more = job.Servers.Any(s => s.FinishSeq > last && (s.FormerMatches ?? 0) + (s.ExpectedMatches ?? 0) > 0);
        bool canRetry = caller.Can(ServiceAccountCapabilities.Work) && !UsageScanJobRules.IsActive(job.State)
            && (await CoversAllAsync(caller, job, cancellationToken)).Responsible;
        return UsageScanJobViews.View(job, items, after, more, clock.GetUtcNow(), caller.User.Id, CanCancel(caller, job), canRetry);
    }

    private static bool CanCancel(SaCaller caller, UsageScanJobRecord job) =>
        job.StartedBy == caller.User.Id && caller.Can(ServiceAccountCapabilities.Work) || caller.Can(ServiceAccountCapabilities.Administer);

    /// <summary>Whether the caller's scope covers every account of the job, and whether it is responsible for every one (one read per account).</summary>
    private async Task<(bool Visible, bool Responsible)> CoversAllAsync(SaCaller caller, UsageScanJobRecord job, CancellationToken cancellationToken)
    {
        bool responsible = true;
        foreach (UsageScanJobAccount account in job.Accounts)
        {
            if (await repository!.AnchorAsync(account.AccountId, cancellationToken) is not { } read || !caller.Scope.Covers(read.Anchor))
            {
                return (false, false);
            }

            responsible &= Responsible(caller.Scope, read.Anchor);
        }

        return job.Accounts.Count > 0 ? (true, responsible) : (false, false);
    }

    /// <summary>Closes active jobs whose Worker lease expired or that nobody claimed in time; true when any was closed.</summary>
    private async Task<bool> ExpireStaleAsync(IReadOnlyCollection<UsageScanJobRecord> active, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset queuedBefore = now - TimeSpan.FromMinutes(Math.Clamp(_options.UsageScanJobs.QueueTimeoutMinutes, 1, 1440));
        Guid[] stale = [.. active.Where(j => j.State == UsageScanJobState.Running && j.LeaseUntil < now
            || j.State == UsageScanJobState.Queued && j.CreatedAt < queuedBefore).Select(j => j.Id)];
        return stale.Length > 0 && await jobs!.Store!.ExpireAsync(stale, now, queuedBefore, cancellationToken) > 0;
    }

    private async Task ExpireStaleAsync(IReadOnlyList<Guid> activeIds, CancellationToken cancellationToken)
    {
        List<UsageScanJobRecord> active = [];
        foreach (Guid id in activeIds)
        {
            if (await jobs!.Store!.GetAsync(id, cancellationToken) is { } job)
            {
                active.Add(job);
            }
        }

        await ExpireStaleAsync(active, cancellationToken);
    }

    private async Task<string?> JobsUnavailableReasonAsync(CancellationToken cancellationToken) =>
        jobs is null ? "collectorNotConfigured"
        : jobs.Availability.Reason is { } reason ? reason
        : jobs.Store is null || !await jobs.Store.IsInstalledAsync(cancellationToken) ? "notInstalled"
        : null;

    private static string JobsReasonText(string? reason) =>
        reason == "notInstalled" ? "Web taraması tabloları (037) bu ortamda kurulu değil; hiçbir sunucuya bağlanılmaz." : UsageScanJobAvailability.ReasonText(reason);

    /// <summary>SHA-256 of the canonical plan (searched names, expected gMSA, servers in order) for the audit trail (R2-5).</summary>
    private static string PlanSha256(IEnumerable<string> accounts, string? expected, IReadOnlyList<string> servers) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { accounts, expected, servers })))).ToLowerInvariant();
}
