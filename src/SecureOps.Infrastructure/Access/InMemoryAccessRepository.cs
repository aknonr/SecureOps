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
    public async Task<EnsureAccessUserResult> EnsureUserAsync(CorporatePrincipal principal, bool createRequest, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool userCreated = false;
            if (!_userIds.TryGetValue(principal.Identifier, out Guid userId))
            {
                userId = Guid.NewGuid();
                _users[userId] = new StoredUser(userId, principal.Identifier, principal.AuthenticationSource, AccessStatus.Pending, now, now, null, []);
                _userIds[principal.Identifier] = userId;
                userCreated = true;
            }
            else
            {
                _users[userId] = _users[userId] with { LastAuthenticatedAt = now };
            }

            StoredRequest? pending = _requests.Values.FirstOrDefault(request => request.UserId == userId && request.Status == AccessRequestStatus.Pending);
            bool requestCreated = false;
            if (createRequest && pending is null && _users[userId].Status == AccessStatus.Pending)
            {
                pending = new StoredRequest(Guid.NewGuid(), userId, AccessRequestStatus.Pending, now, null, null);
                _requests[pending.Id] = pending;
                requestCreated = true;
            }

            return new EnsureAccessUserResult(ToUser(_users[userId]), pending is null ? null : ToRequest(pending), userCreated, requestCreated);
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
    public async Task<AccessMutationResult> DecideRequestAsync(Guid requestId, AccessRequestStatus decision, string actor, IReadOnlyCollection<string> roles, string reason, CancellationToken cancellationToken)
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
                return Invalid(user, request);
            }

            if (decision == AccessRequestStatus.Approved && string.Equals(user.CorporateIdentity, actor, StringComparison.OrdinalIgnoreCase))
            {
                return new AccessMutationResult(AccessMutationDisposition.SelfApprovalDenied, ToUser(user), ToRequest(request), [], []);
            }

            string[] previousRoles = user.Roles.ToArray();
            string[] nextRoles = decision == AccessRequestStatus.Approved ? NormalizeRoles(roles) : [];
            DateTimeOffset now = DateTimeOffset.UtcNow;
            user = user with { Status = decision == AccessRequestStatus.Approved ? AccessStatus.Approved : AccessStatus.Pending, Roles = nextRoles };
            request = request with { Status = decision, DecidedAt = now, DecisionReason = reason };
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
    public async Task<AccessMutationResult> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, string actor, string reason, CancellationToken cancellationToken)
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
                return Invalid(user, null);
            }

            string[] previousRoles = user.Roles.ToArray();
            string[] nextRoles = NormalizeRoles(roles);
            user = user with { Roles = nextRoles };
            _users[userId] = user;
            return Applied(user, null, previousRoles, nextRoles);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccessMutationResult> DisableUserAsync(Guid userId, string actor, string reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_users.TryGetValue(userId, out StoredUser? user))
            {
                return Missing();
            }

            if (user.Status == AccessStatus.Disabled)
            {
                return Invalid(user, null);
            }

            string[] previousRoles = user.Roles.ToArray();
            user = user with { Status = AccessStatus.Disabled, DisabledAt = DateTimeOffset.UtcNow, Roles = [] };
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
        user.Roles,
        AccessRoleCatalog.GetCapabilities(user.Roles));

    private ApplicationAccessRequest ToRequest(StoredRequest request)
    {
        StoredUser user = _users[request.UserId];
        return new ApplicationAccessRequest(request.Id, request.UserId, user.CorporateIdentity, request.Status, request.RequestedAt, request.DecidedAt, request.DecisionReason);
    }

    private static string[] NormalizeRoles(IEnumerable<string> roles) => roles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray();
    private static AccessMutationResult Missing() => new(AccessMutationDisposition.NotFound, null, null, [], []);
    private AccessMutationResult Invalid(StoredUser user, StoredRequest? request) => new(AccessMutationDisposition.InvalidState, ToUser(user), request is null ? null : ToRequest(request), [], []);
    private AccessMutationResult Applied(StoredUser user, StoredRequest? request, IEnumerable<string> previous, IEnumerable<string> next) => new(
        AccessMutationDisposition.Applied,
        ToUser(user),
        request is null ? null : ToRequest(request),
        next.Except(previous, StringComparer.OrdinalIgnoreCase).ToArray(),
        previous.Except(next, StringComparer.OrdinalIgnoreCase).ToArray());

    private sealed record StoredUser(Guid Id, string CorporateIdentity, string AuthenticationSource, AccessStatus Status, DateTimeOffset FirstAuthenticatedAt, DateTimeOffset LastAuthenticatedAt, DateTimeOffset? DisabledAt, IReadOnlyList<string> Roles);
    private sealed record StoredRequest(Guid Id, Guid UserId, AccessRequestStatus Status, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt, string? DecisionReason);
}
