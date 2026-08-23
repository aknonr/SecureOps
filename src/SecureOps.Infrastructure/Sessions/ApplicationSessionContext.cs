using SecureOps.Domain.Sessions;

namespace SecureOps.Infrastructure.Sessions;

/// <summary>Scoped current-request application-session state.</summary>
public sealed class ApplicationSessionContext
{
    /// <summary>The current validated application session, when available.</summary>
    public ApplicationSession? Current { get; private set; }

    /// <summary>Sets the validated application session once for this request.</summary>
    public void Set(ApplicationSession session) => Current = session;
}
