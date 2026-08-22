using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Api.Controllers;

/// <summary>Privileged, exact-input, read-only directory group endpoints.</summary>
[ApiController]
[Route("api/v1/directory")]
[Authorize]
public sealed class DirectoryController : ControllerBase
{
    private readonly IDirectoryGroupQueryService _service;

    /// <summary>Initializes the controller.</summary>
    public DirectoryController(IDirectoryGroupQueryService service) => _service = service;

    /// <summary>Returns one exact principal's direct group memberships.</summary>
    [HttpPost("principals/groups")]
    [Authorize(Policy = Policies.CanViewDirectoryGroups)]
    [EnableRateLimiting(ApiRateLimits.DirectoryGroupQuery)]
    [ProducesResponseType(typeof(DirectoryGroupPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DirectoryGroupPageResponse>> GetPrincipalGroupsAsync(
        [FromBody] DirectoryPrincipalGroupsRequest? request,
        CancellationToken cancellationToken)
    {
        DirectoryQueryResult<DirectoryGroupPageResponse> result = await _service.GetPrincipalGroupsAsync(
            request ?? new DirectoryPrincipalGroupsRequest(null, null), Context(), cancellationToken);
        return Map(result, "The exact directory principal was not found.");
    }

    /// <summary>Returns safe metadata for one exact group.</summary>
    [HttpPost("groups/lookup")]
    [Authorize(Policy = Policies.CanViewDirectoryGroups)]
    [EnableRateLimiting(ApiRateLimits.DirectoryGroupQuery)]
    [ProducesResponseType(typeof(DirectoryGroupDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DirectoryGroupDetailResponse>> GetGroupAsync(
        [FromBody] DirectoryGroupLookupRequest? request,
        CancellationToken cancellationToken)
    {
        DirectoryQueryResult<DirectoryGroupDetailResponse> result = await _service.GetGroupAsync(
            request ?? new DirectoryGroupLookupRequest(null, null), Context(), cancellationToken);
        return Map(result, "The exact directory group was not found.");
    }

    /// <summary>Returns one exact group's direct members without recursive expansion.</summary>
    [HttpPost("groups/members")]
    [Authorize(Policy = Policies.CanViewDirectoryGroupMembers)]
    [EnableRateLimiting(ApiRateLimits.DirectoryGroupMembers)]
    [ProducesResponseType(typeof(DirectoryMemberPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DirectoryMemberPageResponse>> GetGroupMembersAsync(
        [FromBody] DirectoryGroupMembersRequest? request,
        CancellationToken cancellationToken)
    {
        DirectoryQueryResult<DirectoryMemberPageResponse> result = await _service.GetGroupMembersAsync(
            request ?? new DirectoryGroupMembersRequest(null, null), Context(), cancellationToken);
        return Map(result, "The exact directory group was not found.");
    }

    private DirectoryQueryExecutionContext Context() => new(
        User.Identity?.Name ?? "unknown",
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Activity.Current?.Id ?? HttpContext.TraceIdentifier);

    private ActionResult<T> Map<T>(DirectoryQueryResult<T> result, string notFoundDetail) => result.Status switch
    {
        DirectoryQueryStatus.Success => Ok(result.Value),
        DirectoryQueryStatus.NotFound => Problem(StatusCodes.Status404NotFound, result.ErrorCode!, notFoundDetail, "provider", false),
        DirectoryQueryStatus.Invalid => Problem(StatusCodes.Status400BadRequest, OperationalErrorCodes.DirectoryInvalidInput, "The exact directory request was rejected.", "validation", false),
        DirectoryQueryStatus.LimitExceeded => Problem(StatusCodes.Status422UnprocessableEntity, OperationalErrorCodes.DirectoryQueryLimitExceeded, "The bounded directory query exceeded its server-side result ceiling.", "provider", false),
        DirectoryQueryStatus.AuditUnavailable => Problem(StatusCodes.Status503ServiceUnavailable, OperationalErrorCodes.AuditStoreUnavailable, "Required audit storage is unavailable.", "audit", true),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, OperationalErrorCodes.DirectoryProviderUnavailable, "The directory provider is unavailable.", "provider", true)
    };

    private ObjectResult Problem(int status, string code, string detail, string stage, bool retryable) =>
        OperationalProblemDetails.Create(
            status,
            code,
            detail,
            Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            stage,
            retryable);
}
