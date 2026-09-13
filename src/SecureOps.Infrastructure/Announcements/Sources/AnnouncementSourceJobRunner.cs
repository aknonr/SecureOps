using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>Hands an accepted job to the established Hangfire queue. No second queue is introduced.</summary>
public interface IAnnouncementSourceDispatcher
{
    /// <summary>Enqueues durable execution for an already-persisted Queued job.</summary>
    public void Enqueue(Guid jobId);
}

/// <summary>Hangfire client dispatch. The API only enqueues; the Worker executes.</summary>
public sealed class HangfireAnnouncementSourceDispatcher(IBackgroundJobClient client, string queue)
    : IAnnouncementSourceDispatcher
{
    /// <inheritdoc />
    public void Enqueue(Guid jobId) =>
        client.Create<AnnouncementSourceJobRunner>(runner => runner.RunAsync(jobId, CancellationToken.None),
            new Hangfire.States.EnqueuedState(queue));
}

/// <summary>Refuses dispatch when no job host is configured, instead of silently accepting work.</summary>
public sealed class UnavailableAnnouncementSourceDispatcher : IAnnouncementSourceDispatcher
{
    /// <inheritdoc />
    public void Enqueue(Guid jobId) => throw new AnnouncementSourceException("AnnouncementSourceJobHostUnavailable", false);
}

/// <summary>
/// The durable source job body. It claims the persisted Queued row, collects evidence and writes exactly
/// one terminal outcome back to SQL. Because both the claim and the outcome are persisted, a process
/// restart mid-run leaves a Running row that a redelivery cannot double-complete, and a completed
/// outcome survives restart. Authorization was checked at submission and is rechecked here against the
/// stored owner before any source call, so a revoked owner cannot have work executed on their behalf.
/// </summary>
public sealed class AnnouncementSourceJobRunner(
    SqlAnnouncementSourceStore store,
    MaintenanceProfileCatalog profiles,
    AnnouncementSourceCollector collector,
    IAnnouncementSourceAuthorizationRecheck recheck,
    TimeProvider time,
    ILogger<AnnouncementSourceJobRunner> logger)
{
    /// <summary>Executes one job. Hangfire retries are disabled: a source read is not blindly repeated.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DateTimeOffset started = time.GetUtcNow();
        AnnouncementSourceJob? job = await store.ClaimAsync(jobId, started, cancellationToken);
        if (job is null)
        {
            // Already claimed or already terminal: never re-run and never overwrite a recorded outcome.
            logger.LogInformation("Announcement source job was not claimable. JobId: {JobId}.", jobId);
            return;
        }
        string correlation = "announcement-source:" + jobId.ToString("N");
        try
        {
            if (!await recheck.IsStillAuthorizedAsync(job.OwnerId, cancellationToken))
            {
                await store.CompleteAsync(jobId, AnnouncementSourceJobStates.Failed, "AccessDenied", null,
                    correlation, time.GetUtcNow(), cancellationToken);
                return;
            }
            Shared.Configuration.MaintenanceProfileOptions? profile = profiles.Configured(job.Profile);
            if (profile is null)
            {
                await store.CompleteAsync(jobId, AnnouncementSourceJobStates.Failed,
                    "AnnouncementSourceProfileUnavailable", null, correlation, time.GetUtcNow(), cancellationToken);
                return;
            }
            AnnouncementSourceSnapshot snapshot = await collector.CollectAsync(jobId, job.DraftId, job.OwnerId,
                job.Profile, profile.CollectionId, job.OcoReference, cancellationToken);
            await store.CompleteAsync(jobId,
                snapshot.Completeness.Partial ? AnnouncementSourceJobStates.Partial : AnnouncementSourceJobStates.Succeeded,
                null, snapshot, correlation, time.GetUtcNow(), cancellationToken);
        }
        catch (AnnouncementSourceException exception)
        {
            logger.LogWarning("Announcement source job failed safely. JobId: {JobId}. ErrorCode: {ErrorCode}.", jobId, exception.ErrorCode);
            await store.CompleteAsync(jobId, AnnouncementSourceJobStates.Failed, exception.ErrorCode, null,
                correlation, time.GetUtcNow(), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // A cancelled or timed-out read produced no trustworthy evidence; the outcome is recorded, not lost.
            await store.CompleteAsync(jobId, AnnouncementSourceJobStates.Failed, "AnnouncementSourceTimeout", null,
                correlation, time.GetUtcNow(), CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Announcement source job faulted. JobId: {JobId}. FailureType: {FailureType}.", jobId, exception.GetType().Name);
            await store.CompleteAsync(jobId, AnnouncementSourceJobStates.Failed, "AnnouncementSourceUnavailable", null,
                correlation, time.GetUtcNow(), CancellationToken.None);
            throw;
        }
    }
}

/// <summary>Re-validates the submitting owner's persisted access at execution time.</summary>
public interface IAnnouncementSourceAuthorizationRecheck
{
    /// <summary>True only when the stored owner is still approved and still holds the draft capability.</summary>
    public Task<bool> IsStillAuthorizedAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>
/// Re-reads the submitting owner's persisted access when the job actually executes. A job accepted
/// while the owner was approved must not run after that approval or capability was removed, and a
/// module switched off after submission must not still reach a corporate source.
/// </summary>
public sealed class AnnouncementSourceAuthorizationRecheck(IAccessRepository access,
    IOptions<AnnouncementSourceOptions> source, IOptions<AnnouncementOptions> announcements,
    ILogger<AnnouncementSourceAuthorizationRecheck> logger) : IAnnouncementSourceAuthorizationRecheck
{
    /// <inheritdoc />
    public async Task<bool> IsStillAuthorizedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (!announcements.Value.Enabled || !source.Value.Enabled)
        {
            logger.LogWarning("Announcement source job stopped: module disabled after submission.");
            return false;
        }
        ApplicationUser? user = await access.GetUserAsync(ownerId, cancellationToken);
        bool authorized = user is { Status: AccessStatus.Approved }
            && user.Capabilities.Contains(Capabilities.AnnouncementDrafts);
        if (!authorized)
        { logger.LogWarning("Announcement source job stopped: owner access is no longer valid."); }
        return authorized;
    }
}
