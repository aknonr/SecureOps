using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    // Contract first (CHANGE-PLAN-DESIGN.md, PR 1): routes and DTOs are fixed; until the 033 persistence lands every command
    // answers "not installed" after the capability check, so nothing is written and nothing is claimed.

    /// <summary>Plans whose every account is in the caller's scope.</summary>
    public Task<SaResult<ChangePlanPage>> ChangePlansAsync(ClaimsPrincipal principal, AccessOperationContext context, ChangePlanListQuery query,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanPage>(principal, context, ServiceAccountCapabilities.View, cancellationToken);

    /// <summary>Plan detail (404 when any account is outside the caller's scope).</summary>
    public Task<SaResult<ChangePlanView>> ChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanView>(principal, context, ServiceAccountCapabilities.View, cancellationToken);

    /// <summary>Rows of the current preview.</summary>
    public Task<SaResult<ChangePlanItemPage>> ChangePlanItemsAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, int page, int pageSize,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanItemPage>(principal, context, ServiceAccountCapabilities.View, cancellationToken);

    /// <summary>Creates a Draft plan.</summary>
    public Task<SaResult<ChangePlanView>> CreateChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, CreateChangePlanRequest request,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanView>(principal, context, ServiceAccountCapabilities.Work, cancellationToken);

    /// <summary>Replaces the plan's account list.</summary>
    public Task<SaResult<ChangePlanView>> UpdateChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, UpdateChangePlanRequest request,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanView>(principal, context, ServiceAccountCapabilities.Work, cancellationToken);

    /// <summary>Builds a new preview version.</summary>
    public Task<SaResult<ChangePlanView>> PreviewChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, PreviewChangePlanRequest request,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanView>(principal, context, ServiceAccountCapabilities.Work, cancellationToken);

    /// <summary>Approves the current preview.</summary>
    public Task<SaResult<ChangePlanView>> ApproveChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, ApproveChangePlanRequest request,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanView>(principal, context, ServiceAccountCapabilities.Verify, cancellationToken);

    /// <summary>Cancels an open plan.</summary>
    public Task<SaResult<ChangePlanView>> CancelChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, CancelChangePlanRequest request,
        CancellationToken cancellationToken) => NotInstalledAsync<ChangePlanView>(principal, context, ServiceAccountCapabilities.View, cancellationToken);

    private Task<SaResult<T>> NotInstalledAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, string capability,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, capability, _ => Task.FromResult(SaResult<T>.Fail(SaErrors.ChangePlansNotInstalled)), cancellationToken);
}
