using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    // Contract first (CHANGE-PLAN-DESIGN.md, PR 2): checklist, plan evidence and completion routes are fixed; until their
    // persistence lands every command answers "not installed" after the capability check, so nothing is written.

    /// <summary>Checklist marks of one row of the plan's current preview, newest first.</summary>
    public Task<SaResult<IReadOnlyList<ChangePlanItemCheckView>>> ChangePlanItemChecksAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid itemId, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.View,
            _ => Task.FromResult(SaResult<IReadOnlyList<ChangePlanItemCheckView>>.Fail(SaErrors.ChangePlansNotInstalled)), cancellationToken);

    /// <summary>Records the person's own statement for one row of the approved preview.</summary>
    public Task<SaResult<ChangePlanView>> CheckChangePlanItemAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, Guid itemId,
        CheckChangePlanItemRequest request, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work,
            _ => Task.FromResult(SaResult<ChangePlanView>.Fail(SaErrors.ChangePlansNotInstalled)), cancellationToken);

    /// <summary>Attaches evidence to an approved plan.</summary>
    public Task<SaResult<ChangePlanView>> AddChangePlanEvidenceAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, string fileName,
        string contentType, byte[] content, string? label, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work,
            _ => Task.FromResult(SaResult<ChangePlanView>.Fail(SaErrors.ChangePlanEvidenceNotInstalled)), cancellationToken);

    /// <summary>Closes an InProgress plan whose every row is marked.</summary>
    public Task<SaResult<ChangePlanView>> CompleteChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        CompleteChangePlanRequest request, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work,
            _ => Task.FromResult(SaResult<ChangePlanView>.Fail(SaErrors.ChangePlansNotInstalled)), cancellationToken);
}
