using SecureOps.Domain.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>Durable application access and approval persistence boundary.</summary>
public interface IAccessRepository
{
    /// <summary>Creates a pending user/request on first authentication and returns the current snapshot.</summary>
    public Task<EnsureAccessUserResult> EnsureUserAsync(CorporatePrincipal principal, bool createRequest, CancellationToken cancellationToken);
    /// <summary>Gets a user by internal identifier.</summary>
    public Task<ApplicationUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Gets a user by stable corporate principal.</summary>
    public Task<ApplicationUser?> GetUserAsync(string corporateIdentity, CancellationToken cancellationToken);
    /// <summary>Gets the current pending request for a user.</summary>
    public Task<ApplicationAccessRequest?> GetPendingRequestAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Lists access requests, optionally limited to one state.</summary>
    public Task<IReadOnlyList<ApplicationAccessRequest>> ListRequestsAsync(AccessRequestStatus? status, CancellationToken cancellationToken);
    /// <summary>Atomically approves or rejects one pending request.</summary>
    public Task<AccessMutationResult> DecideRequestAsync(Guid requestId, AccessRequestStatus decision, string actor, IReadOnlyCollection<string> roles, string reason, CancellationToken cancellationToken);
    /// <summary>Atomically replaces active roles for an approved user.</summary>
    public Task<AccessMutationResult> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, string actor, string reason, CancellationToken cancellationToken);
    /// <summary>Disables application access and revokes active roles.</summary>
    public Task<AccessMutationResult> DisableUserAsync(Guid userId, string actor, string reason, CancellationToken cancellationToken);
}

/// <summary>Result of first-seen user reconciliation.</summary>
public sealed record EnsureAccessUserResult(ApplicationUser User, ApplicationAccessRequest? PendingRequest, bool UserCreated, bool RequestCreated);

/// <summary>Access persistence mutation disposition.</summary>
public enum AccessMutationDisposition
{
    /// <summary>The mutation was applied.</summary>
    Applied,
    /// <summary>The user or request was not found.</summary>
    NotFound,
    /// <summary>The current state rejects the mutation.</summary>
    InvalidState,
    /// <summary>The actor attempted to approve their own request.</summary>
    SelfApprovalDenied
}

/// <summary>Result of an access mutation.</summary>
public sealed record AccessMutationResult(AccessMutationDisposition Disposition, ApplicationUser? User, ApplicationAccessRequest? Request, IReadOnlyList<string> AddedRoles, IReadOnlyList<string> RemovedRoles);
