using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Reminders;

/// <summary>
/// Evaluates open requests into a deduplicated outbox and delivers in-app reminders and coordinator drafts under a
/// SQL claim/lease. Outbound mail is not implemented. Runs from the existing Hangfire server (recurring job) or on
/// explicit administrator request; there is no in-process timer.
/// </summary>
public sealed class ServiceAccountReminderJob(SqlServiceAccountRepository? repository, IOptions<ServiceAccountOptions> options, TimeProvider clock,
    ILogger<ServiceAccountReminderJob> logger)
{
    private const int _claimBatch = 200;
    private static readonly TimeSpan[] _backoff = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(8)];

    /// <summary>Test hook: replaces local delivery to exercise retry and dead-letter handling.</summary>
    public Func<ReminderClaim, Task>? DeliverOverride { get; init; }

    /// <summary>Hangfire entry point; the outbox owns retries, so Hangfire does not re-run the whole job.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task RunScheduledAsync() => RunAsync(CancellationToken.None);

    /// <summary>Evaluate + enqueue + deliver once. Idempotent: re-running creates no duplicates.</summary>
    public async Task<ReminderRunResult> RunAsync(CancellationToken cancellationToken)
    {
        ServiceAccountOptions settings = options.Value;
        if (!settings.Enabled || repository is null)
        {
            return new ReminderRunResult(0, 0, 0, 0, 0, false);
        }

        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = ReportCalendar.LocalDate(now);
        ReminderSettings rules = new(settings.Reminders.PlanEndLeadDays, settings.Reminders.NoReplyAfterDays);
        IReadOnlyList<ReminderInput> open = await repository.ReminderInputsAsync(cancellationToken);
        List<(ReminderInput, DueReminder)> due = [.. open.SelectMany(r => ReminderRules.Evaluate(r, today, rules).Select(d => (r, d)))];
        int enqueued = await repository.EnqueueRemindersAsync(due, now, cancellationToken);
        string owner = Environment.MachineName + ":" + Guid.NewGuid().ToString("N")[..8];
        int delivered = 0, failed = 0, dead = 0;
        IReadOnlyList<ReminderClaim> claims = await repository.ClaimRemindersAsync(owner, _claimBatch,
            TimeSpan.FromSeconds(Math.Clamp(settings.Reminders.LeaseSeconds, 30, 3600)), now, cancellationToken);
        foreach (ReminderClaim claim in claims)
        {
            try
            {
                await (DeliverOverride?.Invoke(claim) ?? Deliver(claim));
                delivered += await repository.CompleteReminderAsync(claim.Id, owner, clock.GetUtcNow(), cancellationToken) ? 1 : 0;
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException or KeyNotFoundException)
            {
                logger.LogWarning("Service account reminder delivery failed. FailureType: {FailureType}", exception.GetType().Name);
                TimeSpan wait = _backoff[Math.Min(claim.AttemptCount, _backoff.Length - 1)];
                string? status = await repository.FailReminderAsync(claim.Id, owner, "Teslim edilemedi: " + exception.GetType().Name,
                    Math.Clamp(settings.Reminders.MaxAttempts, 1, 20), clock.GetUtcNow() + wait, cancellationToken);
                failed++;
                dead += status == "DeadLetter" ? 1 : 0;
            }
        }

        return new ReminderRunResult(open.Count, enqueued, delivered, failed, dead, !string.IsNullOrWhiteSpace(settings.Reminders.HolidayCalendar));
    }

    /// <summary>Local delivery: the stored payload is the in-app notification or the coordinator's draft text.</summary>
    private static Task Deliver(ReminderClaim claim)
    {
        using var payload = JsonDocument.Parse(claim.PayloadJson);
        if (string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("Message").GetString())
            || claim.Channel is not (nameof(ReminderChannel.InApp) or nameof(ReminderChannel.Draft)))
        {
            throw new InvalidOperationException("Reminder payload or channel is not deliverable.");
        }

        return Task.CompletedTask;
    }
}
