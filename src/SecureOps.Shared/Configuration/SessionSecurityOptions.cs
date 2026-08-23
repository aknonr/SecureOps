namespace SecureOps.Shared.Configuration;

/// <summary>Provider-neutral SecureOps application-session policy.</summary>
public sealed class SessionSecurityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SessionSecurity";

    /// <summary>Maximum idle period for an application session.</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>Absolute maximum lifetime for an application session.</summary>
    public int AbsoluteLifetimeHours { get; set; } = 12;

    /// <summary>Minimum interval between persisted LastSeen updates.</summary>
    public int ActivityPersistenceIntervalMinutes { get; set; } = 5;

    /// <summary>Session-state repository provider: InMemory or SqlServer.</summary>
    public string RepositoryProvider { get; set; } = "InMemory";

    /// <summary>Opaque application-session cookie name.</summary>
    public string CookieName { get; set; } = "__Host-SecureOps.ApplicationSession";

    /// <summary>Maximum administrative active-session page size.</summary>
    public int MaxAdminPageSize { get; set; } = 100;

    /// <summary>Whether the opaque application-session cookie must be Secure.</summary>
    public bool SecureCookie { get; set; } = true;

    /// <summary>Whether the opaque application-session cookie must be inaccessible to client script.</summary>
    public bool HttpOnly { get; set; } = true;

    /// <summary>SameSite mode for the opaque application-session cookie.</summary>
    public string SameSite { get; set; } = "Lax";

    /// <summary>Whether application access is revalidated on every authorized request.</summary>
    public bool RevalidateAccessOnEveryRequest { get; set; } = true;
}
