using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Sessions;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Sessions;

namespace SecureOps.Api.Controllers;

/// <summary>Current and authorized administrative application-session endpoints.</summary>
[ApiController]
[Route("api/v1/sessions")]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public sealed class SessionsController : ControllerBase
{
    private readonly ApplicationSessionContext _sessionContext;
    private readonly IApplicationSessionService _sessionService;
    private readonly ApplicationSessionCookie _cookie;
    private readonly IAccessRepository _accessRepository;

    /// <summary>Initializes the controller.</summary>
    public SessionsController(
        ApplicationSessionContext sessionContext,
        IApplicationSessionService sessionService,
        ApplicationSessionCookie cookie,
        IAccessRepository accessRepository)
    {
        _sessionContext = sessionContext;
        _sessionService = sessionService;
        _cookie = cookie;
        _accessRepository = accessRepository;
    }

    /// <summary>Returns the current authoritative application session.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApplicationSessionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApplicationSessionResponse>> CurrentAsync(CancellationToken cancellationToken)
    {
        ApplicationSession? session = _sessionContext.Current;
        if (session is null)
        {
            return Failure<ApplicationSessionResponse>(OperationalErrorCodes.SessionRevoked);
        }

        ApplicationUser? user = await _accessRepository.GetUserAsync(session.UserId, cancellationToken);
        return Ok(ToResponse(session, user, isCurrent: true));
    }

    /// <summary>Returns a bounded page of active application sessions.</summary>
    [HttpGet("active")]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(ActiveApplicationSessionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ActiveApplicationSessionsResponse>> ActiveAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ApplicationSessionListResult result = await _sessionService.ListActiveAsync(page, pageSize, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<ActiveApplicationSessionsResponse>(result.ErrorCode!);
        }

        var items = new List<ApplicationSessionResponse>(result.Sessions!.Count);
        foreach (ApplicationSession session in result.Sessions)
        {
            ApplicationUser? user = await _accessRepository.GetUserAsync(session.UserId, cancellationToken);
            items.Add(ToResponse(session, user, _sessionContext.Current?.SessionId == session.SessionId));
        }

        return Ok(new ActiveApplicationSessionsResponse(page, pageSize, items));
    }

    /// <summary>Revokes one exact active application session.</summary>
    [HttpPost("revoke")]
    [Authorize(Policy = Policies.CanManageUsers)]
    [ProducesResponseType(typeof(ApplicationSessionEndedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationSessionEndedResponse>> RevokeAsync(
        [FromBody] RevokeApplicationSessionRequest request,
        CancellationToken cancellationToken)
    {
        ApplicationSessionResult result = await _sessionService.RevokeAsync(request.SessionId, request.Reason, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<ApplicationSessionEndedResponse>(result.ErrorCode!);
        }

        ApplicationSession ended = result.Session!;
        if (_sessionContext.Current?.SessionId == ended.SessionId)
        {
            _cookie.Delete(Response);
        }

        return Ok(new ApplicationSessionEndedResponse(ended.SessionId, ended.EndReason!.Value.ToString(), ended.EndedAtUtc!.Value));
    }

    private AccessOperationContext Context() => new(
        User.Identity?.Name ?? "unknown",
        Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        HttpContext.Connection.RemoteIpAddress?.ToString());

    private ActionResult<T> Failure<T>(string code)
    {
        (int status, bool retryable) = code switch
        {
            OperationalErrorCodes.SessionValidationFailed => (StatusCodes.Status400BadRequest, false),
            OperationalErrorCodes.SessionNotFound => (StatusCodes.Status404NotFound, false),
            OperationalErrorCodes.AuditStoreUnavailable or OperationalErrorCodes.SessionStoreUnavailable => (StatusCodes.Status503ServiceUnavailable, true),
            _ => (StatusCodes.Status403Forbidden, false)
        };
        return OperationalProblemDetails.Create(status, code, "The application-session operation could not be completed.", Activity.Current?.Id ?? HttpContext.TraceIdentifier, "session", retryable);
    }

    private static ApplicationSessionResponse ToResponse(
        ApplicationSession session,
        ApplicationUser? user,
        bool isCurrent) => new(
        session.SessionId,
        session.UserId,
        session.StartedAtUtc,
        session.LastSeenAtUtc,
        session.AbsoluteExpiresAtUtc,
        session.AuthenticationMethod,
        session.AccessVersion,
        user?.CorporateIdentity,
        user?.CorporateIdentity,
        DisplayName: null,
        AuthenticationProvider: user?.AuthenticationSource,
        IsCurrent: isCurrent);
}
