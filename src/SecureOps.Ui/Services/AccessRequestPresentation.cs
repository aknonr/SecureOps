using System.Globalization;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Display rules for the access-request queue.
/// </summary>
/// <remarks>
/// Only server timestamps are formatted here. The waiting time is the distance from the request's
/// recorded <c>RequestedAt</c> to the moment the page renders; it is not an SLA, and the server's
/// ordering of the queue is reported as it is rather than re-sorted on one page.
/// </remarks>
public static class AccessRequestPresentation
{
    /// <summary>Rows per page requested from <c>GET /api/v1/access/requests/page</c>.</summary>
    public const int PageSize = 25;

    /// <summary>Days after which a still-pending request is labelled as long-waiting.</summary>
    public const int LongWaitDays = 7;

    private static readonly CultureInfo _tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Formats a server timestamp in the operator's local time.</summary>
    /// <param name="time">Server timestamp.</param>
    /// <returns><c>dd.MM.yyyy HH:mm</c>.</returns>
    public static string Time(DateTimeOffset time) => time.ToLocalTime().ToString("dd.MM.yyyy HH:mm", _tr);

    /// <summary>How long a pending request has waited, in whole days.</summary>
    /// <param name="requestedAt">The request's recorded creation time.</param>
    /// <param name="now">Render time.</param>
    /// <returns>Non-negative whole days.</returns>
    public static int WaitingDays(DateTimeOffset requestedAt, DateTimeOffset now) =>
        Math.Max(0, (int)Math.Floor((now - requestedAt).TotalDays));

    /// <summary>Short waiting-time text for a pending request.</summary>
    /// <param name="requestedAt">The request's recorded creation time.</param>
    /// <param name="now">Render time.</param>
    /// <returns>Turkish text, e.g. "3 gündür bekliyor".</returns>
    public static string WaitingText(DateTimeOffset requestedAt, DateTimeOffset now) =>
        WaitingDays(requestedAt, now) switch
        {
            0 => "Bugün geldi",
            int days => $"{days} gündür bekliyor"
        };

    /// <summary>Whether a pending request has waited at least <see cref="LongWaitDays"/>.</summary>
    /// <param name="request">Request to inspect.</param>
    /// <param name="now">Render time.</param>
    /// <returns><c>true</c> only for a pending request past the threshold.</returns>
    public static bool IsLongWaiting(AccessRequestResponse request, DateTimeOffset now) =>
        string.Equals(request.Status, AccessRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase)
        && WaitingDays(request.RequestedAt, now) >= LongWaitDays;

    /// <summary>Number of pages for a server total.</summary>
    /// <param name="total">Total rows reported with the page.</param>
    /// <returns>At least one.</returns>
    public static int PageCount(long total) => Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

    /// <summary>One secondary identity line: account, then e-mail, else nothing.</summary>
    /// <param name="profile">Nullable enrichment.</param>
    /// <returns>Secondary line, or <c>null</c>.</returns>
    public static string? SecondaryLine(AccessIdentityProfileResponse? profile) =>
        AccessIdentityDisplay.SecondaryAccount(profile)
        ?? (profile?.Email is { Length: > 0 } email ? email : null);
}
