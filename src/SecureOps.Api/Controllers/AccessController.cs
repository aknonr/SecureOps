using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Sessions;
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
public sealed partial class AccessController : ControllerBase
{
    private readonly IApplicationAccessService _accessService;
    private readonly IApplicationSessionService _sessionService;
    private readonly ApplicationSessionContext _sessionContext;
    private readonly ApplicationSessionCookie _sessionCookie;
    private readonly SessionSecurityOptions _sessionOptions;
    private readonly ILogger<AccessController> _logger;
    private readonly ICorporatePrincipalResolver _principalResolver;

    /// <summary>Initializes the controller.</summary>
    public AccessController(
        IApplicationAccessService accessService,
        IApplicationSessionService sessionService,
        ApplicationSessionContext sessionContext,
        ApplicationSessionCookie sessionCookie,
        IOptions<SessionSecurityOptions> sessionOptions,
        ILogger<AccessController> logger,
        ICorporatePrincipalResolver principalResolver)
    {
        _accessService = accessService;
        _sessionService = sessionService;
        _sessionContext = sessionContext;
        _sessionCookie = sessionCookie;
        _sessionOptions = sessionOptions.Value;
        _logger = logger;
        _principalResolver = principalResolver;
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
        AccessIdentityProfile? profile = await _accessService.GetProfileAsync(current.User, cancellationToken);
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
                "Server-side application session; corporate authentication provider remains independent")
            {
                ActivityPersistenceIntervalMinutes = _sessionOptions.ActivityPersistenceIntervalMinutes
            },
            ToResponse(profile),
            current.LatestRequest is null ? null : ToResponse(current.LatestRequest, profile),
            current.User.Version));
    }

    /// <summary>Lists pending or decided access requests for authorized administrators.</summary>
    [HttpGet("requests")]
    [Authorize(Policy = Policies.CanApproveAccessRequests)]
    [ProducesResponseType(typeof(IReadOnlyList<AccessRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccessRequestResponse>>> RequestsAsync([FromQuery] string? status, CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out AccessRequestStatus? parsed))
        {
            return Failure<IReadOnlyList<AccessRequestResponse>>(OperationalErrorCodes.AccessValidationFailed);
        }

        AccessServiceResult<IReadOnlyList<AccessRequestReadModel>> result = await _accessService.ListRequestsAsync(parsed, Context(), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!.Select(item => ToResponse(item.Request, item.Profile)).ToArray())
            : Failure<IReadOnlyList<AccessRequestResponse>>(result.ErrorCode!);
    }

    /// <summary>Lists authoritative access-user state for authorized administrators.</summary>
    [HttpGet("users")]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(IReadOnlyList<AccessUserResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccessUserResponse>>> UsersAsync(CancellationToken cancellationToken)
    {
        AccessServiceResult<IReadOnlyList<AccessUserReadModel>> result = await _accessService.ListUsersAsync(Context(), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!.Select(ToResponse).ToArray())
            : Failure<IReadOnlyList<AccessUserResponse>>(result.ErrorCode!);
    }

    /// <summary>Returns one authoritative access-user record and request history.</summary>
    [HttpGet("users/{id:guid}")]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(AccessUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccessUserResponse>> UserAsync(Guid id, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessUserReadModel> result = await _accessService.GetUserAsync(id, Context(), cancellationToken);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : Failure<AccessUserResponse>(result.ErrorCode!);
    }

    /// <summary>Approves one pending request and assigns reviewed roles.</summary>
    [HttpPost("requests/{id:guid}/approve")]
    [EnableRateLimiting(ApiRateLimits.AccessAdministration)]
    [Authorize(Policy = Policies.CanApproveAccessRequests)]
    [ProducesResponseType(typeof(AccessRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccessRequestResponse>> ApproveAsync(Guid id, [FromBody] AccessDecisionRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.ApproveAsync(id, request.Roles ?? [], request.Reason, request.ExpectedVersion, Context(), cancellationToken, request.RoleVersions);
        return MutationResponse(result);
    }

    /// <summary>Rejects one pending request without assigning access.</summary>
    [HttpPost("requests/{id:guid}/reject")]
    [EnableRateLimiting(ApiRateLimits.AccessAdministration)]
    [Authorize(Policy = Policies.CanApproveAccessRequests)]
    [ProducesResponseType(typeof(AccessRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccessRequestResponse>> RejectAsync(Guid id, [FromBody] AccessDecisionRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.RejectAsync(id, request.Reason, request.ExpectedVersion, Context(), cancellationToken);
        return MutationResponse(result);
    }

    /// <summary>Replaces active application roles for one approved user.</summary>
    [HttpPut("users/{id:guid}/roles")]
    [EnableRateLimiting(ApiRateLimits.AccessAdministration)]
    [Authorize(Policy = Policies.CanAssignRoles)]
    [ProducesResponseType(typeof(CurrentAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrentAccessResponse>> ReplaceRolesAsync(Guid id, [FromBody] AssignRolesRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.ReplaceRolesAsync(id, request.Roles, request.ExpectedVersion, Context(), cancellationToken, request.RoleVersions);
        if (!result.IsSuccess)
        {
            return Failure<CurrentAccessResponse>(result.ErrorCode!);
        }

        ApplicationUser user = result.Value!.User!;
        ApplicationSessionTerminationResult sessionsEnded = await _sessionService.EndUserSessionsAsync(user.Id, SessionEndReason.AccessChanged, Context(), cancellationToken);
        if (!sessionsEnded.IsSuccess)
        {
            _logger.LogError("Access roles changed but immediate session lifecycle audit did not complete. UserId: {UserId}. CorrelationId: {CorrelationId}", user.Id, CorrelationId());
            return Failure<CurrentAccessResponse>(sessionsEnded.ErrorCode!);
        }

        return Ok(ToCurrent(user));
    }

    /// <summary>Disables application access and revokes active roles immediately.</summary>
    [HttpPost("users/{id:guid}/disable")]
    [EnableRateLimiting(ApiRateLimits.AccessAdministration)]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(CurrentAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrentAccessResponse>> DisableAsync(Guid id, [FromBody] DisableAccessRequest request, CancellationToken cancellationToken)
    {
        AccessServiceResult<AccessMutationResult> result = await _accessService.DisableAsync(id, request.Reason, request.ExpectedVersion, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<CurrentAccessResponse>(result.ErrorCode!);
        }

        ApplicationUser user = result.Value!.User!;
        ApplicationSessionTerminationResult sessionsEnded = await _sessionService.EndUserSessionsAsync(user.Id, SessionEndReason.AccessDisabled, Context(), cancellationToken);
        if (!sessionsEnded.IsSuccess)
        {
            _logger.LogError("Access was disabled but immediate session lifecycle audit did not complete. UserId: {UserId}. CorrelationId: {CorrelationId}", user.Id, CorrelationId());
            return Failure<CurrentAccessResponse>(sessionsEnded.ErrorCode!);
        }

        return Ok(ToCurrent(user));
    }

    /// <summary>Ends the current SecureOps session; Windows authentication remains browser/host managed.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(LogoutResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LogoutResponse>> LogoutAsync(CancellationToken cancellationToken)
    {
        if (_sessionContext.Current is null)
        {
            _sessionCookie.Delete(Response);
            return Failure<LogoutResponse>(OperationalErrorCodes.SessionRevoked);
        }

        ApplicationSessionResult result = await _sessionService.LogoutAsync(_sessionContext.Current.SessionId, Context(), cancellationToken);
        _sessionCookie.Delete(Response);
        if (!result.IsSuccess)
        {
            return Failure<LogoutResponse>(result.ErrorCode!);
        }

        return Ok(new LogoutResponse("ApplicationSessionEnded;AuthenticationProviderManaged", User.FindFirst("secureops:auth_source")?.Value ?? User.Identity?.AuthenticationType ?? "unknown"));
    }

    private ActionResult<AccessRequestResponse> MutationResponse(AccessServiceResult<AccessMutationResult> result) =>
        result.IsSuccess ? Ok(ToResponse(result.Value!.Request!)) : Failure<AccessRequestResponse>(result.ErrorCode!);

    private AccessOperationContext Context() => new(_principalResolver.Resolve(User)?.Identifier ?? "",
        CorrelationId(), HttpContext.Connection.RemoteIpAddress?.ToString());
    private string CorrelationId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private ActionResult<T> Failure<T>(string code)
    {
        (int Status, string Stage, bool Retryable) details = code switch
        {
            OperationalErrorCodes.AccessValidationFailed => (StatusCodes.Status400BadRequest, "validation", false),
            OperationalErrorCodes.AccessRecordNotFound => (StatusCodes.Status404NotFound, "access", false),
            OperationalErrorCodes.AccessSelfApprovalDenied => (StatusCodes.Status403Forbidden, "authorization", false),
            OperationalErrorCodes.AccessProtectedRole or OperationalErrorCodes.AccessSelfEscalationDenied or OperationalErrorCodes.AccessLastAdministratorDenied
                => (StatusCodes.Status403Forbidden, "administrative-guard", false),
            OperationalErrorCodes.AccessDenied => (StatusCodes.Status403Forbidden, "authorization", false),
            OperationalErrorCodes.AccessRequestAlreadyDecided => (StatusCodes.Status409Conflict, "lifecycle", false),
            OperationalErrorCodes.AccessUserInvalidState => (StatusCodes.Status409Conflict, "lifecycle", false),
            OperationalErrorCodes.AccessConcurrencyConflict => (StatusCodes.Status409Conflict, "concurrency", true),
            OperationalErrorCodes.AuditStoreUnavailable => (StatusCodes.Status503ServiceUnavailable, "audit", true),
            OperationalErrorCodes.PersistenceUnavailable => (StatusCodes.Status503ServiceUnavailable, "persistence", true),
            OperationalErrorCodes.SessionStoreUnavailable => (StatusCodes.Status503ServiceUnavailable, "session-store", true),
            OperationalErrorCodes.SessionExpired => (StatusCodes.Status403Forbidden, "session", false),
            OperationalErrorCodes.SessionRevoked => (StatusCodes.Status403Forbidden, "session", false),
            _ => (StatusCodes.Status409Conflict, "access", false)
        };
        return OperationalProblemDetails.Create(details.Status, code, "The access operation could not be completed.", CorrelationId(), details.Stage, details.Retryable);
    }

    private CurrentAccessResponse ToCurrent(ApplicationUser user) => new(
        user.Id,
        user.Status.ToString(),
        user.Roles,
        user.Capabilities,
        null,
        user.AuthenticationSource,
        new SessionPolicyResponse(_sessionOptions.IdleTimeoutMinutes, _sessionOptions.AbsoluteLifetimeHours, _sessionOptions.SecureCookie, _sessionOptions.HttpOnly, _sessionOptions.SameSite, _sessionOptions.RevalidateAccessOnEveryRequest, "Server-side application session; corporate authentication provider remains independent")
        {
            ActivityPersistenceIntervalMinutes = _sessionOptions.ActivityPersistenceIntervalMinutes
        },
        Version: user.Version);

    private static AccessRequestResponse ToResponse(ApplicationAccessRequest request, AccessIdentityProfile? profile = null) => new(
        request.Id,
        request.UserId,
        request.CorporateIdentity,
        request.Status.ToString(),
        request.RequestedAt,
        request.DecidedAt,
        request.DecisionReason,
        request.DecidedByCorporateIdentity,
        request.Version,
        ToResponse(profile));

    private static AccessIdentityProfileResponse? ToResponse(AccessIdentityProfile? profile) => profile is null
        ? null
        : new AccessIdentityProfileResponse(profile.DisplayName, profile.Account, profile.Email, profile.Department, profile.Title, profile.Uid);

    private static AccessUserResponse ToResponse(AccessUserReadModel model) => new(
        model.User.Id,
        model.User.CorporateIdentity,
        ToResponse(model.Profile),
        model.User.Status.ToString(),
        model.User.Roles,
        model.User.Capabilities,
        model.LatestRequest is null ? null : ToResponse(model.LatestRequest, model.Profile),
        model.RequestHistory.Select(request => ToResponse(request, model.Profile)).ToArray(),
        model.User.Version,
        model.User.AuthenticationSource,
        model.User.FirstAuthenticatedAt,
        model.User.LastAuthenticatedAt,
        model.User.DisabledAt);

    private static bool TryParseStatus(string? status, out AccessRequestStatus? result)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            result = AccessRequestStatus.Pending;
            return true;
        }

        bool parsed = Enum.TryParse(status, true, out AccessRequestStatus value) && Enum.IsDefined(value);
        result = parsed ? value : null;
        return parsed;
    }
}
