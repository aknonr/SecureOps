using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>Builds safe human-facing labels from persisted profile data.</summary>
public static class AccessIdentityDisplay
{
    /// <summary>Primary label for an OIDC user whose profile has not been backfilled.</summary>
    public const string ProfilePending = "OIDC profil bilgisi bekleniyor";

    /// <summary>Explains how an existing opaque-only OIDC user obtains profile data.</summary>
    public const string ProfileRefreshNotice = "Profil, kullanıcının sonraki başarılı OIDC girişinde güncellenir.";

    /// <summary>Display name, then exact account name, then a bounded fallback.</summary>
    public static string Name(AccessIdentityProfileResponse? profile, string corporateIdentity) =>
        !string.IsNullOrWhiteSpace(profile?.DisplayName)
            ? profile.DisplayName.Trim()
            : !string.IsNullOrWhiteSpace(profile?.Account)
                ? profile.Account.Trim()
                : IsOidc(corporateIdentity)
                    ? ProfilePending
                    : corporateIdentity;

    /// <summary>Exact account name shown below a distinct display name.</summary>
    public static string? SecondaryAccount(AccessIdentityProfileResponse? profile) =>
        !string.IsNullOrWhiteSpace(profile?.DisplayName) && !string.IsNullOrWhiteSpace(profile.Account)
            ? profile.Account.Trim()
            : null;

    /// <summary>Persisted corporate employee number used only as profile metadata.</summary>
    public static string? Uid(AccessIdentityProfileResponse? profile) =>
        string.IsNullOrWhiteSpace(profile?.Uid) ? null : profile.Uid.Trim();

    /// <summary>Whether an opaque-only OIDC record is waiting for profile backfill.</summary>
    public static bool NeedsProfileBackfill(AccessIdentityProfileResponse? profile, string corporateIdentity) =>
        !HasAny(profile) && IsOidc(corporateIdentity);

    /// <summary>Whether a display name or exact account name is available.</summary>
    public static bool HasName(AccessIdentityProfileResponse? profile) =>
        !string.IsNullOrWhiteSpace(profile?.DisplayName)
        || !string.IsNullOrWhiteSpace(profile?.Account);

    /// <summary>Whether any profile or directory field carries a value.</summary>
    public static bool HasAny(AccessIdentityProfileResponse? profile) =>
        profile is not null
        && (!string.IsNullOrWhiteSpace(profile.DisplayName)
            || !string.IsNullOrWhiteSpace(profile.Account)
            || !string.IsNullOrWhiteSpace(profile.Email)
            || !string.IsNullOrWhiteSpace(profile.Department)
            || !string.IsNullOrWhiteSpace(profile.Title)
            || !string.IsNullOrWhiteSpace(profile.Uid));

    private static readonly char[] Separators = [' ', '.', '-', '_', ':', '/', (char)92];

    /// <summary>One or two initials derived from the human-facing label.</summary>
    public static string Initials(AccessIdentityProfileResponse? profile, string corporateIdentity)
    {
        string source = Name(profile, corporateIdentity);
        if (string.Equals(source, ProfilePending, StringComparison.Ordinal))
        {
            return "?";
        }

        string[] parts = source.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }

    private static bool IsOidc(string corporateIdentity) =>
        corporateIdentity.StartsWith("oidc:", StringComparison.OrdinalIgnoreCase);
}
