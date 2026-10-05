using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.Sessions;

/// <summary>Authoritative server-side application-session lifecycle service.</summary>
public sealed class ApplicationSessionService : IApplicationSessionService
{
    private readonly IApplicationAccessService _accessService;
    private readonly IApplicationSessionRepository _repository;
    private readonly IAuditWriter _auditWriter;
    private readonly SessionSecurityOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApplicationSessionService> _logger;

    /// <summary>Initializes the session service.</summary>
    public ApplicationSessionService(
        IApplicationAccessService accessService,
        IApplicationSessionRepository repository,
        IAuditWriter auditWriter,
        IOptions<SessionSecurityOptions> options,
        TimeProvider timeProvider,
        ILogger<ApplicationSessionService> logger)
    {
        _accessService = accessService;
        _repository = repository;
        _auditWriter = auditWriter;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ApplicationSessionResult> ValidateOrStartAsync(
        ClaimsPrincipal principal,
        Guid? presentedSessionId,
        AccessOperationContext context,
        CancellationToken cancellationToken)
    {
        AccessServiceResult<EnsureAccessUserResult> access;
        try
        {
            access = await _accessService.GetCurrentAsync(principal, context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Application access reconciliation failed during session validation. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.StoreUnavailable, OperationalErrorCodes.SessionStoreUnavailable);
        }

        if (!access.IsSuccess)
        {
            return Failure(
                access.ErrorCode == OperationalErrorCodes.AuditStoreUnavailable ? ApplicationSessionDisposition.AuditUnavailable : ApplicationSessionDisposition.AccessDenied,
                access.ErrorCode!);
        }

        ApplicationUser user = access.Value!.User;
        if (user.Status == AccessStatus.Disabled)
        {
            _ = await EndUserSessionsAsync(user.Id, SessionEndReason.AccessDisabled, context, cancellationToken);
            return Failure(ApplicationSessionDisposition.AccessDisabled, OperationalErrorCodes.AccessDisabled);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (presentedSessionId is null)
        {
            return await StartAsync(user, AuthenticationMethod(principal), now, context, cancellationToken);
        }

        try
        {
            ApplicationSession? session = await _repository.GetAsync(presentedSessionId.Value, cancellationToken);
            if (session is null || session.UserId != user.Id)
            {
                return Failure(ApplicationSessionDisposition.Revoked, OperationalErrorCodes.SessionRevoked);
            }

            if (!session.IsActive)
            {
                return EndedFailure(session);
            }

            if (session.AccessVersion != user.Version)
            {
                return await EndForValidationAsync(session, SessionEndReason.AccessChanged, ApplicationSessionDisposition.AccessChanged, OperationalErrorCodes.SessionRevoked, now, context, cancellationToken);
            }

            if (now >= session.AbsoluteExpiresAtUtc)
            {
                return await EndForValidationAsync(session, SessionEndReason.AbsoluteTimeout, ApplicationSessionDisposition.AbsoluteExpired, OperationalErrorCodes.SessionExpired, now, context, cancellationToken);
            }

            if (now - session.LastSeenAtUtc >= TimeSpan.FromMinutes(_options.IdleTimeoutMinutes))
            {
                return await EndForValidationAsync(session, SessionEndReason.IdleTimeout, ApplicationSessionDisposition.IdleExpired, OperationalErrorCodes.SessionExpired, now, context, cancellationToken);
            }

            var persistenceInterval = TimeSpan.FromMinutes(_options.ActivityPersistenceIntervalMinutes);
            if (now - session.LastSeenAtUtc >= persistenceInterval
                && await _repository.TouchAsync(session.SessionId, now, now - persistenceInterval, cancellationToken))
            {
                session = session with { LastSeenAtUtc = now };
            }

            return new ApplicationSessionResult(ApplicationSessionDisposition.Active, session, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Application session validation failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.StoreUnavailable, OperationalErrorCodes.SessionStoreUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<ApplicationSessionResult> LogoutAsync(Guid sessionId, AccessOperationContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            ApplicationSession? session = await _repository.GetAsync(sessionId, cancellationToken);
            if (session is null || !session.IsActive)
            {
                return Failure(ApplicationSessionDisposition.Revoked, OperationalErrorCodes.SessionRevoked);
            }

            _ = await _repository.EndWithAuditAsync(sessionId, now, SessionEndReason.Logout,
                CreateAudit(AuditActions.ApplicationSessionLoggedOut, session, SessionEndReason.Logout, context, null), cancellationToken);

            return new ApplicationSessionResult(ApplicationSessionDisposition.Ended, session with { EndedAtUtc = now, EndReason = SessionEndReason.Logout }, null);
        }
        catch (AuditWriteUnavailableException)
        {
            _logger.LogError("Application session logout audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.AuditUnavailable, OperationalErrorCodes.AuditStoreUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Application session logout failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.StoreUnavailable, OperationalErrorCodes.SessionStoreUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<ApplicationSessionListResult> ListActiveAsync(int page, int pageSize, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize < 1 || pageSize > _options.MaxAdminPageSize)
        {
            return new ApplicationSessionListResult(null, OperationalErrorCodes.SessionValidationFailed);
        }

        if (!await TryAuditAsync(AuditActions.ApplicationSessionsViewed, null, null, context, null, cancellationToken))
        {
            return new ApplicationSessionListResult(null, OperationalErrorCodes.AuditStoreUnavailable);
        }

        try
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            _ = await _repository.EndExpiredAsync(
                now,
                now.AddMinutes(-_options.IdleTimeoutMinutes),
                _options.MaxAdminPageSize,
                session => CreateAudit(ExpiryAction(session), session, session.EndReason, context, null),
                cancellationToken);

            IReadOnlyList<ApplicationSession> sessions = await _repository.ListActiveAsync(
                now,
                now.AddMinutes(-_options.IdleTimeoutMinutes),
                checked((page - 1) * pageSize),
                pageSize,
                cancellationToken);
            return new ApplicationSessionListResult(sessions, null);
        }
        catch (AuditWriteUnavailableException)
        {
            _logger.LogError("Application session expiry audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return new ApplicationSessionListResult(null, OperationalErrorCodes.AuditStoreUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Active application-session listing failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return new ApplicationSessionListResult(null, OperationalErrorCodes.SessionStoreUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<ApplicationSessionResult> RevokeAsync(Guid sessionId, string reason, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 || reason.Any(char.IsControl))
        {
            return Failure(ApplicationSessionDisposition.Invalid, OperationalErrorCodes.SessionValidationFailed);
        }

        try
        {
            ApplicationSession? session = await _repository.GetAsync(sessionId, cancellationToken);
            if (session is null || !session.IsActive)
            {
                return Failure(ApplicationSessionDisposition.Revoked, OperationalErrorCodes.SessionNotFound);
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            _ = await _repository.EndWithAuditAsync(sessionId, now, SessionEndReason.Revoked,
                CreateAudit(AuditActions.ApplicationSessionRevoked, session, SessionEndReason.Revoked, context, reason.Trim()), cancellationToken);

            return new ApplicationSessionResult(ApplicationSessionDisposition.Ended, session with { EndedAtUtc = now, EndReason = SessionEndReason.Revoked }, null);
        }
        catch (AuditWriteUnavailableException)
        {
            _logger.LogError("Application session revocation audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.AuditUnavailable, OperationalErrorCodes.AuditStoreUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Application session revocation failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.StoreUnavailable, OperationalErrorCodes.SessionStoreUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<ApplicationSessionTerminationResult> EndUserSessionsAsync(Guid userId, SessionEndReason reason, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (reason is not SessionEndReason.AccessDisabled and not SessionEndReason.AccessChanged)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        try
        {
            string action = reason == SessionEndReason.AccessDisabled
                ? AuditActions.ApplicationSessionAccessDisabled
                : AuditActions.ApplicationSessionAccessChanged;
            IReadOnlyList<ApplicationSession> sessions = await _repository.EndActiveForUserAsync(userId, _timeProvider.GetUtcNow(), reason,
                session => CreateAudit(action, session, reason, context, null), cancellationToken);

            return new ApplicationSessionTerminationResult(sessions.Count, null);
        }
        catch (AuditWriteUnavailableException)
        {
            _logger.LogError("Application user session termination audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return new ApplicationSessionTerminationResult(0, OperationalErrorCodes.AuditStoreUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Application user session termination failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return new ApplicationSessionTerminationResult(0, OperationalErrorCodes.SessionStoreUnavailable);
        }
    }

    private async Task<ApplicationSessionResult> StartAsync(
        ApplicationUser user,
        string authenticationMethod,
        DateTimeOffset now,
        AccessOperationContext context,
        CancellationToken cancellationToken)
    {
        ApplicationSession session = new(
            CryptographicSessionId(),
            user.Id,
            now,
            now,
            now.AddHours(_options.AbsoluteLifetimeHours),
            null,
            null,
            authenticationMethod,
            user.Version);
        try
        {
            await _repository.InsertAsync(session, cancellationToken);
            if (!await TryAuditAsync(AuditActions.ApplicationSessionStarted, session, null, context, null, cancellationToken))
            {
                await _repository.EndAsync(session.SessionId, now, SessionEndReason.AuditFailure, cancellationToken);
                return Failure(ApplicationSessionDisposition.AuditUnavailable, OperationalErrorCodes.AuditStoreUnavailable);
            }

            return new ApplicationSessionResult(ApplicationSessionDisposition.Started, session, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Application session start failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.StoreUnavailable, OperationalErrorCodes.SessionStoreUnavailable);
        }
    }

    private async Task<ApplicationSessionResult> EndForValidationAsync(
        ApplicationSession session,
        SessionEndReason reason,
        ApplicationSessionDisposition disposition,
        string errorCode,
        DateTimeOffset now,
        AccessOperationContext context,
        CancellationToken cancellationToken)
    {
        string action = reason switch
        {
            SessionEndReason.IdleTimeout => AuditActions.ApplicationSessionIdleTimedOut,
            SessionEndReason.AbsoluteTimeout => AuditActions.ApplicationSessionAbsoluteTimedOut,
            SessionEndReason.AccessChanged => AuditActions.ApplicationSessionAccessChanged,
            _ => throw new InvalidOperationException("Unsupported validation end reason.")
        };
        try
        {
            _ = await _repository.EndWithAuditAsync(session.SessionId, now, reason, CreateAudit(action, session, reason, context, null), cancellationToken);
        }
        catch (AuditWriteUnavailableException)
        {
            _logger.LogError("Application session validation audit failed. CorrelationId: {CorrelationId}", context.CorrelationId);
            return Failure(ApplicationSessionDisposition.AuditUnavailable, OperationalErrorCodes.AuditStoreUnavailable);
        }

        return Failure(disposition, errorCode, session with { EndedAtUtc = now, EndReason = reason });
    }

    private async Task<bool> TryAuditAsync(
        string action,
        ApplicationSession? session,
        SessionEndReason? reason,
        AccessOperationContext context,
        string? operationalReason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditWriter.WriteAsync(CreateAudit(action, session, reason, context, operationalReason), cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Application session audit failed. Action: {Action}. CorrelationId: {CorrelationId}", action, context.CorrelationId);
            return false;
        }
    }

    private AuditEvent CreateAudit(string action, ApplicationSession? session, SessionEndReason? reason, AccessOperationContext context, string? operationalReason) => new()
    {
        OccurredAt = _timeProvider.GetUtcNow(),
        Actor = context.Actor,
        Action = action,
        CorrelationId = context.CorrelationId,
        Details = new
        {
            sessionId = session?.SessionId,
            userId = session?.UserId,
            endReason = reason?.ToString(),
            authenticationMethod = session?.AuthenticationMethod,
            accessVersion = session?.AccessVersion,
            reasonHash = operationalReason is null ? null : AuditAccountHasher.HashAccountInput(operationalReason),
            reasonLength = operationalReason?.Length
        }
    };

    private static string ExpiryAction(ApplicationSession session) => session.EndReason == SessionEndReason.AbsoluteTimeout
        ? AuditActions.ApplicationSessionAbsoluteTimedOut
        : AuditActions.ApplicationSessionIdleTimedOut;

    private static ApplicationSessionResult EndedFailure(ApplicationSession session) => session.EndReason switch
    {
        SessionEndReason.IdleTimeout => Failure(ApplicationSessionDisposition.IdleExpired, OperationalErrorCodes.SessionExpired, session),
        SessionEndReason.AbsoluteTimeout => Failure(ApplicationSessionDisposition.AbsoluteExpired, OperationalErrorCodes.SessionExpired, session),
        SessionEndReason.AccessDisabled => Failure(ApplicationSessionDisposition.AccessDisabled, OperationalErrorCodes.AccessDisabled, session),
        SessionEndReason.AccessChanged => Failure(ApplicationSessionDisposition.AccessChanged, OperationalErrorCodes.SessionRevoked, session),
        _ => Failure(ApplicationSessionDisposition.Revoked, OperationalErrorCodes.SessionRevoked, session)
    };

    private static ApplicationSessionResult Failure(ApplicationSessionDisposition disposition, string errorCode, ApplicationSession? session = null) =>
        new(disposition, session, errorCode);

    private static string AuthenticationMethod(ClaimsPrincipal principal)
    {
        string value = principal.Identity?.AuthenticationType ?? "unknown";
        return value.Length <= 64 ? value : value[..64];
    }

    private static Guid CryptographicSessionId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return new Guid(bytes);
    }
}
