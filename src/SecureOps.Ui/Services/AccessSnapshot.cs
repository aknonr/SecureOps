using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Application access status values returned by <c>GET /api/v1/access/me</c>.
/// </summary>
/// <remarks>
/// The API serializes <c>ApplicationUser.Status</c> as a string. These constants exist so the UI
/// compares against a named value instead of scattering string literals across components.
/// </remarks>
public static class AccessStatuses
{
    /// <summary>Authenticated, awaiting approval.</summary>
    public const string Pending = "Pending";

    /// <summary>Approved for application use.</summary>
    public const string Approved = "Approved";

    /// <summary>Access explicitly disabled.</summary>
    public const string Disabled = "Disabled";
}

/// <summary>
/// The result of resolving the caller's application access, including the failure case.
/// </summary>
/// <remarks>
/// A snapshot always carries either <see cref="Access"/> or <see cref="Problem"/>. Components render
/// from whichever is present, which keeps "access could not be determined" a first-class UI state
/// rather than an exception that escapes into the circuit.
/// </remarks>
/// <param name="Access">Access projection when the call succeeded.</param>
/// <param name="Problem">Translated failure when the call did not succeed.</param>
/// <param name="LoadedAt">When this snapshot was taken.</param>
public sealed record AccessSnapshot(
    CurrentAccessResponse? Access,
    UiProblem? Problem,
    DateTimeOffset LoadedAt)
{
    /// <summary>Whether access was resolved successfully.</summary>
    public bool IsResolved => Access is not null;

    /// <summary>Application access status, or <c>null</c> when unresolved.</summary>
    public string? Status => Access?.AccessStatus;

    /// <summary>Whether the caller is approved for application use.</summary>
    public bool IsApproved =>
        string.Equals(Status, AccessStatuses.Approved, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the caller is awaiting access approval.</summary>
    public bool IsPending =>
        string.Equals(Status, AccessStatuses.Pending, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the caller's access has been disabled.</summary>
    public bool IsDisabled =>
        string.Equals(Status, AccessStatuses.Disabled, StringComparison.OrdinalIgnoreCase);

    /// <summary>Granted application roles, empty when unresolved.</summary>
    public IReadOnlyList<string> Roles => Access?.Roles ?? [];

    /// <summary>Granted capabilities, empty when unresolved.</summary>
    public IReadOnlyList<string> Capabilities => Access?.Capabilities ?? [];

    /// <summary>
    /// Whether the caller holds a capability.
    /// </summary>
    /// <param name="capability">Capability identifier from <c>SecureOps.Shared.Auth.Capabilities</c>.</param>
    /// <returns><c>true</c> when the capability is granted.</returns>
    /// <remarks>
    /// Presentation only. Hiding an action is a courtesy that reduces dead ends; the API re-checks
    /// every capability and remains the security boundary.
    /// </remarks>
    public bool Can(string capability) =>
        Access is not null && Access.Capabilities.Contains(capability, StringComparer.Ordinal);

    /// <summary>
    /// Whether the caller holds at least one of the supplied capabilities.
    /// </summary>
    /// <param name="capabilities">Capability identifiers.</param>
    /// <returns><c>true</c> when any capability is granted.</returns>
    public bool CanAny(params string[] capabilities) => capabilities.Any(Can);
}
