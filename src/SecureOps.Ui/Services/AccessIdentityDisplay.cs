using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Turns nullable directory enrichment into something safe to put on screen.
/// </summary>
/// <remarks>
/// Every field of <see cref="AccessIdentityProfileResponse"/> is nullable, and the whole object is
/// nullable, because the provider resolves what it can and reports nothing for what it cannot. The
/// demo bridge resolves nothing at all, so "absent" is the normal case rather than an edge one.
/// <para>
/// The rule this type exists to enforce: <b>fall back to the principal identifier, never to a
/// placeholder</b>. An invented name, a blank line where a name belongs, or an em dash standing in
/// for an e-mail all read as data. On a screen where an administrator grants authority, a
/// fabricated person is a safety problem, not a cosmetic one — so an unresolvable profile shows the
/// raw identifier the API actually gave us, and the caller states plainly that enrichment is
/// unavailable.
/// </para>
/// </remarks>
public static class AccessIdentityDisplay
{
    /// <summary>
    /// The best available name for a principal.
    /// </summary>
    /// <param name="profile">Nullable enrichment.</param>
    /// <param name="corporateIdentity">Principal identifier, always present.</param>
    /// <returns><c>DisplayName</c>, then the exact account name, then the principal identifier.</returns>
    public static string Name(AccessIdentityProfileResponse? profile, string corporateIdentity) =>
        !string.IsNullOrWhiteSpace(profile?.DisplayName)
            ? profile.DisplayName
            : !string.IsNullOrWhiteSpace(profile?.Account)
                ? profile.Account
                : corporateIdentity;

    /// <summary>
    /// Whether a name distinct from the principal identifier was resolved.
    /// </summary>
    /// <param name="profile">Nullable enrichment.</param>
    /// <returns><c>true</c> when a display name or exact account name is available.</returns>
    /// <remarks>
    /// Used to decide whether to show the identifier as a secondary line. When the name <i>is</i> the
    /// identifier, printing it twice looks like a rendering fault.
    /// </remarks>
    public static bool HasName(AccessIdentityProfileResponse? profile) =>
        !string.IsNullOrWhiteSpace(profile?.DisplayName)
        || !string.IsNullOrWhiteSpace(profile?.Account);

    /// <summary>
    /// Whether any enrichment field at all was resolved.
    /// </summary>
    /// <param name="profile">Nullable enrichment.</param>
    /// <returns><c>true</c> when at least one field carries a value.</returns>
    public static bool HasAny(AccessIdentityProfileResponse? profile) =>
        profile is not null
        && (!string.IsNullOrWhiteSpace(profile.DisplayName)
            || !string.IsNullOrWhiteSpace(profile.Account)
            || !string.IsNullOrWhiteSpace(profile.Email)
            || !string.IsNullOrWhiteSpace(profile.Department)
            || !string.IsNullOrWhiteSpace(profile.Title));

    // Separators used when deriving initials. (char)92 is a backslash, written this way so no
    // literal backslash appears in the source.
    private static readonly char[] Separators =
        [' ', '.', '-', '_', ':', '/', (char)92];

    /// <summary>
    /// Initials for an avatar, derived from whatever name is actually shown.
    /// </summary>
    /// <param name="profile">Nullable enrichment.</param>
    /// <param name="corporateIdentity">Principal identifier.</param>
    /// <returns>One or two uppercase letters.</returns>
    public static string Initials(AccessIdentityProfileResponse? profile, string corporateIdentity)
    {
        string source = Name(profile, corporateIdentity);

        // A principal identifier is commonly "demo:team-lead" or "DOMAIN\user"; splitting on those
        // separators as well as whitespace keeps the initials meaningful when no real name exists.
        // Separators live in a field because a backslash char literal here was mangled
        // by the tooling chain; (char)92 avoids writing one at all.
        string[] parts = source
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }
}
