using System.Security.Claims;
using SecureOps.Domain.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>Application access approval, role, and capability orchestration.</summary>
public interface IApplicationAccessService
{
    /// <summary>Reconciles first-seen state and returns current access.</summary>
    public Task<AccessServiceResult<EnsureAccessUserResult>> GetCurrentAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Lists access requests for authorized administrators.</summary>
    public Task<AccessServiceResult<IReadOnlyList<ApplicationAccessRequest>>> ListRequestsAsync(AccessRequestStatus? status, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Approves a pending request with reviewed roles.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> ApproveAsync(Guid requestId, IReadOnlyCollection<string> roles, string reason, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Rejects a pending request.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> RejectAsync(Guid requestId, string reason, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Replaces active roles for an approved user.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, string reason, AccessOperationContext context, CancellationToken cancellationToken);
    /// <summary>Disables user access and active roles.</summary>
    public Task<AccessServiceResult<AccessMutationResult>> DisableAsync(Guid userId, string reason, AccessOperationContext context, CancellationToken cancellationToken);
}
