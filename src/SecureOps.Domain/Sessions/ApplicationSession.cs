namespace SecureOps.Domain.Sessions;

/// <summary>Authoritative server-side SecureOps application session.</summary>
public sealed record ApplicationSession(
    Guid SessionId,
    Guid UserId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset? EndedAtUtc,
    SessionEndReason? EndReason,
    string AuthenticationMethod,
    long AccessVersion)
{
    /// <summary>Whether no terminal state has been persisted.</summary>
    public bool IsActive => EndedAtUtc is null;
}

/// <summary>Terminal application-session reasons.</summary>
public enum SessionEndReason
{
    /// <summary>The configured idle limit elapsed.</summary>
    IdleTimeout,
    /// <summary>The absolute lifetime elapsed.</summary>
    AbsoluteTimeout,
    /// <summary>The user explicitly logged out of the SecureOps session.</summary>
    Logout,
    /// <summary>An authorized administrator revoked the session.</summary>
    Revoked,
    /// <summary>Application access was disabled.</summary>
    AccessDisabled,
    /// <summary>Roles or another access decision changed.</summary>
    AccessChanged,
    /// <summary>Session start was rolled back after mandatory audit failed.</summary>
    AuditFailure
}
