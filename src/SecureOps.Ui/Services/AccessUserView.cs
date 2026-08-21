using MudBlazor;
using SecureOps.Ui.Shared.Components;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Presentation rules for an administrative access-user record.
/// </summary>
/// <remarks>
/// Shared by the user list and the user detail so the two cannot describe the same record
/// differently — a list saying "Onay bekliyor" next to a detail saying "Reddedildi" would be worse
/// than either alone.
/// <para>
/// The distinction this type exists for: <b>a refused user keeps <c>AccessStatus.Pending</c></b>.
/// The API has no Rejected user status, and after a rejection it deliberately creates no replacement
/// request, so the account sits at Pending indefinitely with <c>latestRequest.status</c> as the only
/// evidence it was refused. Reading status alone therefore shows a closed case as an open one, and
/// an administrator working the queue would re-approve someone a colleague turned down.
/// </para>
/// </remarks>
public static class AccessUserView
{
    /// <summary>Whether the user is approved for application use.</summary>
    /// <param name="user">Administrative user record.</param>
    /// <returns><c>true</c> when approved.</returns>
    public static bool IsApproved(AccessUserResponse user) =>
        string.Equals(user.AccessStatus, AccessStatuses.Approved, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the user's access has been switched off.</summary>
    /// <param name="user">Administrative user record.</param>
    /// <returns><c>true</c> when disabled.</returns>
    public static bool IsDisabled(AccessUserResponse user) =>
        string.Equals(user.AccessStatus, AccessStatuses.Disabled, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the user's most recent request was refused.
    /// </summary>
    /// <param name="user">Administrative user record.</param>
    /// <returns><c>true</c> when pending status masks a rejected request.</returns>
    public static bool IsRejected(AccessUserResponse user) =>
        !IsApproved(user)
        && !IsDisabled(user)
        && string.Equals(
            user.LatestRequest?.Status,
            AccessRequestStatuses.Rejected,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a decision is genuinely outstanding for this user.
    /// </summary>
    /// <param name="user">Administrative user record.</param>
    /// <returns><c>true</c> when a pending request is waiting on an administrator.</returns>
    public static bool IsAwaitingDecision(AccessUserResponse user) =>
        !IsApproved(user) && !IsDisabled(user) && !IsRejected(user);

    /// <summary>
    /// Operator-facing status label.
    /// </summary>
    /// <param name="user">Administrative user record.</param>
    /// <returns>Turkish label reflecting the combined user and request state.</returns>
    public static string StatusLabel(AccessUserResponse user)
    {
        if (IsDisabled(user))
        {
            return "Kapatıldı";
        }

        if (IsApproved(user))
        {
            return "Onaylı";
        }

        return IsRejected(user) ? "Reddedildi" : "Onay bekliyor";
    }

    /// <summary>
    /// Badge tone for the combined state.
    /// </summary>
    /// <param name="user">Administrative user record.</param>
    /// <returns>Tone matching the label.</returns>
    /// <remarks>
    /// A rejection is Critical rather than Caution: it is a closed decision, not an open task, and
    /// colouring it like the pending queue is precisely the confusion this type exists to prevent.
    /// </remarks>
    public static SoStatusBadge.BadgeTone StatusTone(AccessUserResponse user)
    {
        if (IsDisabled(user))
        {
            return SoStatusBadge.BadgeTone.Critical;
        }

        if (IsApproved(user))
        {
            return SoStatusBadge.BadgeTone.Positive;
        }

        return IsRejected(user)
            ? SoStatusBadge.BadgeTone.Critical
            : SoStatusBadge.BadgeTone.Caution;
    }

    /// <summary>
    /// Icon reinforcing the status, so state is not carried by colour alone.
    /// </summary>
    /// <param name="user">Administrative user record.</param>
    /// <returns>Material icon name.</returns>
    public static string StatusIcon(AccessUserResponse user)
    {
        if (IsDisabled(user))
        {
            return Icons.Material.Filled.Block;
        }

        if (IsApproved(user))
        {
            return Icons.Material.Filled.CheckCircle;
        }

        return IsRejected(user)
            ? Icons.Material.Filled.Cancel
            : Icons.Material.Filled.HourglassTop;
    }
}
