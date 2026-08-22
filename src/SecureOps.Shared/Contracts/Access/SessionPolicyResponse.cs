namespace SecureOps.Shared.Contracts.Access;

/// <summary>Safe provider-neutral session-policy metadata.</summary>
public sealed record SessionPolicyResponse(
    int IdleTimeoutMinutes,
    int AbsoluteLifetimeHours,
    bool SecureCookie,
    bool HttpOnly,
    string SameSite,
    bool AccessRevalidatedOnEveryRequest,
    string EnforcementMode)
{
    /// <summary>Minimum interval between persisted activity updates.</summary>
    public int ActivityPersistenceIntervalMinutes { get; init; }
}
