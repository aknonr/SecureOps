using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts.Reminders;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    private static readonly string[] _reminderStatuses = ["Pending", "Delivered", "Failed", "DeadLetter", "Dismissed"];

    /// <summary>In-app reminders, coordinator drafts and delivery failures within the caller's scope.</summary>
    public Task<SaResult<IReadOnlyList<ReminderView>>> RemindersAsync(ClaimsPrincipal principal, AccessOperationContext context, string? status, bool myTeam,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            if (status is not null && !_reminderStatuses.Contains(status, StringComparer.Ordinal))
            {
                return SaResult<IReadOnlyList<ReminderView>>.Fail(SaErrors.Invalid, "status");
            }

            return new SaResult<IReadOnlyList<ReminderView>>(await repository!.RemindersAsync(caller.Scope, status, myTeam, cancellationToken));
        }, cancellationToken);

    /// <summary>Dismisses a delivered or dead-lettered reminder on an account the caller can work.</summary>
    public Task<SaResult<Guid>> DismissReminderAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, DismissReminderRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (await repository!.ReminderAccountAsync(id, cancellationToken) is not { } accountId
                || await repository.AnchorAsync(accountId, cancellationToken) is not { } anchor || !caller.Scope.Covers(anchor.Anchor))
            {
                return SaResult<Guid>.Fail(SaErrors.NotFound);
            }

            return string.IsNullOrWhiteSpace(request.ExpectedVersion)
                ? SaResult<Guid>.Fail(SaErrors.Invalid, "expectedVersion")
                : await repository.DismissReminderAsync(id, accountId, request.ExpectedVersion, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Administrator-triggered evaluation; the same idempotent run as the scheduled job.</summary>
    public Task<SaResult<ReminderRunResult>> RunRemindersAsync(ClaimsPrincipal principal, AccessOperationContext context, ServiceAccountReminderJob job,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, async _ => new SaResult<ReminderRunResult>(await job.RunAsync(cancellationToken)),
            cancellationToken);
}
