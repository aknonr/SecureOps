using SecureOps.Infrastructure.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client-side mirror of the server's access-decision validation rules.
/// </summary>
/// <remarks>
/// <para>
/// This exists for a specific reason. The API returns <c>AccessRequestInvalidState</c> (409) for
/// several unrelated situations: a blank reason, an empty role set, an unknown role code, a request
/// another administrator has already decided, disabling an already-disabled user, and assigning
/// roles to a disabled user. The first three are the operator mistyping something; the rest are the
/// world having moved on. They need opposite responses — fix the form, versus reload and look again
/// — and the response body cannot tell them apart. Tracked as G-9 in
/// <c>docs/26-ui-backend-contract-gaps.md</c>.
/// </para>
/// <para>
/// Validating here removes the input cases before a request is ever sent, which leaves a 409 from
/// the server meaning, in practice, a genuine state conflict. That is what lets the conflict UX say
/// "someone else changed this" without risking that it was really a typo. If the API later splits
/// the code, this can relax to a courtesy check.
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
