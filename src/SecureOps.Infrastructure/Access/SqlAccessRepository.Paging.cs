using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Access;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Infrastructure.Access;

public sealed partial class SqlAccessRepository
{
    /// <summary>Queries only the requested page; profile values are persisted authentication evidence.</summary>
    public async Task<AccessPage<AccessUserResponse>> PageUsersAsync(AccessPageQuery query, CancellationToken cancellationToken)
    {
        ValidatePage(query, ["Pending", "Approved", "Disabled", "Rejected"]);
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        const string filter = """
            WHERE (@Status IS NULL OR (CASE WHEN u.AccessStatus='Pending' AND
                (SELECT TOP(1) Status FROM security.AccessRequests WHERE UserId=u.UserId ORDER BY RequestedAt DESC,AccessRequestId DESC)='Rejected'
                THEN 'Rejected' ELSE u.AccessStatus END)=@Status)
            AND (@Search IS NULL OR u.DisplayName LIKE @Search ESCAPE '~' OR u.LoginName LIKE @Search ESCAPE '~'
                OR u.Mail LIKE @Search ESCAPE '~' OR u.CorporateIdentity LIKE @Search ESCAPE '~')
            AND (@Role IS NULL OR EXISTS (SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND r.RoleCode=@Role))
            """;
        object parameters = PageParameters(query);
        long count = await connection.ExecuteScalarAsync<long>(Command($"SELECT COUNT_BIG(*) FROM security.Users u {filter}", parameters, transaction, cancellationToken));
        IEnumerable<UserRow> rows = await connection.QueryAsync<UserRow>(Command($"{_readUserSql} {filter} {_userGroupBy} ORDER BY u.UserId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", parameters, transaction, cancellationToken));
        List<AccessUserResponse> items = [];
        foreach (UserRow row in rows)
        {
            RequestRow? request = await connection.QuerySingleOrDefaultAsync<RequestRow>(Command(
                $"SELECT TOP(1) * FROM ({_readRequestSql}) q WHERE q.UserId=@Id ORDER BY q.RequestedAt DESC,q.Id DESC", new { row.Id }, transaction, cancellationToken));
            ApplicationUser user = Map(row);
            AccessIdentityProfileResponse profile = Profile(row);
            AccessRequestResponse? latest = request is null ? null : RequestResponse(request, profile);
            items.Add(new(user.Id, user.CorporateIdentity, profile, user.Status.ToString(), user.Roles, user.Capabilities, latest,
                [], user.Version, user.AuthenticationSource, user.FirstAuthenticatedAt, user.LastAuthenticatedAt, user.DisabledAt));
        }
        await transaction.CommitAsync(cancellationToken);
        return new(items, count, query.Page, query.PageSize);
    }

    /// <summary>Request state filters are independent of the user's approval state.</summary>
    public async Task<AccessPage<AccessRequestResponse>> PageRequestsAsync(AccessPageQuery query, CancellationToken cancellationToken)
    {
        ValidatePage(query, ["Pending", "Approved", "Rejected", "Cancelled"]);
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        const string filter = """
            WHERE (@Status IS NULL OR ar.Status=@Status)
            AND (@Search IS NULL OR u.DisplayName LIKE @Search ESCAPE '~' OR u.LoginName LIKE @Search ESCAPE '~'
                OR u.Mail LIKE @Search ESCAPE '~' OR u.CorporateIdentity LIKE @Search ESCAPE '~')
            AND (@Role IS NULL OR EXISTS(SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND r.RoleCode=@Role))
            """;
        object parameters = PageParameters(query);
        long count = await connection.ExecuteScalarAsync<long>(Command($"SELECT COUNT_BIG(*) FROM security.AccessRequests ar JOIN security.Users u ON ar.UserId=u.UserId {filter}", parameters, transaction, cancellationToken));
        IEnumerable<RequestRow> rows = await connection.QueryAsync<RequestRow>(Command($"{_readRequestSql} {filter} ORDER BY ar.RequestedAt DESC,ar.AccessRequestId DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", parameters, transaction, cancellationToken));
        List<AccessRequestResponse> items = [];
        foreach (RequestRow row in rows)
        {
            UserRow user = await connection.QuerySingleAsync<UserRow>(Command(
                "SELECT UserId AS Id,CorporateIdentity,AuthenticationSource,LoginName,DisplayName,Mail,Uid,ProfileUpdatedAt FROM security.Users WHERE UserId=@UserId", row, transaction, cancellationToken));
            items.Add(RequestResponse(row, Profile(user)));
        }
        await transaction.CommitAsync(cancellationToken);
        return new(items, count, query.Page, query.PageSize);
    }

    private static void ValidatePage(AccessPageQuery query, string[] statuses)
    {
        if (query.Page is < 1 or > 1000000 || query.PageSize is < 1 or > 100 || query.Search?.Length > 128 ||
            query.Role?.Length > 64 || (query.Status is not null && !statuses.Contains(query.Status, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Invalid bounded access query.", nameof(query));
        }
    }

    private static object PageParameters(AccessPageQuery query) => new
    {
        query.Status,
        query.Role,
        query.PageSize,
        Offset = (query.Page - 1) * query.PageSize,
        Search = string.IsNullOrWhiteSpace(query.Search) ? null : "%" + query.Search.Trim().Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%"
    };

    private static AccessIdentityProfileResponse Profile(UserRow row) => new(row.DisplayName, row.LoginName, row.Mail, null, null, row.Uid);
    private static AccessRequestResponse RequestResponse(RequestRow row, AccessIdentityProfileResponse profile) => new(row.Id, row.UserId, row.CorporateIdentity, row.Status, row.RequestedAt, row.DecidedAt, row.DecisionReason, row.DecidedByCorporateIdentity, row.Version, profile);
}
