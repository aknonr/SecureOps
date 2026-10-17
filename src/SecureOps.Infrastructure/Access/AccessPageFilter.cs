using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>
/// One definition of the bounded administrative list query. The SQL repository validates with it before
/// building its set-based query; providers without SQL apply the same rules in memory, so Demo/local
/// hosts get the same filters instead of an unavailable list.
/// </summary>
public static class AccessPageFilter
{
    /// <summary>User status filter values; "Rejected" is a Pending user whose latest request was rejected.</summary>
    public static IReadOnlyList<string> UserStatuses { get; } = ["Pending", "Approved", "Disabled", "Rejected"];

    /// <summary>Request status filter values.</summary>
    public static IReadOnlyList<string> RequestStatuses { get; } = ["Pending", "Approved", "Rejected", "Cancelled"];

    /// <summary>Rejects an unbounded or unknown query (page, size, search/role length, status).</summary>
    /// <exception cref="ArgumentException">The query is outside the reviewed bounds.</exception>
    public static void Validate(AccessPageQuery query, IReadOnlyList<string> statuses)
    {
        if (query.Page is < 1 or > 1000000 || query.PageSize is < 1 or > 100 || query.Search?.Length > 128 ||
            query.Role?.Length > 64 || (query.Status is not null && !statuses.Contains(query.Status, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Invalid bounded access query.", nameof(query));
        }
    }

    /// <summary>Filters and pages users with the SQL semantics (case-insensitive contains search, active role).</summary>
    public static AccessPage<AccessUserResponse> Users(IEnumerable<AccessUserResponse> users, AccessPageQuery query)
    {
        Validate(query, UserStatuses);
        AccessUserResponse[] matched =
        [
            .. users.Where(user => query.Status is null || EffectiveStatus(user) == query.Status)
                .Where(user => Matches(query.Search, user.Profile, user.CorporateIdentity))
                .Where(user => query.Role is null || user.Roles.Contains(query.Role, StringComparer.OrdinalIgnoreCase))
                .OrderBy(user => user.UserId)
        ];

        // The paged list carries no request history, exactly like the SQL page.
        return new AccessPage<AccessUserResponse>(
            [.. Page(matched, query).Select(user => user with { RequestHistory = [] })], matched.Length, query.Page, query.PageSize);
    }

    /// <summary>Filters and pages requests newest first; the role filter uses the requester's active roles.</summary>
    public static AccessPage<AccessRequestResponse> Requests(
        IEnumerable<AccessRequestResponse> requests,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> activeRolesByUser,
        AccessPageQuery query)
    {
        Validate(query, RequestStatuses);
        AccessRequestResponse[] matched =
        [
            .. requests.Where(request => query.Status is null || request.Status == query.Status)
                .Where(request => Matches(query.Search, request.Profile, request.CorporateIdentity))
                .Where(request => query.Role is null
                    || (activeRolesByUser.TryGetValue(request.UserId, out IReadOnlyList<string>? roles) && roles.Contains(query.Role, StringComparer.OrdinalIgnoreCase)))
                .OrderByDescending(request => request.RequestedAt)
                .ThenByDescending(request => request.Id)
        ];

        return new AccessPage<AccessRequestResponse>([.. Page(matched, query)], matched.Length, query.Page, query.PageSize);
    }

    /// <summary>Status shown for a user: a refused, still-Pending user reads as Rejected.</summary>
    public static string EffectiveStatus(AccessUserResponse user) =>
        user.AccessStatus == "Pending" && user.LatestRequest?.Status == "Rejected" ? "Rejected" : user.AccessStatus;

    private static IEnumerable<T> Page<T>(IReadOnlyList<T> items, AccessPageQuery query) =>
        items.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);

    // Mirrors LIKE '%term%' over display name, login, mail and corporate identity under a case-insensitive collation.
    private static bool Matches(string? search, AccessIdentityProfileResponse? profile, string corporateIdentity)
    {
        if (string.IsNullOrWhiteSpace(search))
        { return true; }
        string term = search.Trim();
        return Contains(profile?.DisplayName) || Contains(profile?.Account) || Contains(profile?.Email) || Contains(corporateIdentity);

        bool Contains(string? value) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    }
}
