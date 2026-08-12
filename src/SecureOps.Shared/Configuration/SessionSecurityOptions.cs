namespace SecureOps.Shared.Configuration;

/// <summary>Provider-neutral web authentication session policy.</summary>
public sealed class SessionSecurityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SessionSecurity";

    /// <summary>Maximum idle period for a future stateful cookie/OIDC session.</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>Absolute maximum lifetime for a future stateful cookie/OIDC session.</summary>
    public int AbsoluteLifetimeHours { get; set; } = 8;

    /// <summary>Whether any future authentication cookie must be Secure.</summary>
    public bool SecureCookie { get; set; } = true;

    /// <summary>Whether any future authentication cookie must be inaccessible to client script.</summary>
    public bool HttpOnly { get; set; } = true;

    /// <summary>SameSite mode reserved for a future cookie/OIDC handler.</summary>
    public string SameSite { get; set; } = "Lax";

    /// <summary>Whether application access is revalidated on every authorized request.</summary>
    public bool RevalidateAccessOnEveryRequest { get; set; } = true;
}
