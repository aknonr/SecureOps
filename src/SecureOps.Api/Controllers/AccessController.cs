using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureOps.Api.Middleware;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Api.Controllers;

/// <summary>Application access status, approval, role, disable, and logout endpoints.</summary>
[ApiController]
[Route("api/v1/access")]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public sealed class AccessController : ControllerBase
{
    private readonly IApplicationAccessService _accessService;
    private readonly IAuditWriter _auditWriter;
    private readonly SessionSecurityOptions _sessionOptions;
    private readonly ILogger<AccessController> _logger;

    /// <summary>Initializes the controller.</summary>
    public AccessController(
        IApplicationAccessService accessService,
        IAuditWriter auditWriter,
        IOptions<SessionSecurityOptions> sessionOptions,
        ILogger<AccessController> logger)
    {
        _accessService = accessService;
        _auditWriter = auditWriter;
        _sessionOptions = sessionOptions.Value;
        _logger = logger;
    }

    /// <summary>Returns current Pending, Approved, or Disabled application access.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentAccessResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentAccessResponse>> MeAsync(CancellationToken cancellationToken)
    {
        AccessServiceResult<EnsureAccessUserResult> result = await _accessService.GetCurrentAsync(User, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<CurrentAccessResponse>(result.ErrorCode!);
        }

        EnsureAccessUserResult current = result.Value!;
        return Ok(new CurrentAccessResponse(
            current.User.Id,
            current.User.Status.ToString(),
            current.User.Roles,
            current.User.Capabilities,
            current.PendingRequest?.Id,
            current.User.AuthenticationSource,
            new SessionPolicyResponse(
                _sessionOptions.IdleTimeoutMinutes,
                _sessionOptions.AbsoluteLifetimeHours,
                _sessionOptions.SecureCookie,
                _sessionOptions.HttpOnly,
                _sessionOptions.SameSite,
                _sessionOptions.RevalidateAccessOnEveryRequest,
                "Authentication-provider managed; application access revalidated per request")));
    }

    /// <summary>Lists pending or decided access requests for authorized administrators.</summary>
    [HttpGet("requests")]
    [Authorize(Policy = Policies.CanApproveAccessRequests)]
    [ProducesResponseType(typeof(IReadOnlyList<AccessRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccessRequestResponse>>> RequestsAsync([FromQuery] string? status, CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out AccessRequestStatus? parsed))
        {
            return Failure<IReadOnlyList<AccessRequestResponse>>(OperationalErrorCodes.AccessRequestInvalidState);
        }

        AccessServiceResult<IReadOnlyList<ApplicationAccessRequest>> result = await _accessService.ListRequestsAsync(parsed, Context(), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!.Select(ToResponse).ToArray())
            : Failure<IReadOnlyList<AccessRequestResponse>>(result.ErrorCode!);
    }

    /// <summary>Approves one pending request and assigns reviewed roles.</summary>
    [HttpPost("requests/{id:guid}/approve")]
    [Authorize(Policy = Policies.CanApproveAccessRequests)]
    [ProducesResponseType(typeof(AccessRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccessRequestResponse>> ApproveAsync(Guid id, [FromBody] AccessDecisionRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.ApproveAsync(id, request.Roles ?? [], request.Reason, Context(), cancellationToken);
        return MutationResponse(result);
    }

    /// <summary>Rejects one pending request without assigning access.</summary>
    [HttpPost("requests/{id:guid}/reject")]
    [Authorize(Policy = Policies.CanApproveAccessRequests)]
    [ProducesResponseType(typeof(AccessRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccessRequestResponse>> RejectAsync(Guid id, [FromBody] AccessDecisionRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.RejectAsync(id, request.Reason, Context(), cancellationToken);
        return MutationResponse(result);
    }

    /// <summary>Replaces active application roles for one approved user.</summary>
    [HttpPut("users/{id:guid}/roles")]
    [Authorize(Policy = Policies.CanAssignRoles)]
    [ProducesResponseType(typeof(CurrentAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrentAccessResponse>> ReplaceRolesAsync(Guid id, [FromBody] AssignRolesRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.ReplaceRolesAsync(id, request.Roles, request.Reason, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<CurrentAccessResponse>(result.ErrorCode!);
        }

        ApplicationUser user = result.Value!.User!;
        return Ok(ToCurrent(user));
    }

    /// <summary>Disables application access and revokes active roles immediately.</summary>
    [HttpPost("users/{id:guid}/disable")]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(CurrentAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrentAccessResponse>> DisableAsync(Guid id, [FromBody] DisableAccessRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.DisableAsync(id, request.Reason, Context(), cancellationToken);
        return result.IsSuccess ? Ok(ToCurrent(result.Value!.User!)) : Failure<CurrentAccessResponse>(result.ErrorCode!);
    }

    /// <summary>Records logout intent; Windows authentication remains browser/host managed.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(LogoutResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LogoutResponse>> LogoutAsync(CancellationToken cancellationToken)
    {
        string correlationId = CorrelationId();
        try
        {
            await _auditWriter.WriteAsync(new AuditEvent
            {
                Actor = User.Identity?.Name ?? "unknown",
                Action = AuditActions.SessionLogoutRequested,
                CorrelationId = correlationId,
                SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Details = new { authenticationSource = User.FindFirst("secureops:auth_source")?.Value ?? User.Identity?.AuthenticationType }
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Logout audit failed. CorrelationId: {CorrelationId}", correlationId);
            return Failure<LogoutResponse>(OperationalErrorCodes.AuditStoreUnavailable);
        }

        return Ok(new LogoutResponse("ProviderManaged", User.FindFirst("secureops:auth_source")?.Value ?? User.Identity?.AuthenticationType ?? "unknown"));
    }

    private ActionResult<AccessRequestResponse> MutationResponse(AccessServiceResult<AccessMutationResult> result) =>
        result.IsSuccess ? Ok(ToResponse(result.Value!.Request!)) : Failure<AccessRequestResponse>(result.ErrorCode!);

    private AccessOperationContext Context() => new(User.Identity?.Name ?? "unknown", CorrelationId(), HttpContext.Connection.RemoteIpAddress?.ToString());
    private string CorrelationId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private ActionResult<T> Failure<T>(string code)
    {
        int status = code switch
        {
            OperationalErrorCodes.AccessRecordNotFound => StatusCodes.Status404NotFound,
            OperationalErrorCodes.AuditStoreUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status409Conflict
        };
        return OperationalProblemDetails.Create(status, code, "The access operation could not be completed.", CorrelationId(), "access", false);
    }

    private CurrentAccessResponse ToCurrent(ApplicationUser user) => new(
        user.Id,
        user.Status.ToString(),
        user.Roles,
        user.Capabilities,
        null,
        user.AuthenticationSource,
        new SessionPolicyResponse(_sessionOptions.IdleTimeoutMinutes, _sessionOptions.AbsoluteLifetimeHours, _sessionOptions.SecureCookie, _sessionOptions.HttpOnly, _sessionOptions.SameSite, _sessionOptions.RevalidateAccessOnEveryRequest, "Authentication-provider managed; application access revalidated per request"));

    private static AccessRequestResponse ToResponse(ApplicationAccessRequest request) => new(request.Id, request.UserId, request.CorporateIdentity, request.Status.ToString(), request.RequestedAt, request.DecidedAt, request.DecisionReason);

    private static bool TryParseStatus(string? status, out AccessRequestStatus? result)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            result = AccessRequestStatus.Pending;
            return true;
        }

        bool parsed = Enum.TryParse(status, true, out AccessRequestStatus value);
        result = parsed ? value : null;
        return parsed;
    }
}
