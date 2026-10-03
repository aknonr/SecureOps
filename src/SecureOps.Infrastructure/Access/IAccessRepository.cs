using SecureOps.Domain.Access;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>Durable application access and approval persistence boundary.</summary>
public interface IAccessRepository
{
    /// <summary>Creates a pending user/request on first authentication and returns the current snapshot.</summary>
    public Task<EnsureAccessUserResult> EnsureUserAsync(CorporatePrincipal principal, bool createRequest, TimeSpan activityPersistenceInterval, CancellationToken cancellationToken);
    /// <summary>Gets a user by internal identifier.</summary>
    public Task<ApplicationUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Gets a user by stable corporate principal.</summary>
    public Task<ApplicationUser?> GetUserAsync(string corporateIdentity, CancellationToken cancellationToken);
    /// <summary>Lists all application users for an authorized administrative projection.</summary>
    public Task<IReadOnlyList<ApplicationUser>> ListUsersAsync(CancellationToken cancellationToken);
    /// <summary>Gets the current pending request for a user.</summary>
    public Task<ApplicationAccessRequest?> GetPendingRequestAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Lists access requests, optionally limited to one state.</summary>
    public Task<IReadOnlyList<ApplicationAccessRequest>> ListRequestsAsync(AccessRequestStatus? status, CancellationToken cancellationToken);
    /// <summary>Lists the complete request history for one application user.</summary>
    public Task<IReadOnlyList<ApplicationAccessRequest>> ListRequestsForUserAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Atomically approves or rejects one pending request.</summary>
    public Task<AccessMutationResult> DecideRequestAsync(Guid requestId, AccessRequestStatus decision, long expectedVersion, string actor, IReadOnlyCollection<string> roles, string reason, CancellationToken cancellationToken, IReadOnlyDictionary<string, long>? roleVersions = null);
    /// <summary>Atomically replaces active roles for an approved user.</summary>
    public Task<AccessMutationResult> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, long expectedVersion, string actor, CancellationToken cancellationToken, IReadOnlyDictionary<string, long>? roleVersions = null);
    /// <summary>Disables application access and revokes active roles.</summary>
    public Task<AccessMutationResult> DisableUserAsync(Guid userId, long expectedVersion, string actor, string reason, CancellationToken cancellationToken);
    /// <summary>Reads the current business role definitions (code, name, version, protection, capabilities).</summary>
    public Task<IReadOnlyList<AccessRoleDefinition>> GetRoleDefinitionsAsync(CancellationToken cancellationToken);
}

/// <summary>Result of first-seen user reconciliation.</summary>
public sealed record EnsureAccessUserResult(ApplicationUser User, ApplicationAccessRequest? LatestRequest, bool UserCreated, bool RequestCreated)
{
    /// <summary>The current request only when it remains pending.</summary>
    public ApplicationAccessRequest? PendingRequest => LatestRequest?.Status == AccessRequestStatus.Pending ? LatestRequest : null;
}

/// <summary>Access persistence mutation disposition.</summary>
public enum AccessMutationDisposition
{
    /// <summary>The mutation was applied.</summary>
    Applied,
    /// <summary>The user or request was not found.</summary>
    NotFound,
    /// <summary>The current state rejects the mutation.</summary>
    RequestAlreadyDecided,
    /// <summary>The supplied version no longer matches persisted state.</summary>
    ConcurrencyConflict,
    /// <summary>The user lifecycle rejects the mutation.</summary>
    UserInvalidState,
    /// <summary>The actor attempted to approve their own request.</summary>
    SelfApprovalDenied,
    /// <summary>The role set contains an unregistered role.</summary>
    InvalidRoles,
    /// <summary>Self-escalation or last-administrator protection rejected the change.</summary>
    AdministrativeGuard,
    /// <summary>The caller cannot add capabilities to their own identity.</summary>
    SelfEscalationDenied,
    /// <summary>At least one approved administrator must remain.</summary>
    LastAdministratorDenied
}

/// <summary>Result of an access mutation.</summary>
public sealed record AccessMutationResult(AccessMutationDisposition Disposition, ApplicationUser? User, ApplicationAccessRequest? Request, IReadOnlyList<string> AddedRoles, IReadOnlyList<string> RemovedRoles);
