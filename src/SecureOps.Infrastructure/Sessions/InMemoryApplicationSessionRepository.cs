using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.Sessions;

/// <summary>Concurrency-safe non-durable application-session repository for local and isolated tests.</summary>
public sealed class InMemoryApplicationSessionRepository(IAuditWriter auditWriter) : IApplicationSessionRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<Guid, ApplicationSession> _sessions = [];

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
    public async Task<bool> EndWithAuditAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Dictionary<Guid, ApplicationSession> next = new(_sessions);
            bool ended = next.TryGetValue(sessionId, out ApplicationSession? session) && session.IsActive;
            if (ended)
            {
                next[sessionId] = session! with { EndedAtUtc = endedAtUtc, EndReason = reason };
            }

            // Prepare state before audit; after the atomic append, publication cannot fail or cancel.
            await AppendAuditAsync([auditEvent], cancellationToken);
            _sessions = next;
            return ended;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, Func<ApplicationSession, AuditEvent> auditFactory, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ApplicationSession[] affected = _sessions.Values
                .Where(session => session.UserId == userId && session.IsActive)
                .ToArray();
            Dictionary<Guid, ApplicationSession> next = new(_sessions);
            foreach (ApplicationSession session in affected)
            {
                next[session.SessionId] = session with { EndedAtUtc = endedAtUtc, EndReason = reason };
            }

            await AppendAuditAsync(affected.Select(auditFactory).ToArray(), cancellationToken);
            _sessions = next;
            return affected;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationSession>> EndExpiredAsync(
        DateTimeOffset nowUtc,
        DateTimeOffset idleCutoffUtc,
        int maximumCount,
        Func<ApplicationSession, AuditEvent> auditFactory,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<ApplicationSession> ended = [];
            Dictionary<Guid, ApplicationSession> next = new(_sessions);
            foreach (ApplicationSession session in _sessions.Values
                         .Where(item => item.IsActive
                             && (item.AbsoluteExpiresAtUtc <= nowUtc || item.LastSeenAtUtc <= idleCutoffUtc))
                         .OrderBy(item => item.AbsoluteExpiresAtUtc)
                         .ThenBy(item => item.LastSeenAtUtc)
                         .Take(maximumCount)
                         .ToArray())
            {
                SessionEndReason? reason = session.AbsoluteExpiresAtUtc <= nowUtc
                    ? SessionEndReason.AbsoluteTimeout
                    : session.LastSeenAtUtc <= idleCutoffUtc
                        ? SessionEndReason.IdleTimeout
                        : null;
                if (reason is null)
                {
                    continue;
                }

                ApplicationSession terminal = session with { EndedAtUtc = nowUtc, EndReason = reason };
                next[session.SessionId] = terminal;
                ended.Add(terminal);
            }

            await AppendAuditAsync(ended.Select(auditFactory).ToArray(), cancellationToken);
            _sessions = next;
            return ended;
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

    private async Task AppendAuditAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        try
        {
            if (auditWriter is not IAtomicAuditWriter atomic)
            {
                throw new AuditWriteUnavailableException("Atomic in-memory session termination requires an atomic local audit writer.");
            }

            await atomic.WriteBatchAsync(events, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AuditWriteUnavailableException("Application-session atomic local audit failed.", exception);
        }
    }
}
