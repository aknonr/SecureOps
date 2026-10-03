using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Api.Controllers;

public sealed partial class AccessController
{
    /// <summary>Reads a SQL-bounded user page with persisted identity labels.</summary>
    [HttpGet("users/page")]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(AccessPage<AccessUserResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccessPage<AccessUserResponse>>> UsersPageAsync([FromQuery] AccessPageQuery query, [FromServices] IAccessRepository repository, [FromServices] IAuditWriter audit, CancellationToken cancellationToken)
    {
        if (repository is not SqlAccessRepository sql)
        {
            return StatusCode(503);
        }

        if (!await AuditPageAsync(audit, AuditActions.AccessUsersViewed, cancellationToken))
        { return Failure<AccessPage<AccessUserResponse>>(OperationalErrorCodes.AuditStoreUnavailable); }
        try
        { return Ok(await sql.PageUsersAsync(query, cancellationToken)); }
        catch (ArgumentException) { return Failure<AccessPage<AccessUserResponse>>(OperationalErrorCodes.AccessValidationFailed); }
    }

    /// <summary>Reads a SQL-bounded request page without treating rejected requests as pending requests.</summary>
    [HttpGet("requests/page")]
    [Authorize(Policy = Policies.CanApproveAccessRequests)]
    [ProducesResponseType(typeof(AccessPage<AccessRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccessPage<AccessRequestResponse>>> RequestsPageAsync([FromQuery] AccessPageQuery query, [FromServices] IAccessRepository repository, [FromServices] IAuditWriter audit, CancellationToken cancellationToken)
    {
        if (repository is not SqlAccessRepository sql)
        {
            return StatusCode(503);
        }

        if (!await AuditPageAsync(audit, AuditActions.AccessRequestsViewed, cancellationToken))
        { return Failure<AccessPage<AccessRequestResponse>>(OperationalErrorCodes.AuditStoreUnavailable); }
        try
        { return Ok(await sql.PageRequestsAsync(query, cancellationToken)); }
        catch (ArgumentException) { return Failure<AccessPage<AccessRequestResponse>>(OperationalErrorCodes.AccessValidationFailed); }
    }

    /// <summary>Returns server-owned definitions for authorized access decisions.</summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(IReadOnlyList<AccessRoleDefinition>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccessRoleDefinition>>> RolesAsync([FromServices] IAccessRepository repository, CancellationToken cancellationToken)
    {
        if (!await IsAccessAdministratorAsync(cancellationToken))
        {
            return Failure<IReadOnlyList<AccessRoleDefinition>>(OperationalErrorCodes.AccessDenied);
        }

        return Ok(await repository.GetRoleDefinitionsAsync(cancellationToken));
    }

    /// <summary>Read-only module view: every registered action and the business roles that grant it.</summary>
    [HttpGet("modules")]
    [ProducesResponseType(typeof(AccessModuleOverviewResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccessModuleOverviewResponse>> ModulesAsync([FromServices] IAccessRepository repository, CancellationToken cancellationToken)
    {
        if (!await IsAccessAdministratorAsync(cancellationToken))
        {
            return Failure<AccessModuleOverviewResponse>(OperationalErrorCodes.AccessDenied);
        }

        return Ok(AccessModuleView.Overview(AccessActionCatalog.Actions, await repository.GetRoleDefinitionsAsync(cancellationToken)));
    }

    /// <summary>Explains, per module, what one user can do and through which assigned roles.</summary>
    [HttpGet("users/{id:guid}/effective")]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(AccessEffectiveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccessEffectiveResponse>> EffectiveAsync(Guid id, [FromServices] IAccessRepository repository, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessUserReadModel> result = await _accessService.GetUserAsync(id, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<AccessEffectiveResponse>(result.ErrorCode!);
        }

        return Ok(AccessModuleView.Explain(result.Value!.User, AccessActionCatalog.Actions, await repository.GetRoleDefinitionsAsync(cancellationToken)));
    }

    /// <summary>Explains the caller's own effective access; any authenticated principal may read their own.</summary>
    [HttpGet("me/effective")]
    [ProducesResponseType(typeof(AccessEffectiveResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccessEffectiveResponse>> MyEffectiveAsync([FromServices] IAccessRepository repository, CancellationToken cancellationToken)
    {
        AccessServiceResult<EnsureAccessUserResult> result = await _accessService.GetCurrentAsync(User, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<AccessEffectiveResponse>(result.ErrorCode!);
        }

        return Ok(AccessModuleView.Explain(result.Value!.User, AccessActionCatalog.Actions, await repository.GetRoleDefinitionsAsync(cancellationToken)));
    }

    // Same gate as the role definitions read: an Approved user holding any access-administration capability.
    private async Task<bool> IsAccessAdministratorAsync(CancellationToken cancellationToken)
    {
        AccessServiceResult<EnsureAccessUserResult> result = await _accessService.GetCurrentAsync(User, Context(), cancellationToken);
        return result.IsSuccess && result.Value!.User.Status == AccessStatus.Approved && result.Value.User.Capabilities.Any(capability =>
            capability is Capabilities.AccessManageUsers or Capabilities.AccessAssignRoles or Capabilities.AccessApproveRequests);
    }

    /// <summary>Returns registered action descriptions; arbitrary permission strings cannot be registered.</summary>
    [HttpGet("roles/actions")]
    [Authorize(Policy = Policies.CanAssignRoles)]
    [ProducesResponseType(typeof(IReadOnlyList<AccessActionDefinition>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AccessActionDefinition>> RoleActions() => Ok(AccessActionCatalog.Actions);

    /// <summary>Previews the current effective impact of a proposed definition.</summary>
    [HttpPost("roles/preview")]
    [EnableRateLimiting(ApiRateLimits.AccessAdministration)]
    [Authorize(Policy = Policies.CanManageUsers)]
    [Authorize(Policy = Policies.CanAssignRoles)]
    [ProducesResponseType(typeof(AccessRoleImpact), StatusCodes.Status200OK)]
    public Task<ActionResult<AccessRoleImpact>> RolePreviewAsync([FromBody] AccessRoleChange change, [FromServices] IAccessRepository repository, CancellationToken cancellationToken) =>
        RoleChangeAsync(change, repository, false, cancellationToken);

    /// <summary>Applies only the exact reviewed definition/assignment impact.</summary>
    [HttpPut("roles")]
    [EnableRateLimiting(ApiRateLimits.AccessAdministration)]
    [Authorize(Policy = Policies.CanManageUsers)]
    [Authorize(Policy = Policies.CanAssignRoles)]
    [ProducesResponseType(typeof(AccessRoleImpact), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<AccessRoleImpact>> RoleSaveAsync([FromBody] AccessRoleChange change, [FromServices] IAccessRepository repository, CancellationToken cancellationToken) =>
        RoleChangeAsync(change, repository, true, cancellationToken);

    private async Task<bool> AuditPageAsync(IAuditWriter audit, string action, CancellationToken token)
    {
        AccessOperationContext context = Context();
        try
        { await audit.WriteAsync(new() { Actor = context.Actor, Action = action, CorrelationId = context.CorrelationId, SourceIp = context.SourceIp }, token); return true; }
        catch (Exception exception) when (exception is not OperationCanceledException) { return false; }
    }

    private async Task<ActionResult<AccessRoleImpact>> RoleChangeAsync(AccessRoleChange change, IAccessRepository repository, bool apply, CancellationToken cancellationToken)
    {
        if (repository is not SqlAccessRepository sql)
        {
            return StatusCode(503);
        }

        AccessServiceResult<AccessRoleImpact> result = await sql.ChangeRoleAsync(change, Context().Actor, apply, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure<AccessRoleImpact>(result.ErrorCode!);
    }
}
