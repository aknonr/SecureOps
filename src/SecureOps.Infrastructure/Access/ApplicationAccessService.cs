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
    private readonly IAccessIdentityProfileResolver _profileResolver;
    private readonly IAuditWriter _auditWriter;
    private readonly AccessOptions _options;
    private readonly SessionSecurityOptions _sessionOptions;
    private readonly ILogger<ApplicationAccessService> _logger;

    /// <summary>Initializes the service.</summary>
    public ApplicationAccessService(
        ICorporatePrincipalResolver principalResolver,
        IAccessRepository repository,
        IAccessIdentityProfileResolver profileResolver,
        IAuditWriter auditWriter,
        IOptions<AccessOptions> options,
        IOptions<SessionSecurityOptions> sessionOptions,
        ILogger<ApplicationAccessService> logger)
    {
        _principalResolver = principalResolver;
        _repository = repository;
        _profileResolver = profileResolver;
        _auditWriter = auditWriter;
        _options = options.Value;
        _sessionOptions = sessionOptions.Value;
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

        EnsureAccessUserResult ensured = await _repository.EnsureUserAsync(
            corporatePrincipal,
            _options.AutoCreateRequest,
            TimeSpan.FromMinutes(_sessionOptions.ActivityPersistenceIntervalMinutes),
            cancellationToken);
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
            const string bootstrapReason = "Controlled authentication bootstrap.";
            AccessMutationResult mutation = await _repository.DecideRequestAsync(
                ensured.PendingRequest.Id,
                AccessRequestStatus.Approved,
                ensured.PendingRequest.Version,
                bootstrap.Value.SystemActor,
                bootstrap.Value.Roles,
                bootstrapReason,
                cancellationToken);
            if (mutation.Disposition == AccessMutationDisposition.Applied)
            {
                AccessOperationContext bootstrapContext = context with { Actor = bootstrap.Value.SystemActor };
                AccessServiceResult<AccessMutationResult> audited = await MapMutationAsync(
                    mutation,
                    AuditActions.AccessApproved,
                    bootstrapContext,
                    bootstrapReason,
                    cancellationToken);
                if (!audited.IsSuccess)
                {
                    return AccessServiceResult<EnsureAccessUserResult>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
                }

                ensured = new EnsureAccessUserResult(mutation.User!, mutation.Request, ensured.UserCreated, ensured.RequestCreated);
            }
        }

        return AccessServiceResult<EnsureAccessUserResult>.Success(ensured);
    }

    /// <inheritdoc />
    public Task<AccessIdentityProfile?> GetProfileAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        _profileResolver.ResolveAsync(user.CorporateIdentity, cancellationToken);

    /// <inheritdoc />
    public async Task<AccessServiceResult<IReadOnlyList<AccessRequestReadModel>>> ListRequestsAsync(AccessRequestStatus? status, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (!await TryAuditAsync(AuditActions.AccessRequestsViewed, context, null, null, null, null, cancellationToken))
        {
            return AccessServiceResult<IReadOnlyList<AccessRequestReadModel>>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        IReadOnlyList<ApplicationAccessRequest> requests = await _repository.ListRequestsAsync(status, cancellationToken);
        List<AccessRequestReadModel> results = new(requests.Count);
        Dictionary<string, AccessIdentityProfile?> profiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (ApplicationAccessRequest request in requests)
        {
            if (!profiles.TryGetValue(request.CorporateIdentity, out AccessIdentityProfile? profile))
            {
                profile = await _profileResolver.ResolveAsync(request.CorporateIdentity, cancellationToken);
                profiles[request.CorporateIdentity] = profile;
            }

            results.Add(new AccessRequestReadModel(request, profile));
        }

        return AccessServiceResult<IReadOnlyList<AccessRequestReadModel>>.Success(results);
    }

    /// <inheritdoc />
    public async Task<AccessServiceResult<IReadOnlyList<AccessUserReadModel>>> ListUsersAsync(AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (!await TryAuditAsync(AuditActions.AccessUsersViewed, context, null, null, null, null, cancellationToken))
        {
            return AccessServiceResult<IReadOnlyList<AccessUserReadModel>>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        IReadOnlyList<ApplicationUser> users = await _repository.ListUsersAsync(cancellationToken);
        IReadOnlyList<ApplicationAccessRequest> requests = await _repository.ListRequestsAsync(status: null, cancellationToken);
        List<AccessUserReadModel> results = new(users.Count);
        foreach (ApplicationUser user in users)
        {
            AccessIdentityProfile? profile = await _profileResolver.ResolveAsync(user.CorporateIdentity, cancellationToken);
            results.Add(new AccessUserReadModel(
                user,
                profile,
                requests.Where(request => request.UserId == user.Id).OrderByDescending(request => request.RequestedAt).ToArray()));
        }

        return AccessServiceResult<IReadOnlyList<AccessUserReadModel>>.Success(results);
    }

    /// <inheritdoc />
    public async Task<AccessServiceResult<AccessUserReadModel>> GetUserAsync(Guid userId, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (!await TryAuditAsync(AuditActions.AccessUserViewed, context, userId, null, null, null, cancellationToken))
        {
            return AccessServiceResult<AccessUserReadModel>.Fail(OperationalErrorCodes.AuditStoreUnavailable);
        }

        ApplicationUser? user = await _repository.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccessServiceResult<AccessUserReadModel>.Fail(OperationalErrorCodes.AccessRecordNotFound);
        }

        AccessIdentityProfile? profile = await _profileResolver.ResolveAsync(user.CorporateIdentity, cancellationToken);
        IReadOnlyList<ApplicationAccessRequest> requests = await _repository.ListRequestsForUserAsync(userId, cancellationToken);
        return AccessServiceResult<AccessUserReadModel>.Success(new AccessUserReadModel(user, profile, requests));
    }

    /// <inheritdoc />
    public Task<AccessServiceResult<AccessMutationResult>> ApproveAsync(Guid requestId, IReadOnlyCollection<string> roles, string reason, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken) =>
        DecideAsync(requestId, AccessRequestStatus.Approved, roles, reason, expectedVersion, context, cancellationToken);

    /// <inheritdoc />
    public Task<AccessServiceResult<AccessMutationResult>> RejectAsync(Guid requestId, string reason, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken) =>
        DecideAsync(requestId, AccessRequestStatus.Rejected, [], reason, expectedVersion, context, cancellationToken);

    /// <inheritdoc />
    public async Task<AccessServiceResult<AccessMutationResult>> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, string reason, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (!ValidReason(reason) || !ValidRoles(roles, requireAtLeastOne: true) || expectedVersion <= 0)
        {
            return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AccessValidationFailed);
        }

        AccessMutationResult mutation = await _repository.ReplaceRolesAsync(userId, NormalizeRoles(roles), expectedVersion, context.Actor, reason.Trim(), cancellationToken);
        return await MapMutationAsync(mutation, null, context, reason.Trim(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessServiceResult<AccessMutationResult>> DisableAsync(Guid userId, string reason, long expectedVersion, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (!ValidReason(reason) || expectedVersion <= 0)
        {
            return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AccessValidationFailed);
        }

        AccessMutationResult mutation = await _repository.DisableUserAsync(userId, expectedVersion, context.Actor, reason.Trim(), cancellationToken);
        return await MapMutationAsync(mutation, AuditActions.AccessDisabled, context, reason.Trim(), cancellationToken);
    }

    private async Task<AccessServiceResult<AccessMutationResult>> DecideAsync(
        Guid requestId,
        AccessRequestStatus decision,
        IReadOnlyCollection<string> roles,
        string reason,
        long expectedVersion,
        AccessOperationContext context,
        CancellationToken cancellationToken)
    {
        if (!ValidReason(reason) || expectedVersion <= 0 || (decision == AccessRequestStatus.Approved && !ValidRoles(roles, requireAtLeastOne: true)))
        {
            return AccessServiceResult<AccessMutationResult>.Fail(OperationalErrorCodes.AccessValidationFailed);
        }

        IReadOnlyCollection<string> normalizedRoles = decision == AccessRequestStatus.Approved ? NormalizeRoles(roles) : [];
        AccessMutationResult mutation = await _repository.DecideRequestAsync(requestId, decision, expectedVersion, context.Actor, normalizedRoles, reason.Trim(), cancellationToken);
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
            AccessMutationDisposition.RequestAlreadyDecided => OperationalErrorCodes.AccessRequestAlreadyDecided,
            AccessMutationDisposition.ConcurrencyConflict => OperationalErrorCodes.AccessConcurrencyConflict,
            AccessMutationDisposition.UserInvalidState => OperationalErrorCodes.AccessUserInvalidState,
            AccessMutationDisposition.SelfApprovalDenied => OperationalErrorCodes.AccessSelfApprovalDenied,
            _ => throw new InvalidOperationException($"Unknown access mutation disposition: {mutation.Disposition}.")
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
    private static string[] NormalizeRoles(IEnumerable<string> roles) => AccessRoleCatalog.RoleCodes
        .Where(roleCode => roles.Contains(roleCode, StringComparer.OrdinalIgnoreCase))
        .OrderBy(roleCode => roleCode, StringComparer.Ordinal)
        .ToArray();
}
