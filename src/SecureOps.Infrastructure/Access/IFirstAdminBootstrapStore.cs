namespace SecureOps.Infrastructure.Access;

/// <summary>Atomic persistence boundary for the one-time OIDC first-Admin grant.</summary>
public interface IFirstAdminBootstrapStore
{
    /// <summary>Attempts the one-time grant and its audit evidence in one transaction.</summary>
    public Task<FirstAdminBootstrapDisposition> TryGrantAsync(
        FirstAdminBootstrapCommand command,
        CancellationToken cancellationToken);
}

/// <summary>Safe inputs for the atomic first-Admin persistence operation.</summary>
public sealed record FirstAdminBootstrapCommand(
    Guid UserId,
    Guid AccessRequestId,
    long AccessRequestVersion,
    string StableIdentity,
    string? CorrelationId,
    string? SourceIp);

/// <summary>Outcome of the atomic first-Admin persistence operation.</summary>
public enum FirstAdminBootstrapDisposition
{
    /// <summary>The first Admin assignment and its audit evidence were committed.</summary>
    Applied,
    /// <summary>An Admin assignment already exists or existed previously.</summary>
    AlreadyProvisioned,
    /// <summary>The user, request, role, or concurrency state was not eligible.</summary>
    NotEligible
}
