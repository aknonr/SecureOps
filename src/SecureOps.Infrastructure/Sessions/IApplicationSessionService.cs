using System.Security.Claims;
using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Access;

namespace SecureOps.Infrastructure.Sessions;

/// <summary>Provider-neutral application-session lifecycle boundary.</summary>
public interface IApplicationSessionService
{
    /// <summary>Validates an existing server session or creates one for an authenticated principal.</summary>
    public Task<ApplicationSessionResult> ValidateOrStartAsync(ClaimsPrincipal principal, Guid? presentedSessionId, AccessOperationContext context, CancellationToken cancellationToken);

    /// <summary>Ends the current application session for explicit logout.</summary>
    public Task<ApplicationSessionResult> LogoutAsync(Guid sessionId, AccessOperationContext context, CancellationToken cancellationToken);

    /// <summary>Lists a bounded page of active sessions after mandatory audit.</summary>
    public Task<ApplicationSessionListResult> ListActiveAsync(int page, int pageSize, AccessOperationContext context, CancellationToken cancellationToken);

    /// <summary>Revokes one exact active session after mandatory audit.</summary>
    public Task<ApplicationSessionResult> RevokeAsync(Guid sessionId, string reason, AccessOperationContext context, CancellationToken cancellationToken);

    /// <summary>Ends all active sessions after application-access disable or version change.</summary>
    public Task<ApplicationSessionTerminationResult> EndUserSessionsAsync(Guid userId, SessionEndReason reason, AccessOperationContext context, CancellationToken cancellationToken);
}

/// <summary>Application-session lifecycle result.</summary>
public sealed record ApplicationSessionResult(ApplicationSessionDisposition Disposition, ApplicationSession? Session, string? ErrorCode)
{
    /// <summary>Whether the request has a valid current session.</summary>
    public bool IsSuccess => Disposition is ApplicationSessionDisposition.Active or ApplicationSessionDisposition.Started or ApplicationSessionDisposition.Ended;
}

/// <summary>Bounded active-session list result.</summary>
public sealed record ApplicationSessionListResult(IReadOnlyList<ApplicationSession>? Sessions, string? ErrorCode)
{
    /// <summary>Whether the list was produced.</summary>
    public bool IsSuccess => ErrorCode is null;
}

/// <summary>Bulk user-session termination result.</summary>
public sealed record ApplicationSessionTerminationResult(int EndedCount, string? ErrorCode)
{
    /// <summary>Whether persistence and all mandatory audits completed.</summary>
    public bool IsSuccess => ErrorCode is null;
}

/// <summary>Application-session lifecycle dispositions.</summary>
public enum ApplicationSessionDisposition
{
    /// <summary>An existing session remains active.</summary>
    Active,
    /// <summary>A new session was persisted and audited.</summary>
    Started,
    /// <summary>A requested terminal transition was applied.</summary>
    Ended,
    /// <summary>The idle lifetime elapsed.</summary>
    IdleExpired,
    /// <summary>The absolute lifetime elapsed.</summary>
    AbsoluteExpired,
    /// <summary>The session was revoked or is no longer active.</summary>
    Revoked,
    /// <summary>Application access is disabled.</summary>
    AccessDisabled,
    /// <summary>The session access version is stale.</summary>
    AccessChanged,
    /// <summary>Authentication/access reconciliation failed.</summary>
    AccessDenied,
    /// <summary>The session store is unavailable.</summary>
    StoreUnavailable,
    /// <summary>Mandatory session audit is unavailable.</summary>
    AuditUnavailable,
    /// <summary>Session input failed validation.</summary>
    Invalid
}
