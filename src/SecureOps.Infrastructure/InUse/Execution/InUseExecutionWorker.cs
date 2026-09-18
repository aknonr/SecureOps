using Hangfire;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>One bounded run on the existing Hangfire host; step leases, not queue retries, govern external effects.</summary>
public sealed class InUseExecutionWorker(IInUseExecutionStore store, IInUseCompletionTransport transport,
    InUseCompletionPolicy policy, IOptions<InUseCompletionOptions> options)
{
    /// <summary>Readback steps are separate from each acknowledged mutation.</summary>
    public static IReadOnlyList<string> Steps { get; } = ["Validate", "Property4463", "Property4464", "Upload", "Attachment", "Bpm", "Closure"];

    /// <summary>Lease expiry cannot authorize repetition of a potentially accepted remote mutation.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid id, CancellationToken token)
    {
        string executor = "SecureOps.Worker:" + Environment.MachineName + ":" + Environment.ProcessId;
        for (int count = 0; count < Steps.Count && !token.IsCancellationRequested; count++)
        {
            InUseExecutionLease? lease = await store.ClaimAsync(id, policy.Fingerprint, executor, token);
            if (lease is null)
            { return; }
            InUseRemoteResult outcome;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 1, 120)));
            try
            {
                outcome = policy.Readiness(lease.Intent.Source).Available
                    ? await transport.ExecuteAsync(Steps[lease.Step], lease, timeout.Token)
                    : new("Rejected", "WritePolicyUnavailable");
            }
            catch (Exception)
            { outcome = new("Unknown", "ResponseUnavailableReconcileBeforeRetry"); }
            using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (!await store.CompleteAsync(lease, outcome, executor, completion.Token)
                || outcome.Outcome is "Unknown" or "Rejected" or "Unconfirmed")
            { return; }
        }
    }
}

/// <summary>Uses the existing configured queue; the durable SQL row is authoritative even if enqueue fails.</summary>
public sealed class InUseExecutionDispatcher(IServiceProvider services, IOptions<HangfireOptions> options)
{
    /// <summary>Configured queue availability, not a live-worker heartbeat assertion.</summary>
    public bool Available => options.Value.Enabled && services.GetService(typeof(IBackgroundJobClient)) is IBackgroundJobClient;
    /// <summary>Enqueues an already committed operation only.</summary>
    public void Enqueue(Guid id)
    {
        if (!Available)
        { throw new InvalidOperationException("In Use Worker queue is unavailable."); }
        ((IBackgroundJobClient)services.GetService(typeof(IBackgroundJobClient))!).Create<InUseExecutionWorker>(
            job => job.RunAsync(id, CancellationToken.None), new Hangfire.States.EnqueuedState(options.Value.Queue));
    }
}

/// <summary>Startup/minutely outbox recovery on the existing foreground Worker.</summary>
public sealed class InUseExecutionRecovery(SqlInUseExecutionStore store, InUseExecutionDispatcher dispatcher, IOptions<InUseCompletionOptions> options)
{
    /// <summary>Disabled deployments never inspect or activate historical intents.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken token)
    {
        if (!options.Value.Enabled || !dispatcher.Available)
        { return; }
        foreach (Guid id in await store.RecoverAsync(token))
        { dispatcher.Enqueue(id); }
    }
}
