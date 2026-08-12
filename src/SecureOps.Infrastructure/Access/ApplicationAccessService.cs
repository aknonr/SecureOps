using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.Access;

/// <summary>Authentication-independent access approval and role service.</summary>
public sealed class ApplicationAccessService : IApplicationAccessService
{
    private readonly ICorporatePrincipalResolver _principalResolver;
    private readonly IAccessRepository _repository;
    private readonly IAuditWriter _auditWriter;
    private readonly AccessOptions _options;
    private readonly ILogger<ApplicationAccessService> _logger;

    /// <summary>Initializes the service.</summary>
    public ApplicationAccessService(
        ICorporatePrincipalResolver principalResolver,
        IAccessRepository repository,
        IAuditWriter auditWriter,
        IOptions<AccessOptions> options,
        ILogger<ApplicationAccessService> logger)
    {
        _principalResolver = principalResolver;
        _repository = repository;
        _auditWriter = auditWriter;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccessServiceResult<EnsureAccessUserResult>> GetCurrentAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken)
    {
        CorporatePrincipal? corporatePrincipal = _principalResolver.Resolve(principal);
        if (corporatePrincipal is null)
        {
            return AccessServiceResult<EnsureAccessUserResult>.Fail(OperationalErrorCodes.AccessDenied);
        }

        EnsureAccessUserResult ensured = await _repository.EnsureUserAsync(corporatePrincipal, _options.AutoCreateRequest, cancellationToken);
        if (ensured.UserCreated && !await TryAuditAsync(AuditActions.UserFirstSeen, context, ensured.User.Id, ensured.PendingRequest?.Id, null, null, cancellationToken))
        {
            return AccessServiceResult<EnsureAccessUserResult>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        if (ensured.RequestCreated && !await TryAuditAsync(AuditActions.AccessRequested, context, ensured.User.Id, ensured.PendingRequest?.Id, null, null, cancellationToken))
        {
            return AccessServiceResult<EnsureAccessUserResult>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        (string SystemActor, string[] Roles)? bootstrap = ResolveBootstrap(corporatePrincipal);
        if (ensured.User.Status == AccessStatus.Pending && ensured.PendingRequest is not null && bootstrap is not null)
        {
            AccessMutationResult mutation = await _repository.DecideRequestAsync(
                ensured.PendingRequest.Id,
                AccessRequestStatus.Approved,
                bootstrap.Value.SystemActor,
                bootstrap.Value.Roles,
                "Controlled authentication compatibility bootstrap.",
                cancellationToken);
            if (mutation.Disposition == AccessMutationDisposition.Applied)
            {
                AccessOperationContext bootstrapContext = context with { Actor = bootstrap.Value.SystemActor };
                if (!await AuditMutationAsync(AuditActions.AccessApproved, mutation, bootstrapContext, "Controlled authentication compatibility bootstrap.", cancellationToken))
                {
                    return AccessServiceResult<EnsureAccessUserResult>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
                }

                ensured = new EnsureAccessUserResult(mutation.User!, mutation.Request, ensured.UserCreated, ensured.RequestCreated);
            }
        }

        return AccessServiceResult<EnsureAccessUserResult>.Success(ensured);
    }

    /// <inheritdoc />
    public async Task<AccessServiceResult<IReadOnlyList<ApplicationAccessRequest>>> ListRequestsAsync(AccessRequestStatus? status, AccessOperationContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationAccessRequest> requests = await _repository.ListRequestsAsync(status, cancellationToken);
        return await TryAuditAsync(AuditActions.AccessRequestsViewed, context, null, null, null, null, cancellationToken)
            ? AccessServiceResult<IReadOnlyList<ApplicationAccessRequest>>.Success(requests)
            : AccessServiceResult<IReadOnlyList<ApplicationAccessRequest>>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
    }

    /// <inheritdoc />
    public Task<AccessServiceResult<AccessMutationResult>> ApproveAsync(Guid requestId, IReadOnlyCollection<string> roles, string reason, AccessOperationContext context, CancellationToken cancellationToken) =>
        DecideAsync(requestId, AccessRequestStatus.Approved, roles, reason, context, cancellationToken);

    /// <inheritdoc />
    public Task<AccessServiceResult<AccessMutationResult>> RejectAsync(Guid requestId, string reason, AccessOperationContext context, CancellationToken cancellationToken) =>
        DecideAsync(requestId, AccessRequestStatus.Rejected, [], reason, context, cancellationToken);

    /// <inheritdoc />
    public async Task<AccessServiceResult<AccessMutationResult>> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, string reason, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (!ValidReason(reason) || !ValidRoles(roles, requireAtLeastOne: true))
        {
            return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AccessRequestInvalidState);
        }

        AccessMutationResult mutation = await _repository.ReplaceRolesAsync(userId, roles, context.Actor, reason.Trim(), cancellationToken);
        return await MapMutationAsync(mutation, null, context, reason.Trim(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessServiceResult<AccessMutationResult>> DisableAsync(Guid userId, string reason, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (!ValidReason(reason))
        {
            return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AccessRequestInvalidState);
        }

        AccessMutationResult mutation = await _repository.DisableUserAsync(userId, context.Actor, reason.Trim(), cancellationToken);
        return await MapMutationAsync(mutation, AuditActions.AccessDisabled, context, reason.Trim(), cancellationToken);
    }

    private async Task<AccessServiceResult<AccessMutationResult>> DecideAsync(
        Guid requestId,
        AccessRequestStatus decision,
        IReadOnlyCollection<string> roles,
        string reason,
        AccessOperationContext context,
        CancellationToken cancellationToken)
    {
        if (!ValidReason(reason) || (decision == AccessRequestStatus.Approved && !ValidRoles(roles, requireAtLeastOne: true)))
        {
            return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AccessRequestInvalidState);
        }

        AccessMutationResult mutation = await _repository.DecideRequestAsync(requestId, decision, context.Actor, roles, reason.Trim(), cancellationToken);
        return await MapMutationAsync(
            mutation,
            decision == AccessRequestStatus.Approved ? AuditActions.AccessApproved : AuditActions.AccessRejected,
            context,
            reason.Trim(),
            cancellationToken);
    }

    private async Task<AccessServiceResult<AccessMutationResult>> MapMutationAsync(
        AccessMutationResult mutation,
        string? action,
        AccessOperationContext context,
        string reason,
        CancellationToken cancellationToken)
    {
        string? error = mutation.Disposition switch
        {
            AccessMutationDisposition.Applied => null,
            AccessMutationDisposition.NotFound => OperationalErrorCodes.AccessRecordNotFound,
            AccessMutationDisposition.SelfApprovalDenied => OperationalErrorCodes.AccessSelfApprovalDenied,
            _ => OperationalErrorCodes.AccessRequestInvalidState
        };
        if (error is not null)
        {
            return AccessServiceResult<AccessMutationResult>.Fail(error);
        }

        if (action is not null && !await AuditMutationAsync(action, mutation, context, reason, cancellationToken))
        {
            return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        foreach (string role in mutation.AddedRoles)
        {
            if (!await TryAuditAsync(AuditActions.RoleAssigned, context, mutation.User!.Id, mutation.Request?.Id, role, reason, cancellationToken))
            {
                return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
            }
        }

        foreach (string role in mutation.RemovedRoles)
        {
            if (!await TryAuditAsync(AuditActions.RoleRemoved, context, mutation.User!.Id, mutation.Request?.Id, role, reason, cancellationToken))
            {
                return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
            }
        }

        return AccessServiceResult<AccessMutationResult>.Success(mutation);
    }

    private Task<bool> AuditMutationAsync(string action, AccessMutationResult mutation, AccessOperationContext context, string reason, CancellationToken cancellationToken) =>
        TryAuditAsync(action, context, mutation.User?.Id, mutation.Request?.Id, null, reason, cancellationToken);

    private async Task<bool> TryAuditAsync(string action, AccessOperationContext context, Guid? userId, Guid? requestId, string? role, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            await _auditWriter.WriteAsync(new AuditEvent
            {
                Actor = context.Actor,
                Action = action,
                CorrelationId = context.CorrelationId,
                SourceIp = context.SourceIp,
                Details = new { targetUserId = userId, accessRequestId = requestId, role, reason }
            }, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Access audit failed. Action: {Action}. CorrelationId: {CorrelationId}", action, context.CorrelationId);
            return false;
        }
    }

    private (string SystemActor, string[] Roles)? ResolveBootstrap(CorporatePrincipal principal)
    {
        if (_options.BootstrapAdministrators.Contains(principal.Identifier, StringComparer.OrdinalIgnoreCase))
        {
            return ("system:configured-bootstrap", ["Admin"]);
        }

        if (!_options.DemoCompatibilityEnabled || !string.Equals(principal.AuthenticationSource, "demo-api-bridge", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return principal.Identifier.ToLowerInvariant() switch
        {
            "demo:platform-admin" => ("system:demo-compatibility", ["Admin"]),
            "demo:team-lead" => ("system:demo-compatibility", ["Lead"]),
            _ => null
        };
    }

    private static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500;
    private static bool ValidRoles(IReadOnlyCollection<string> roles, bool requireAtLeastOne) =>
        (!requireAtLeastOne || roles.Count > 0) && roles.Count <= 16 && roles.All(AccessRoleCatalog.IsKnownRole);
}
