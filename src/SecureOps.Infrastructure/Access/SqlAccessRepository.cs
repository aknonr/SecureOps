using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.Access;

/// <summary>SQL Server application-access repository with transactional decisions.</summary>
public sealed class SqlAccessRepository : IAccessRepository
{
    private const int _commandTimeoutSeconds = 15;
    private readonly string _connectionString;

    /// <summary>Initializes the SQL repository.</summary>
    public SqlAccessRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for SQL access persistence.");
    }

    /// <inheritdoc />
    public async Task<EnsureAccessUserResult> EnsureUserAsync(CorporatePrincipal principal, bool createRequest, TimeSpan activityPersistenceInterval, CancellationToken cancellationToken)
    {
        bool oidcProfile = string.Equals(principal.AuthenticationSource, "oidc", StringComparison.Ordinal);
        string? loginName = oidcProfile ? principal.LoginName : null;
        string? displayName = oidcProfile ? principal.DisplayName : null;
        string? mail = oidcProfile ? principal.Mail : null;
        string? uid = oidcProfile ? principal.Uid : null;
        const string select = "SELECT UserId FROM security.Users WITH (UPDLOCK, HOLDLOCK) WHERE CorporateIdentity = @CorporateIdentity;";
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        Guid? userId = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(select, new { CorporateIdentity = principal.Identifier }, transaction, cancellationToken));
        bool userCreated = userId is null;
        if (userCreated)
        {
            userId = Guid.NewGuid();
            const string insertUser = """
                INSERT INTO security.Users
                    (UserId, CorporateIdentity, AuthenticationSource, AccessStatus, FirstAuthenticatedAt, LastAuthenticatedAt,
                     LoginName, DisplayName, Mail, Uid, ProfileUpdatedAt)
                VALUES (@UserId, @CorporateIdentity, @AuthenticationSource, 'Pending', SYSUTCDATETIME(), SYSUTCDATETIME(),
                        @LoginName, @DisplayName, @Mail, @Uid,
                        CASE WHEN @LoginName IS NULL AND @DisplayName IS NULL AND @Mail IS NULL AND @Uid IS NULL
                             THEN NULL ELSE SYSUTCDATETIME() END);
                """;
            await connection.ExecuteAsync(Command(insertUser, new
            {
                UserId = userId.Value,
                CorporateIdentity = principal.Identifier,
                principal.AuthenticationSource,
                LoginName = loginName,
                DisplayName = displayName,
                Mail = mail,
                Uid = uid
            }, transaction, cancellationToken));
        }
        else
        {
            const string updateSeen = """
                DECLARE @Now datetimeoffset(7) = SYSUTCDATETIME();
                UPDATE security.Users
                SET LastAuthenticatedAt = CASE
                        WHEN LastAuthenticatedAt <= DATEADD(MINUTE, -@ActivityPersistenceIntervalMinutes, @Now)
                        THEN @Now ELSE LastAuthenticatedAt END,
                    LoginName = COALESCE(@LoginName, LoginName),
                    DisplayName = COALESCE(@DisplayName, DisplayName),
                    Mail = COALESCE(@Mail, Mail),
                    Uid = COALESCE(@Uid, Uid),
                    ProfileUpdatedAt = CASE WHEN
                        (@LoginName IS NOT NULL AND (LoginName IS NULL OR CONVERT(varbinary(max), LoginName) <> CONVERT(varbinary(max), @LoginName))) OR
                        (@DisplayName IS NOT NULL AND (DisplayName IS NULL OR CONVERT(varbinary(max), DisplayName) <> CONVERT(varbinary(max), @DisplayName))) OR
                        (@Mail IS NOT NULL AND (Mail IS NULL OR CONVERT(varbinary(max), Mail) <> CONVERT(varbinary(max), @Mail))) OR
                        (@Uid IS NOT NULL AND (Uid IS NULL OR CONVERT(varbinary(max), Uid) <> CONVERT(varbinary(max), @Uid)))
                        THEN @Now ELSE ProfileUpdatedAt END
                WHERE UserId = @UserId
                  AND (LastAuthenticatedAt <= DATEADD(MINUTE, -@ActivityPersistenceIntervalMinutes, @Now) OR
                       (@LoginName IS NOT NULL AND (LoginName IS NULL OR CONVERT(varbinary(max), LoginName) <> CONVERT(varbinary(max), @LoginName))) OR
                       (@DisplayName IS NOT NULL AND (DisplayName IS NULL OR CONVERT(varbinary(max), DisplayName) <> CONVERT(varbinary(max), @DisplayName))) OR
                       (@Mail IS NOT NULL AND (Mail IS NULL OR CONVERT(varbinary(max), Mail) <> CONVERT(varbinary(max), @Mail))) OR
                       (@Uid IS NOT NULL AND (Uid IS NULL OR CONVERT(varbinary(max), Uid) <> CONVERT(varbinary(max), @Uid))));
                """;
            await connection.ExecuteAsync(Command(updateSeen, new
            {
                UserId = userId.GetValueOrDefault(),
                ActivityPersistenceIntervalMinutes = (int)activityPersistenceInterval.TotalMinutes,
                LoginName = loginName,
                DisplayName = displayName,
                Mail = mail,
                Uid = uid
            }, transaction, cancellationToken));
        }

        Guid ensuredUserId = userId ?? throw new InvalidOperationException("Access user identity was not persisted.");
        const string latestSql = "SELECT TOP (1) AccessRequestId FROM security.AccessRequests WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId ORDER BY RequestedAt DESC;";
        Guid? requestId = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(latestSql, new { UserId = ensuredUserId }, transaction, cancellationToken));
        bool requestCreated = false;
        if (createRequest && requestId is null)
        {
            string? status = await connection.QuerySingleAsync<string>(Command("SELECT AccessStatus FROM security.Users WHERE UserId = @UserId;", new { UserId = ensuredUserId }, transaction, cancellationToken));
            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                requestId = Guid.NewGuid();
                const string insertRequest = """
                    INSERT INTO security.AccessRequests (AccessRequestId, UserId, Status, RequestedAt)
                    VALUES (@AccessRequestId, @UserId, 'Pending', SYSUTCDATETIME());
                    INSERT INTO security.AccessRequestHistory (AccessRequestId, Status, ChangedByCorporateIdentity)
                    VALUES (@AccessRequestId, 'Pending', NULL);
                    """;
                await connection.ExecuteAsync(Command(insertRequest, new { AccessRequestId = requestId.Value, UserId = ensuredUserId }, transaction, cancellationToken));
                requestCreated = true;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        ApplicationUser user = (await GetUserAsync(ensuredUserId, cancellationToken))!;
        ApplicationAccessRequest? request = requestId is null ? null : await GetRequestAsync(requestId.Value, cancellationToken);
        return new EnsureAccessUserResult(user, request, userCreated, requestCreated);
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        UserRow? row = await connection.QuerySingleOrDefaultAsync<UserRow>(Command($"{_readUserSql} WHERE u.UserId = @UserId {_userGroupBy}", new { UserId = userId }, null, cancellationToken));
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> GetUserAsync(string corporateIdentity, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        UserRow? row = await connection.QuerySingleOrDefaultAsync<UserRow>(Command($"{_readUserSql} WHERE u.CorporateIdentity = @CorporateIdentity {_userGroupBy}", new { CorporateIdentity = corporateIdentity }, null, cancellationToken));
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationUser>> ListUsersAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        IEnumerable<UserRow> rows = await connection.QueryAsync<UserRow>(Command($"{_readUserSql} {_userGroupBy} ORDER BY u.CorporateIdentity", null, null, cancellationToken));
        return rows.Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async Task<ApplicationAccessRequest?> GetPendingRequestAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        RequestRow? row = await connection.QuerySingleOrDefaultAsync<RequestRow>(Command($"{_readRequestSql} WHERE ar.UserId = @UserId AND ar.Status = 'Pending'", new { UserId = userId }, null, cancellationToken));
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationAccessRequest>> ListRequestsAsync(AccessRequestStatus? status, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        string sql = $"{_readRequestSql} WHERE (@Status IS NULL OR ar.Status = @Status) ORDER BY ar.RequestedAt DESC;";
        IEnumerable<RequestRow> rows = await connection.QueryAsync<RequestRow>(Command(sql, new { Status = status?.ToString() }, null, cancellationToken));
        return rows.Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationAccessRequest>> ListRequestsForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        IEnumerable<RequestRow> rows = await connection.QueryAsync<RequestRow>(Command($"{_readRequestSql} WHERE ar.UserId = @UserId ORDER BY ar.RequestedAt DESC", new { UserId = userId }, null, cancellationToken));
        return rows.Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async Task<AccessMutationResult> DecideRequestAsync(Guid requestId, AccessRequestStatus decision, long expectedVersion, string actor, IReadOnlyCollection<string> roles, string reason, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        RequestRow? request = await connection.QuerySingleOrDefaultAsync<RequestRow>(Command($"{_readRequestSql.Replace("FROM security.AccessRequests ar", "FROM security.AccessRequests ar WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)} WHERE ar.AccessRequestId = @RequestId", new { RequestId = requestId }, transaction, cancellationToken));
        if (request is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Missing();
        }

        if (!string.Equals(request.Status, AccessRequestStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase) || decision == AccessRequestStatus.Pending)
        {
            ApplicationUser currentUser = await GetUserWithinTransactionAsync(connection, transaction, request.UserId, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.RequestAlreadyDecided, currentUser, Map(request), [], []);
        }

        if (request.Version != expectedVersion)
        {
            ApplicationUser currentUser = await GetUserWithinTransactionAsync(connection, transaction, request.UserId, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.ConcurrencyConflict, currentUser, Map(request), [], []);
        }

        ApplicationUser targetUser = await GetUserWithinTransactionAsync(connection, transaction, request.UserId, cancellationToken);
        if (decision == AccessRequestStatus.Approved && targetUser.Status == AccessStatus.Disabled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.UserInvalidState, targetUser, Map(request), [], []);
        }

        if (decision == AccessRequestStatus.Approved && string.Equals(request.CorporateIdentity, actor, StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.SelfApprovalDenied, null, Map(request), [], []);
        }

        string[] previous = await GetActiveRolesAsync(connection, transaction, request.UserId, cancellationToken);
        string[] next = decision == AccessRequestStatus.Approved ? NormalizeRoles(roles) : [];
        const string update = """
            UPDATE security.AccessRequests SET Status = @Decision, DecidedAt = SYSUTCDATETIME(),
                DecidedByCorporateIdentity = @Actor, DecisionReason = @Reason, Version = Version + 1
            WHERE AccessRequestId = @RequestId;
            UPDATE security.Users SET AccessStatus = @UserStatus, DisabledAt = NULL,
                DisabledByCorporateIdentity = NULL, AccessVersion = AccessVersion + 1
            WHERE UserId = @UserId;
            """;
        await connection.ExecuteAsync(Command(update, new
        {
            RequestId = requestId,
            request.UserId,
            Decision = decision.ToString(),
            UserStatus = decision == AccessRequestStatus.Approved ? AccessStatus.Approved.ToString() : AccessStatus.Pending.ToString(),
            Actor = actor,
            Reason = reason
        }, transaction, cancellationToken));
        await ReplaceRolesWithinTransactionAsync(connection, transaction, request.UserId, next, actor, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        ApplicationUser user = (await GetUserAsync(request.UserId, cancellationToken))!;
        ApplicationAccessRequest updatedRequest = (await GetRequestAsync(requestId, cancellationToken))!;
        return Applied(user, updatedRequest, previous, next);
    }

    /// <inheritdoc />
    public async Task<AccessMutationResult> ReplaceRolesAsync(Guid userId, IReadOnlyCollection<string> roles, long expectedVersion, string actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        ApplicationUser? current = await GetUserForUpdateAsync(connection, transaction, userId, cancellationToken);
        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Missing();
        }

        if (current.Status != AccessStatus.Approved)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.UserInvalidState, current, null, [], []);
        }

        if (current.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.ConcurrencyConflict, current, null, [], []);
        }

        string[] previous = current.Roles.ToArray();
        string[] next = NormalizeRoles(roles);
        await ReplaceRolesWithinTransactionAsync(connection, transaction, userId, next, actor, cancellationToken);
        await connection.ExecuteAsync(Command("UPDATE security.Users SET AccessVersion = AccessVersion + 1 WHERE UserId = @UserId;", new { UserId = userId }, transaction, cancellationToken));
        await connection.ExecuteAsync(Command("""
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(SYSUTCDATETIME(),@actor,'AccessRolesChanged',@correlation,@details);
            """, new
        {
            actor,
            correlation = $"access-role-version:{userId:D}:{expectedVersion + 1}",
            details = JsonSerializer.Serialize(new
            {
                targetUserId = userId,
                oldRoles = previous,
                newRoles = next,
                oldCapabilities = AccessRoleCatalog.GetCapabilities(previous),
                newCapabilities = AccessRoleCatalog.GetCapabilities(next),
                previousVersion = expectedVersion,
                version = expectedVersion + 1,
                descriptionSource = "SystemGenerated",
                description = "Ordinary role assignment.",
                outcome = "Applied"
            })
        }, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        // Do not let a later writer's state replace this operation's result/audit snapshot.
        return Applied(current with { Version = expectedVersion + 1, Roles = next, Capabilities = AccessRoleCatalog.GetCapabilities(next) }, null, previous, next);
    }

    /// <inheritdoc />
    public async Task<AccessMutationResult> DisableUserAsync(Guid userId, long expectedVersion, string actor, string reason, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        ApplicationUser? current = await GetUserForUpdateAsync(connection, transaction, userId, cancellationToken);
        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Missing();
        }

        if (current.Status != AccessStatus.Approved)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.UserInvalidState, current, null, [], []);
        }

        if (current.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AccessMutationResult(AccessMutationDisposition.ConcurrencyConflict, current, null, [], []);
        }

        const string update = """
            UPDATE security.Users SET AccessStatus = 'Disabled', DisabledAt = SYSUTCDATETIME(),
                DisabledByCorporateIdentity = @Actor, AccessVersion = AccessVersion + 1 WHERE UserId = @UserId;
            UPDATE security.RoleAssignments SET RevokedAt = SYSUTCDATETIME(), RevokedByCorporateIdentity = @Actor
                WHERE UserId = @UserId AND RevokedAt IS NULL;
            """;
        await connection.ExecuteAsync(Command(update, new { UserId = userId, Actor = actor }, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return Applied((await GetUserAsync(userId, cancellationToken))!, null, current.Roles, []);
    }

    private async Task<ApplicationAccessRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        RequestRow? row = await connection.QuerySingleOrDefaultAsync<RequestRow>(Command($"{_readRequestSql} WHERE ar.AccessRequestId = @RequestId", new { RequestId = requestId }, null, cancellationToken));
        return row is null ? null : Map(row);
    }

    private static async Task ReplaceRolesWithinTransactionAsync(SqlConnection connection, SqlTransaction transaction, Guid userId, IReadOnlyCollection<string> roles, string actor, CancellationToken cancellationToken)
    {
        const string revoke = """
            UPDATE ra SET RevokedAt = SYSUTCDATETIME(), RevokedByCorporateIdentity = @Actor
            FROM security.RoleAssignments ra
            JOIN security.Roles r ON r.RoleId = ra.RoleId
            WHERE ra.UserId = @UserId AND ra.RevokedAt IS NULL AND r.RoleCode NOT IN @Roles;
            """;
        await connection.ExecuteAsync(Command(revoke, new { UserId = userId, Roles = roles.Count == 0 ? ["__none__"] : roles, Actor = actor }, transaction, cancellationToken));
        const string assign = """
            INSERT INTO security.RoleAssignments (RoleAssignmentId, UserId, RoleId, GrantedAt, GrantedByCorporateIdentity)
            SELECT NEWID(), @UserId, r.RoleId, SYSUTCDATETIME(), @Actor
            FROM security.Roles r
            WHERE r.RoleCode IN @Roles
              AND NOT EXISTS (SELECT 1 FROM security.RoleAssignments ra WHERE ra.UserId = @UserId AND ra.RoleId = r.RoleId AND ra.RevokedAt IS NULL);
            """;
        await connection.ExecuteAsync(Command(assign, new { UserId = userId, Roles = roles, Actor = actor }, transaction, cancellationToken));
    }

    private static async Task<string[]> GetActiveRolesAsync(SqlConnection connection, SqlTransaction transaction, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT r.RoleCode FROM security.RoleAssignments ra JOIN security.Roles r ON r.RoleId = ra.RoleId WHERE ra.UserId = @UserId AND ra.RevokedAt IS NULL;";
        return (await connection.QueryAsync<string>(Command(sql, new { UserId = userId }, transaction, cancellationToken))).ToArray();
    }

    private static async Task<ApplicationUser?> GetUserForUpdateAsync(SqlConnection connection, SqlTransaction transaction, Guid userId, CancellationToken cancellationToken)
    {
        const string lockSql = "SELECT UserId FROM security.Users WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId;";
        Guid? found = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(lockSql, new { UserId = userId }, transaction, cancellationToken));
        return found is null ? null : await GetUserWithinTransactionAsync(connection, transaction, userId, cancellationToken);
    }

    private static async Task<ApplicationUser> GetUserWithinTransactionAsync(SqlConnection connection, SqlTransaction transaction, Guid userId, CancellationToken cancellationToken)
    {
        UserRow row = await connection.QuerySingleAsync<UserRow>(Command($"{_readUserSql} WHERE u.UserId = @UserId {_userGroupBy}", new { UserId = userId }, transaction, cancellationToken));
        return Map(row);
    }

    private static ApplicationUser Map(UserRow row)
    {
        string[] roles = string.IsNullOrWhiteSpace(row.Roles) ? [] : row.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new ApplicationUser(row.Id, row.CorporateIdentity, row.AuthenticationSource, Enum.Parse<AccessStatus>(row.Status, true), row.FirstAuthenticatedAt, row.LastAuthenticatedAt, row.DisabledAt, row.Version, roles, AccessRoleCatalog.GetCapabilities(roles), row.LoginName, row.DisplayName, row.Mail, row.Uid, row.ProfileUpdatedAt);
    }

    private static ApplicationAccessRequest Map(RequestRow row) => new(row.Id, row.UserId, row.CorporateIdentity, Enum.Parse<AccessRequestStatus>(row.Status, true), row.RequestedAt, row.DecidedAt, row.DecisionReason, row.DecidedByCorporateIdentity, row.Version);
    private static string[] NormalizeRoles(IEnumerable<string> roles) => roles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray();
    private static AccessMutationResult Missing() => new(AccessMutationDisposition.NotFound, null, null, [], []);
    private static AccessMutationResult Applied(ApplicationUser user, ApplicationAccessRequest? request, IEnumerable<string> previous, IEnumerable<string> next) => new(AccessMutationDisposition.Applied, user, request, next.Except(previous, StringComparer.OrdinalIgnoreCase).ToArray(), previous.Except(next, StringComparer.OrdinalIgnoreCase).ToArray());
    private static CommandDefinition Command(string sql, object? parameters, IDbTransaction? transaction, CancellationToken cancellationToken) => new(sql, parameters, transaction, _commandTimeoutSeconds, cancellationToken: cancellationToken);

    private const string _readUserSql = """
        SELECT u.UserId AS Id, u.CorporateIdentity, u.AuthenticationSource, u.AccessStatus AS Status,
            u.FirstAuthenticatedAt, u.LastAuthenticatedAt, u.DisabledAt, u.AccessVersion AS Version,
            u.LoginName, u.DisplayName, u.Mail, u.Uid, u.ProfileUpdatedAt,
            STRING_AGG(r.RoleCode, ',') AS Roles
        FROM security.Users u
        LEFT JOIN security.RoleAssignments ra ON ra.UserId = u.UserId AND ra.RevokedAt IS NULL
        LEFT JOIN security.Roles r ON r.RoleId = ra.RoleId
        """;

    private const string _readRequestSql = """
        SELECT ar.AccessRequestId AS Id, ar.UserId, u.CorporateIdentity, ar.Status,
            ar.RequestedAt, ar.DecidedAt, ar.DecisionReason, ar.DecidedByCorporateIdentity, ar.Version
        FROM security.AccessRequests ar
        JOIN security.Users u ON u.UserId = ar.UserId
        """;

    private const string _userGroupBy = "GROUP BY u.UserId, u.CorporateIdentity, u.AuthenticationSource, u.AccessStatus, u.FirstAuthenticatedAt, u.LastAuthenticatedAt, u.DisabledAt, u.AccessVersion, u.LoginName, u.DisplayName, u.Mail, u.Uid, u.ProfileUpdatedAt";

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public string CorporateIdentity { get; init; } = string.Empty;
        public string AuthenticationSource { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset FirstAuthenticatedAt { get; init; }
        public DateTimeOffset LastAuthenticatedAt { get; init; }
        public DateTimeOffset? DisabledAt { get; init; }
        public long Version { get; init; }
        public string? Roles { get; init; }
        public string? LoginName { get; init; }
        public string? DisplayName { get; init; }
        public string? Mail { get; init; }
        public string? Uid { get; init; }
        public DateTimeOffset? ProfileUpdatedAt { get; init; }
    }

    private sealed class RequestRow
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string CorporateIdentity { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset RequestedAt { get; init; }
        public DateTimeOffset? DecidedAt { get; init; }
        public string? DecisionReason { get; init; }
        public string? DecidedByCorporateIdentity { get; init; }
        public long Version { get; init; }
    }
}
