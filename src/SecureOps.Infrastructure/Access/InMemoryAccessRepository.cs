using SecureOps.Domain.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>Concurrency-safe access repository for local and isolated tests.</summary>
public sealed class InMemoryAccessRepository : IAccessRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, StoredUser> _users = [];
    private readonly Dictionary<string, Guid> _userIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, StoredRequest> _requests = [];

    /// <inheritdoc />
    public async Task<EnsureAccessUserResult> EnsureUserAsync(CorporatePrincipal principal, bool createRequest, TimeSpan activityPersistenceInterval, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool oidcProfile = string.Equals(principal.AuthenticationSource, "oidc", StringComparison.Ordinal);
            bool userCreated = false;
            if (!_userIds.TryGetValue(principal.Identifier, out Guid userId))
            {
                userId = Guid.NewGuid();
                _users[userId] = new StoredUser(userId, principal.Identifier, principal.AuthenticationSource, AccessStatus.Pending, now, now, null, 1, [],
                    oidcProfile ? principal.LoginName : null,
                    oidcProfile ? principal.DisplayName : null,
                    oidcProfile ? principal.Mail : null,
                    oidcProfile ? principal.Uid : null,
                    oidcProfile && HasProfile(principal) ? now : null);
                _userIds[principal.Identifier] = userId;
                userCreated = true;
            }
            else
            {
                StoredUser existing = _users[userId];
                bool profileChanged = oidcProfile && ProfileChanged(existing, principal);
                _users[userId] = existing with
                {
                    LastAuthenticatedAt = now - existing.LastAuthenticatedAt >= activityPersistenceInterval
                        ? now
                        : existing.LastAuthenticatedAt,
                    LoginName = oidcProfile ? principal.LoginName ?? existing.LoginName : existing.LoginName,
                    DisplayName = oidcProfile ? principal.DisplayName ?? existing.DisplayName : existing.DisplayName,
                    Mail = oidcProfile ? principal.Mail ?? existing.Mail : existing.Mail,
                    Uid = oidcProfile ? principal.Uid ?? existing.Uid : existing.Uid,
                    ProfileUpdatedAt = profileChanged ? now : existing.ProfileUpdatedAt
                };
            }

            StoredRequest? latest = _requests.Values
                .Where(request => request.UserId == userId)
                .OrderByDescending(request => request.RequestedAt)
                .FirstOrDefault();
            bool requestCreated = false;
            if (createRequest && latest is null && _users[userId].Status == AccessStatus.Pending)
            {
                latest = new StoredRequest(Guid.NewGuid(), userId, AccessRequestStatus.Pending, now, null, null, null, 1);
                _requests[latest.Id] = latest;
                requestCreated = true;
            }

            return new EnsureAccessUserResult(ToUser(_users[userId]), latest is null ? null : ToRequest(latest), userCreated, requestCreated);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<ApplicationUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken) => ReadAsync(cancellationToken, () =>
        _users.TryGetValue(userId, out StoredUser? user) ? ToUser(user) : null);

    /// <inheritdoc />
    public Task<ApplicationUser?> GetUserAsync(string corporateIdentity, CancellationToken cancellationToken) => ReadAsync(cancellationToken, () =>
        _userIds.TryGetValue(corporateIdentity, out Guid userId) ? ToUser(_users[userId]) : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<ApplicationUser>> ListUsersAsync(CancellationToken cancellationToken) => ReadAsync<IReadOnlyList<ApplicationUser>>(
        cancellationToken,
        () => _users.Values.OrderBy(user => user.CorporateIdentity, StringComparer.OrdinalIgnoreCase).Select(ToUser).ToArray());

    /// <inheritdoc />
    public Task<ApplicationAccessRequest?> GetPendingRequestAsync(Guid userId, CancellationToken cancellationToken) => ReadAsync(cancellationToken, () =>
    {
        StoredRequest? request = _requests.Values.FirstOrDefault(item => item.UserId == userId && item.Status == AccessRequestStatus.Pending);
        return request is null ? null : ToRequest(request);
    });

    /// <inheritdoc />
    public Task<IReadOnlyList<ApplicationAccessRequest>> ListRequestsAsync(AccessRequestStatus? status, CancellationToken cancellationToken) => ReadAsync<IReadOnlyList<ApplicationAccessRequest>>(
        cancellationToken,
        () => _requests.Values
            .Where(request => status is null || request.Status == status)
            .OrderByDescending(request => request.RequestedAt)
            .Select(ToRequest)
            .ToArray());

    /// <inheritdoc />
    public Task<IReadOnlyList<ApplicationAccessRequest>> ListRequestsForUserAsync(Guid userId, CancellationToken cancellationToken) => ReadAsync<IReadOnlyList<ApplicationAccessRequest>>(
        cancellationToken,
        () => _requests.Values
            .Where(request => request.UserId == userId)
            .OrderByDescending(request => request.RequestedAt)
            .Select(ToRequest)
            .ToArray());

    /// <inheritdoc />
    public async Task<AccessMutationResult> DecideRequestAsync(Guid requestId, AccessRequestStatus decision, long expectedVersion, string actor, IReadOnlyCollection<string> roles, string reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_requests.TryGetValue(requestId, out StoredRequest? request) || !_users.TryGetValue(request.UserId, out StoredUser? user))
            {
                return Missing();
            }

            if (request.Status != AccessRequestStatus.Pending || decision == AccessRequestStatus.Pending)
            {
                return Conflict(AccessMutationDisposition.RequestAlreadyDecided, user, request);
            }

            if (request.Version != expectedVersion)
            {
                return Conflict(AccessMutationDisposition.ConcurrencyConflict, user, request);
            }

            if (decision == AccessRequestStatus.Approved && user.Status == AccessStatus.Disabled)
            {
                return Conflict(AccessMutationDisposition.UserInvalidState, user, request);
            }

            if (decision == AccessRequestStatus.Approved && string.Equals(user.CorporateIdentity, actor, StringComparison.OrdinalIgnoreCase))
            {
                return new AccessMutationResult(AccessMutationDisposition.SelfApprovalDenied, ToUser(user), ToRequest(request), [], []);
            }

            string[] previousRoles = user.Roles.ToArray();
            string[] nextRoles = decision == AccessRequestStatus.Approved ? NormalizeRoles(roles) : [];
            DateTimeOffset now = DateTimeOffset.UtcNow;
            user = user with { Status = decision == AccessRequestStatus.Approved ? AccessStatus.Approved : AccessStatus.Pending, Roles = nextRoles, Version = user.Version + 1 };
            request = request with { Status = decision, DecidedAt = now, DecisionReason = reason, DecidedByCorporateIdentity = actor, Version = request.Version + 1 };
            _users[user.Id] = user;
            _requests[request.Id] = request;
            return Applied(user, request, previousRoles, nextRoles);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccessMutationResult> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, long expectedVersion, string actor, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_users.TryGetValue(userId, out StoredUser? user))
            {
                return Missing();
            }

            if (user.Status != AccessStatus.Approved)
            {
                return Conflict(AccessMutationDisposition.UserInvalidState, user, null);
            }

            if (user.Version != expectedVersion)
            {
                return Conflict(AccessMutationDisposition.ConcurrencyConflict, user, null);
            }

            string[] previousRoles = user.Roles.ToArray();
            string[] nextRoles = NormalizeRoles(roles);
            user = user with { Roles = nextRoles, Version = user.Version + 1 };
            _users[userId] = user;
            return Applied(user, null, previousRoles, nextRoles);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccessMutationResult> DisableUserAsync(Guid userId, long expectedVersion, string actor, string reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_users.TryGetValue(userId, out StoredUser? user))
            {
                return Missing();
            }

            if (user.Status != AccessStatus.Approved)
            {
                return Conflict(AccessMutationDisposition.UserInvalidState, user, null);
            }

            if (user.Version != expectedVersion)
            {
                return Conflict(AccessMutationDisposition.ConcurrencyConflict, user, null);
            }

            string[] previousRoles = user.Roles.ToArray();
            user = user with { Status = AccessStatus.Disabled, DisabledAt = DateTimeOffset.UtcNow, Roles = [], Version = user.Version + 1 };
            _users[userId] = user;
            return Applied(user, null, previousRoles, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> ReadAsync<T>(CancellationToken cancellationToken, Func<T> read)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return read();
        }
        finally
        {
            _gate.Release();
        }
    }

    private ApplicationUser ToUser(StoredUser user) => new(
        user.Id,
        user.CorporateIdentity,
        user.AuthenticationSource,
        user.Status,
        user.FirstAuthenticatedAt,
        user.LastAuthenticatedAt,
        user.DisabledAt,
        user.Version,
        user.Roles,
        AccessRoleCatalog.GetCapabilities(user.Roles),
        user.LoginName,
        user.DisplayName,
        user.Mail,
        user.Uid,
        user.ProfileUpdatedAt);

    private ApplicationAccessRequest ToRequest(StoredRequest request)
    {
        StoredUser user = _users[request.UserId];
        return new ApplicationAccessRequest(request.Id, request.UserId, user.CorporateIdentity, request.Status, request.RequestedAt, request.DecidedAt, request.DecisionReason, request.DecidedByCorporateIdentity, request.Version);
    }

    private static string[] NormalizeRoles(IEnumerable<string> roles) => roles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray();
    private static AccessMutationResult Missing() => new(AccessMutationDisposition.NotFound, null, null, [], []);
    private AccessMutationResult Conflict(AccessMutationDisposition disposition, StoredUser user, StoredRequest? request) => new(disposition, ToUser(user), request is null ? null : ToRequest(request), [], []);
    private AccessMutationResult Applied(StoredUser user, StoredRequest? request, IEnumerable<string> previous, IEnumerable<string> next) => new(
        AccessMutationDisposition.Applied,
        ToUser(user),
        request is null ? null : ToRequest(request),
        next.Except(previous, StringComparer.OrdinalIgnoreCase).ToArray(),
        previous.Except(next, StringComparer.OrdinalIgnoreCase).ToArray());

    private static bool HasProfile(CorporatePrincipal principal) =>
        principal.LoginName is not null || principal.DisplayName is not null || principal.Mail is not null || principal.Uid is not null;

    private static bool ProfileChanged(StoredUser user, CorporatePrincipal principal) =>
        (principal.LoginName is not null && !string.Equals(principal.LoginName, user.LoginName, StringComparison.Ordinal))
        || (principal.DisplayName is not null && !string.Equals(principal.DisplayName, user.DisplayName, StringComparison.Ordinal))
        || (principal.Mail is not null && !string.Equals(principal.Mail, user.Mail, StringComparison.Ordinal))
        || (principal.Uid is not null && !string.Equals(principal.Uid, user.Uid, StringComparison.Ordinal));

    private sealed record StoredUser(Guid Id, string CorporateIdentity, string AuthenticationSource, AccessStatus Status, DateTimeOffset FirstAuthenticatedAt, DateTimeOffset LastAuthenticatedAt, DateTimeOffset? DisabledAt, long Version, IReadOnlyList<string> Roles, string? LoginName, string? DisplayName, string? Mail, string? Uid, DateTimeOffset? ProfileUpdatedAt);
    private sealed record StoredRequest(Guid Id, Guid UserId, AccessRequestStatus Status, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt, string? DecisionReason, string? DecidedByCorporateIdentity, long Version);
}
