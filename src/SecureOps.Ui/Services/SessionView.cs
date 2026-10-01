using SecureOps.Shared.Contracts.Sessions;

namespace SecureOps.Ui.Services;

/// <summary>Builds safe administrator-facing application-session values.</summary>
public static class SessionView
{
    /// <summary>Shown when the API supplied no human identity metadata.</summary>
    public const string UnknownUser = "Kimlik bilgisi yok";

    /// <summary>Shown for an OIDC user whose persisted profile has not been backfilled.</summary>
    public const string ProfilePending = "OIDC profil bilgisi bekleniyor";

    /// <summary>Display name, exact login name, or an explicit bounded fallback.</summary>
    public static string Name(ApplicationSessionResponse session) =>
        FirstPresent(session.DisplayName, session.Principal)
        ?? (IsOidc(session) ? ProfilePending : UnknownUser);

    /// <summary>Exact account line shown only when it does not repeat the primary label.</summary>
    public static string? Account(ApplicationSessionResponse session)
    {
        string? account = FirstPresent(session.Principal);
        return account is null || string.Equals(account, Name(session), StringComparison.OrdinalIgnoreCase)
            ? null
            : account;
    }

    /// <summary>Provider name when available, otherwise the authentication method.</summary>
    public static string Authentication(ApplicationSessionResponse session) =>
        FirstPresent(session.AuthenticationProvider, session.AuthenticationMethod) ?? "Bilinmiyor";

    /// <summary>Persisted corporate employee number used only as profile metadata.</summary>
    public static string? Uid(ApplicationSessionResponse session) => FirstPresent(session.Uid);

    /// <summary>Whether the absolute session lifetime remains valid.</summary>
    public static bool IsActive(ApplicationSessionResponse session, DateTimeOffset now) =>
        session.AbsoluteExpiresAtUtc > now;

    /// <summary>Remaining absolute lifetime in operator-friendly terms.</summary>
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

    /// <summary>Local-time rendering used across the session screens.</summary>
    public static string Local(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    private static string? FirstPresent(params string?[] candidates) =>
        Array.Find(candidates, candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private static bool IsOidc(ApplicationSessionResponse session) =>
        string.Equals(session.AuthenticationProvider, "oidc", StringComparison.OrdinalIgnoreCase)
        || session.NormalizedPrincipal?.StartsWith("oidc:", StringComparison.OrdinalIgnoreCase) == true;
}
