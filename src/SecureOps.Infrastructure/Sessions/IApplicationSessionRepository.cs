using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.Sessions;

/// <summary>Authoritative application-session persistence boundary.</summary>
public interface IApplicationSessionRepository
{
    /// <summary>Inserts a new application session.</summary>
    public Task InsertAsync(ApplicationSession session, CancellationToken cancellationToken);

    /// <summary>Gets one exact session.</summary>
    public Task<ApplicationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Persists activity only when the prior timestamp is old enough.</summary>
    public Task<bool> TouchAsync(Guid sessionId, DateTimeOffset lastSeenAtUtc, DateTimeOffset persistBeforeUtc, CancellationToken cancellationToken);

    /// <summary>Compensates a failed session-start audit. Normal termination must use the audited operation.</summary>
    public Task<bool> EndAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken);

    /// <summary>Commits one termination and its append-only audit in one operation.</summary>
    /// <returns>Whether this operation ended an active session and wrote its audit. A loser writes no audit; the caller rereads the winning state.</returns>
    public Task<bool> EndWithAuditAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, AuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>Atomically ends all active sessions for one user with their required audit and returns the affected sessions.</summary>
    public Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, Func<ApplicationSession, AuditEvent> auditFactory, CancellationToken cancellationToken);

    /// <summary>Atomically transitions expired active sessions with all required append-only audit events.</summary>
    public Task<IReadOnlyList<ApplicationSession>> EndExpiredAsync(
        DateTimeOffset nowUtc,
        DateTimeOffset idleCutoffUtc,
        int maximumCount,
        Func<ApplicationSession, AuditEvent> auditFactory,
        CancellationToken cancellationToken);

    /// <summary>Returns a bounded page of currently effective sessions.</summary>
    public Task<IReadOnlyList<ApplicationSession>> ListActiveAsync(DateTimeOffset absoluteCutoffUtc, DateTimeOffset idleCutoffUtc, int skip, int take, CancellationToken cancellationToken);
}
