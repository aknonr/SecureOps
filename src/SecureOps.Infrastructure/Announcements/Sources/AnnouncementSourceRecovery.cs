using Hangfire;
using Microsoft.Extensions.Logging;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>Replays due SQL intents through the same Hangfire queue, with at-least-once delivery.</summary>
public sealed class AnnouncementSourceRecovery(SqlAnnouncementSourceStore store,
    IAnnouncementSourceDispatcher dispatcher, ILogger<AnnouncementSourceRecovery> logger)
{
    /// <summary>Disabled composition cannot accept new work; transient enqueue outages remain recoverable.</summary>
    public bool IsConfigured => dispatcher.IsConfigured;
    /// <summary>One bounded recovery pass, run by Hangfire on startup and every minute.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken token)
    {
        foreach (Guid jobId in await store.DueAsync(token))
        { await DispatchAsync(jobId, token); }
    }

    /// <summary>SQL intent remains recoverable after any enqueue or acknowledgement failure.</summary>
    public async Task DispatchAsync(Guid jobId, CancellationToken token)
    {
        try
        {
            DateTimeOffset? reservation = await store.ReserveDispatchAsync(jobId, token);
            if (reservation is null)
            { return; }
            string queuedId = dispatcher.Enqueue(jobId);
            await store.AcknowledgeDispatchAsync(jobId, reservation.Value, queuedId, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Announcement dispatch awaits recovery. JobId: {JobId}. FailureType: {FailureType}.",
                jobId, exception.GetType().Name);
        }
    }
}
