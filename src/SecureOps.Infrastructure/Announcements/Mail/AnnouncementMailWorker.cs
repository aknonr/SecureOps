using Hangfire;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Domain.Commands;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Mail;

/// <summary>Enqueues only an already committed intent on the existing application queue.</summary>
public interface IAnnouncementMailDispatcher
{
    /// <summary>True when the existing durable queue is available by configuration.</summary>
    public bool IsConfigured { get; }
    /// <summary>A duplicate queue invocation cannot repeat the fenced SMTP command.</summary>
    public void Enqueue(Guid commandId);
}

/// <summary>Uses the API's existing Hangfire client; never sends in the request process.</summary>
public sealed class AnnouncementMailDispatcher(IServiceProvider services, IOptions<HangfireOptions> options) : IAnnouncementMailDispatcher
{
    /// <inheritdoc />
    public bool IsConfigured => options.Value.Enabled && services.GetService(typeof(IBackgroundJobClient)) is IBackgroundJobClient;
    /// <inheritdoc />
    public void Enqueue(Guid commandId)
    {
        if (!IsConfigured)
        { throw new InvalidOperationException("Mail Worker unavailable."); }
        ((IBackgroundJobClient)services.GetService(typeof(IBackgroundJobClient))!).Create<AnnouncementMailWorker>(
            runner => runner.RunAsync(commandId, CancellationToken.None), new Hangfire.States.EnqueuedState(options.Value.Queue));
    }
}

/// <summary>One claimed attempt, with persisted human intent and a separate technical executor.</summary>
public sealed class AnnouncementMailWorker(SqlAnnouncementMailStore store, AnnouncementMailPolicy policy,
    IAnnouncementMailTransport transport, IOptions<AnnouncementMailOptions> options)
{
    /// <summary>No Hangfire send retries. Expired dispatch becomes Unknown, never Queued.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid commandId, CancellationToken token)
    {
        OperationExecutor executor = new("SecureOps.Worker", Environment.MachineName + ":" + Environment.ProcessId, Environment.UserDomainName + "\\" + Environment.UserName);
        AnnouncementMailExecution? execution = await store.ClaimAsync(commandId, policy.Fingerprint(), executor, token);
        if (execution is null)
        { return; }
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 1, 120)));
        AnnouncementTransportOutcome outcome;
        try
        { outcome = await transport.SubmitAsync(execution, bounded.Token); }
        catch (Exception) { outcome = new("Unknown", [], []); }
        // Record an observed outcome even during host shutdown; failed persistence leaves a fenced lease.
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await store.CompleteAsync(execution, outcome, executor, completion.Token);
    }
}

/// <summary>Existing Hangfire recovery schedules only undispatched intents; it never retries an uncertain send.</summary>
public sealed class AnnouncementMailRecovery(SqlAnnouncementMailStore store, IAnnouncementMailDispatcher dispatcher,
    IOptions<AnnouncementMailOptions> options)
{
    /// <summary>Runs through the existing queue/foreground Worker, not a new scheduler.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken token)
    {
        if (!options.Value.Enabled)
        { return; }
        foreach (Guid commandId in await store.RecoverAsync(token))
        { dispatcher.Enqueue(commandId); }
    }
}
