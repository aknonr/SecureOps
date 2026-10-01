using System.Security.Claims;
using SecureOps.Domain.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>Application access approval, role, and capability orchestration.</summary>
public interface IApplicationAccessService
{
    /// <summary>Reconciles first-seen state and returns current access.</summary>
    public Task<AccessServiceResult<EnsureAccessUserResult>> GetCurrentAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Resolves optional safe profile fields for the current access principal.</summary>
    public Task<AccessIdentityProfile?> GetProfileAsync(ApplicationUser user, CancellationToken cancellationToken);
    /// <summary>Lists access requests for authorized administrators.</summary>
    public Task<AccessServiceResult<IReadOnlyList<AccessRequestReadModel>>> ListRequestsAsync(AccessRequestStatus? status, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Lists authoritative access-user projections for authorized administrators.</summary>
    public Task<AccessServiceResult<IReadOnlyList<AccessUserReadModel>>> ListUsersAsync(AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Gets one authoritative access-user projection for an authorized administrator.</summary>
    public Task<AccessServiceResult<AccessUserReadModel>> GetUserAsync(Guid userId, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Approves a pending request with reviewed roles.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> ApproveAsync(Guid requestId, IReadOnlyCollection<string> roles, string reason, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken, IReadOnlyDictionary<string, long>? roleVersions = null);
    /// <summary>Rejects a pending request.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> RejectAsync(Guid requestId, string reason, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Replaces active roles for an approved user.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken, IReadOnlyDictionary<string, long>? roleVersions = null);
    /// <summary>Disables user access and active roles.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> DisableAsync(Guid userId, string reason, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken);
}
