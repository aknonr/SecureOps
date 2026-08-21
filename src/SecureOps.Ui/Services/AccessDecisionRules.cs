using SecureOps.Infrastructure.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client-side mirror of the server's access-decision validation rules.
/// </summary>
/// <remarks>
/// <para>
/// Client-side validation here is a <b>courtesy, not a safety mechanism</b>. It saves a round trip
/// and puts the message next to the field, and that is all it is for.
/// </para>
/// <para>
/// It used to be load-bearing. Before backend commit <c>78183dd</c> the API answered a blank reason,
/// an empty role set, and "another administrator already decided this" with one shared
/// <c>AccessRequestInvalidState</c> code, so the UI could only treat a 409 as a conflict by first
/// making sure no input case could produce one. That was a workaround (G-9). The API now returns
/// <c>AccessValidationFailed</c>, <c>AccessRequestAlreadyDecided</c>,
/// <c>AccessConcurrencyConflict</c>, and <c>AccessUserInvalidState</c> as distinct codes, so the
/// inference is gone and these rules no longer carry the distinction.
/// </para>
/// <para>
/// As with <see cref="AccountInputRules"/>, this is never stricter than the server: if the server
/// tightens a rule, the server rejects and its message is shown.
/// </para>
/// </remarks>
public static class AccessDecisionRules
{
    /// <summary>Maximum reason length the API accepts, from <c>ApplicationAccessService.ValidReason</c>.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Maximum number of roles the API accepts in one assignment.</summary>
    public const int MaxRoles = 16;

    /// <summary>
    /// Role codes the API will accept, read from the reviewed server-side catalog.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="AccessRoleCatalog"/> rather than duplicated, so a role added or retired
    /// on the server cannot leave the picker offering something the API rejects. Only the role
    /// <i>codes</i> come from here — capabilities are never derived in the UI. What a role grants is
    /// shown as prose guidance, and the effective capability list always comes from the API's
    /// response to a real call.
    /// </remarks>
    public static IReadOnlyList<string> AssignableRoles { get; } = AccessRoleCatalog.RoleCodes
        .OrderBy(role => role, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Validates a justification.
    /// </summary>
    /// <param name="reason">Reason entered by the administrator.</param>
    /// <returns>An operator-facing message, or <c>null</c> when acceptable.</returns>
    public static string? ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Gerekçe zorunludur. Bu bilgi denetim kaydına işlenir.";
        }

        return reason.Trim().Length > MaxReasonLength
            ? $"Gerekçe en fazla {MaxReasonLength} karakter olabilir."
            : null;
    }

    /// <summary>
    /// Validates a role set for approval or role replacement.
    /// </summary>
    /// <param name="roles">Selected role codes.</param>
    /// <returns>An operator-facing message, or <c>null</c> when acceptable.</returns>
    public static string? ValidateRoles(IReadOnlyCollection<string>? roles)
    {
        if (roles is null || roles.Count == 0)
        {
            return "En az bir rol seçilmelidir.";
        }

        if (roles.Count > MaxRoles)
        {
            return $"En fazla {MaxRoles} rol atanabilir.";
        }

        string[] unknown = roles
            .Where(role => !AccessRoleCatalog.IsKnownRole(role))
            .ToArray();

        return unknown.Length == 0
            ? null
            : $"Tanınmayan rol: {string.Join(", ", unknown)}.";
    }
}
