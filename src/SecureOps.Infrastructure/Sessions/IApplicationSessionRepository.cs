using SecureOps.Domain.Sessions;

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

    /// <summary>Ends one active session exactly once.</summary>
    public Task<bool> EndAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken);

    /// <summary>Ends all active sessions for one user and returns the affected identifiers.</summary>
    public Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken);

    /// <summary>Returns a bounded page of currently effective sessions.</summary>
    public Task<IReadOnlyList<ApplicationSession>> ListActiveAsync(DateTimeOffset absoluteCutoffUtc, DateTimeOffset idleCutoffUtc, int skip, int take, CancellationToken cancellationToken);
}
