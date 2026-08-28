using SecureOps.Shared.Contracts.Sessions;

namespace SecureOps.Ui.Services;

/// <summary>
/// Turns application-session metadata into what an administrator needs to recognise a person.
/// </summary>
/// <remarks>
/// The Active Sessions page previously led with the persisted user GUID, because that was the only
/// identity the endpoint returned. It now returns provider-neutral principal metadata, and an
/// administrator deciding whether to interrupt someone's work needs to read a name, not match a
/// GUID against another screen.
/// <para>
/// Nothing here invents identity. Every field the API supplies is nullable, and the fallback chain
/// walks from the friendliest real value to the rawest one — display name, principal, normalized
/// principal — and stops at an explicit "unknown" rather than a placeholder person. Internal
/// identifiers stay available for support, but never as the primary label.
/// </para>
/// </remarks>
public static class SessionView
{
    /// <summary>Shown when the API supplied no identity metadata at all.</summary>
    public const string UnknownUser = "Kimlik bilgisi yok";

    /// <summary>
    /// Primary operator-facing identity for a session.
    /// </summary>
    /// <param name="session">Session metadata.</param>
    /// <returns>Display name when present, otherwise the principal, otherwise the normalized principal.</returns>
    public static string Name(ApplicationSessionResponse session) =>
        FirstPresent(session.DisplayName, session.Principal, session.NormalizedPrincipal) ?? UnknownUser;

    /// <summary>
    /// Account line shown under the name.
    /// </summary>
    /// <param name="session">Session metadata.</param>
    /// <returns>The account identifier, or <see langword="null"/> when it would repeat the name.</returns>
    /// <remarks>
    /// Returns null when the account and the name are the same value. Printing an identifier twice
    /// reads as a rendering fault, and the contract says a display name is often absent — so the
    /// account frequently <i>is</i> the name.
    /// </remarks>
    public static string? Account(ApplicationSessionResponse session)
    {
        string? account = FirstPresent(session.Principal, session.NormalizedPrincipal);

        return account is null || string.Equals(account, Name(session), StringComparison.OrdinalIgnoreCase)
            ? null
            : account;
    }

    /// <summary>
    /// How the session was authenticated.
    /// </summary>
    /// <param name="session">Session metadata.</param>
    /// <returns>Provider name when the API supplies one, otherwise the authentication method.</returns>
    public static string Authentication(ApplicationSessionResponse session) =>
        FirstPresent(session.AuthenticationProvider, session.AuthenticationMethod) ?? "Bilinmiyor";

    /// <summary>
    /// Whether a session should still be listed as active.
    /// </summary>
    /// <param name="session">Session metadata.</param>
    /// <param name="now">Current time.</param>
    /// <returns><c>false</c> when the absolute lifetime has already elapsed.</returns>
    /// <remarks>
    /// The API marks expirations terminal before returning a page, so this should never filter
    /// anything. It exists because a session that has demonstrably ended must not be offered with a
    /// "Sonlandır" button: the administrator would be acting on something that no longer exists, and
    /// the resulting conflict would read as a fault in the page rather than as stale data.
    /// </remarks>
    public static bool IsActive(ApplicationSessionResponse session, DateTimeOffset now) =>
        session.AbsoluteExpiresAtUtc > now;

    /// <summary>
    /// Remaining lifetime in operator-friendly terms.
    /// </summary>
    /// <param name="session">Session metadata.</param>
    /// <param name="now">Current time.</param>
    /// <returns>A short "N sa M dk" style remainder, or an expiry statement when none is left.</returns>
    public static string Remaining(ApplicationSessionResponse session, DateTimeOffset now)
    {
        TimeSpan remaining = session.AbsoluteExpiresAtUtc - now;

        if (remaining <= TimeSpan.Zero)
        {
            return "Süresi doldu";
        }

        int hours = (int)remaining.TotalHours;
        int minutes = remaining.Minutes;

        return hours > 0 ? $"{hours} sa {minutes} dk" : $"{minutes} dk";
    }

    /// <summary>
    /// Local-time rendering used across the session screens.
    /// </summary>
    /// <param name="value">Server-supplied timestamp.</param>
    /// <returns>Day, month, year, hour, and minute in the viewer's local time.</returns>
    public static string Local(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    private static string? FirstPresent(params string?[] candidates) =>
        Array.Find(candidates, candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();
}
