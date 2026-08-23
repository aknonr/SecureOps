using SecureOps.Domain.Sessions;

namespace SecureOps.Infrastructure.Sessions;

/// <summary>Concurrency-safe non-durable application-session repository for local and isolated tests.</summary>
public sealed class InMemoryApplicationSessionRepository : IApplicationSessionRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, ApplicationSession> _sessions = [];

    /// <inheritdoc />
    public async Task InsertAsync(ApplicationSession session, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _sessions.Add(session.SessionId, session);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ApplicationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _sessions.GetValueOrDefault(sessionId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> TouchAsync(Guid sessionId, DateTimeOffset lastSeenAtUtc, DateTimeOffset persistBeforeUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out ApplicationSession? session)
                || !session.IsActive
                || session.LastSeenAtUtc > persistBeforeUtc)
            {
                return false;
            }

            _sessions[sessionId] = session with { LastSeenAtUtc = lastSeenAtUtc };
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> EndAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out ApplicationSession? session) || !session.IsActive)
            {
                return false;
            }

            _sessions[sessionId] = session with { EndedAtUtc = endedAtUtc, EndReason = reason };
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ApplicationSession[] affected = _sessions.Values
                .Where(session => session.UserId == userId && session.IsActive)
                .ToArray();
            foreach (ApplicationSession session in affected)
            {
                _sessions[session.SessionId] = session with { EndedAtUtc = endedAtUtc, EndReason = reason };
            }

            return affected;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationSession>> ListActiveAsync(DateTimeOffset absoluteCutoffUtc, DateTimeOffset idleCutoffUtc, int skip, int take, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _sessions.Values
                .Where(session => session.IsActive
                    && session.AbsoluteExpiresAtUtc > absoluteCutoffUtc
                    && session.LastSeenAtUtc > idleCutoffUtc)
                .OrderByDescending(session => session.LastSeenAtUtc)
                .ThenBy(session => session.SessionId)
                .Skip(skip)
                .Take(take)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }
}
