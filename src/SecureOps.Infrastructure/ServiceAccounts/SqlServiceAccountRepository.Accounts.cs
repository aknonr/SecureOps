using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    private static readonly Dictionary<string, string> _sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "s.NormalizedName",
        ["due"] = "CASE WHEN g.NearestDue IS NULL THEN 1 ELSE 0 END, g.NearestDue",
        ["open"] = "g.OpenRequests",
        ["observed"] = "CASE WHEN s.LastObservedOn IS NULL THEN 1 ELSE 0 END, s.LastObservedOn",
        ["owner"] = "CASE WHEN ot.Name IS NULL THEN 1 ELSE 0 END, ot.Name"
    };

    private static readonly Dictionary<string, string> _statusFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = "g.OpenRequests > 0",
        ["overdue"] = "g.HasOverdue = 1",
        ["awaiting"] = "g.HasAwaiting = 1",
        ["none"] = "g.OpenRequests = 0 AND s.LifecycleState = 'Active'",
        ["closed"] = "s.LifecycleState = 'ClosureVerified'",
        ["unowned"] = "s.CurrentOwnerTeamId IS NULL",
        ["proposed"] = "g.OwnershipProposed = 1",
        ["notseen"] = "s.LastObservationPresence = 'NotPresent'"
    };

    /// <summary>True when the sort or status filter is supported (whitelisted SQL fragments only).</summary>
    public static bool ValidListQuery(AccountListQuery query) => _sorts.ContainsKey(query.Sort) && (query.Status is null || _statusFilters.ContainsKey(query.Status));

    /// <summary>Server-paged scoped account list with stable ordering (sort key, then ID) before OFFSET.</summary>
    public Task<AccountPage> ListAccountsAsync(ServiceAccountScope scope, AccountListQuery query, DateOnly today, CancellationToken cancellationToken) =>
        RetryReadOnDeadlockAsync(() => ListAccountsOnceAsync(scope, query, today, cancellationToken));

    private async Task<AccountPage> ListAccountsOnceAsync(ServiceAccountScope scope, AccountListQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        string order = _sorts[query.Sort] + (query.Descending ? " DESC" : " ASC");
        string status = query.Status is null ? "1 = 1" : _statusFilters[query.Status];
        DynamicParameters parameters = ScopeParameters(scope, new
        {
            search = ServiceAccountText.AccountKey(query.Search) is { } key ? "%" + EscapeLike(key) + "%" : null,
            query.OrganizationId,
            query.TeamId,
            query.PersonId,
            domain = query.Domain is null ? null : ServiceAccountText.DomainKey(query.Domain),
            unknownDomain = string.Equals(query.Domain, "unknown", StringComparison.OrdinalIgnoreCase),
            myTeam = query.MyTeam,
            myTeams = System.Text.Json.JsonSerializer.Serialize(scope.DirectTeams.Select(t => t.ToString("D"))),
            query.DueBefore,
            today,
            skip = (query.Page - 1) * query.PageSize,
            query.PageSize
        });
        // The ORDER BY/status fragments come only from the whitelists above.
        string cte = $"""
            WITH Scoped AS (
                SELECT a.* FROM svcacct.Accounts a
                WHERE {ScopePredicate}
                  AND (@search IS NULL OR a.NormalizedName LIKE @search)
                  AND (@OrganizationId IS NULL OR a.ReportOrganizationId = @OrganizationId)
                  AND (@unknownDomain = 0 OR a.NormalizedDomain IS NULL)
                  AND (@domain IS NULL OR @unknownDomain = 1 OR a.NormalizedDomain = @domain)
                  AND (@TeamId IS NULL OR a.CurrentOwnerTeamId = @TeamId OR EXISTS (SELECT 1 FROM svcacct.WorkRequests tr
                        WHERE tr.AccountId = a.Id AND tr.Status = 'Open' AND tr.TargetTeamId = @TeamId))
                  AND (@PersonId IS NULL OR a.CurrentOwnerPersonId = @PersonId OR EXISTS (SELECT 1 FROM svcacct.WorkRequests pr
                        WHERE pr.AccountId = a.Id AND pr.Status = 'Open' AND pr.FollowupPersonId = @PersonId))
                  AND (@myTeam = 0 OR a.CurrentOwnerTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@myTeams))
                        OR EXISTS (SELECT 1 FROM svcacct.WorkRequests mr WHERE mr.AccountId = a.Id AND mr.Status = 'Open'
                            AND mr.TargetTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@myTeams)))
                        OR EXISTS (SELECT 1 FROM svcacct.Handovers mh WHERE mh.AccountId = a.Id AND mh.Status = 'Proposed'
                            AND mh.TargetTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@myTeams))))
            ), Agg AS (
                SELECT s.Id,
                    (SELECT COUNT(*) FROM svcacct.WorkRequests r WHERE r.AccountId = s.Id AND r.Status = 'Open') AS OpenRequests,
                    (SELECT MIN(COALESCE(r.PlanEnd, r.NextFollowupOn)) FROM svcacct.WorkRequests r WHERE r.AccountId = s.Id AND r.Status = 'Open') AS NearestDue,
                    CAST(CASE WHEN EXISTS (SELECT 1 FROM svcacct.WorkRequests r WHERE r.AccountId = s.Id AND r.Status = 'Open' AND r.PlanEnd < @today) THEN 1 ELSE 0 END AS bit) AS HasOverdue,
                    CAST(CASE WHEN EXISTS (SELECT 1 FROM svcacct.WorkRequests r WHERE r.AccountId = s.Id AND r.Status = 'Open'
                        AND (r.PlanStart IS NULL OR r.PlanEnd IS NULL OR r.ActionType = 'Evaluate')) THEN 1 ELSE 0 END AS bit) AS HasAwaiting,
                    CAST(CASE WHEN EXISTS (SELECT 1 FROM svcacct.OwnershipAssignments o WHERE o.AccountId = s.Id AND o.State = 'Proposed') THEN 1 ELSE 0 END AS bit) AS OwnershipProposed,
                    (SELECT TOP 1 r.FollowupPersonId FROM svcacct.WorkRequests r WHERE r.AccountId = s.Id AND r.Status = 'Open'
                        AND r.FollowupPersonId IS NOT NULL ORDER BY r.CreatedAt DESC, r.Id) AS FollowupPersonId
                FROM Scoped s
            )
            """;
        string sql = cte + $"""
            SELECT s.Id, s.AccountName, s.Domain, s.IdentityState, s.ReportOrganizationId, org.Name AS OrganizationName, s.CurrentOwnerTeamId,
                ot.Name AS OwnerTeamName, s.CurrentOwnerPersonId, op.DisplayName AS OwnerPersonName, op.VerificationState AS OwnerPersonState,
                g.FollowupPersonId, fp.DisplayName AS FollowupPersonName, g.OwnershipProposed, g.OpenRequests, g.NearestDue, g.HasOverdue,
                s.LifecycleState, s.LastObservedOn, s.LastObservationPresence, s.RowVer
            FROM Scoped s JOIN Agg g ON g.Id = s.Id
            LEFT JOIN svcacct.Organizations org ON org.Id = s.ReportOrganizationId
            LEFT JOIN svcacct.Teams ot ON ot.Id = s.CurrentOwnerTeamId
            LEFT JOIN svcacct.People op ON op.Id = s.CurrentOwnerPersonId
            LEFT JOIN svcacct.People fp ON fp.Id = g.FollowupPersonId
            WHERE {status} AND (@DueBefore IS NULL OR g.NearestDue <= @DueBefore)
            ORDER BY {order}, s.Id
            OFFSET @skip ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        string filter = $"WHERE {status} AND (@DueBefore IS NULL OR g.NearestDue <= @DueBefore)";
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        List<ListRow> rows = [.. await connection.QueryAsync<ListRow>(Cmd(sql, parameters, null, cancellationToken))];
        int total = await connection.ExecuteScalarAsync<int>(Cmd(cte + "\nSELECT COUNT(*) FROM Scoped s JOIN Agg g ON g.Id = s.Id " + filter + ";",
            parameters, null, cancellationToken));
        return new AccountPage([.. rows.Select(r => new AccountListItem(r.Id, r.AccountName, r.Domain, r.IdentityState,
            Ref(r.ReportOrganizationId, r.OrganizationName), Ref(r.CurrentOwnerTeamId, r.OwnerTeamName),
            Ref(r.CurrentOwnerPersonId, r.OwnerPersonName, r.OwnerPersonState),
            r.CurrentOwnerPersonId is null ? Ref(r.FollowupPersonId, r.FollowupPersonName, "Takipçi (sahip değil)") : null,
            r.OwnershipProposed, r.OpenRequests, r.NearestDue, Status(r.LifecycleState, r.HasOverdue, r.OpenRequests),
            r.LastObservedOn, r.LastObservationPresence, Version(r.RowVer)))], total, query.Page, query.PageSize);
    }

    private sealed record ListRow(Guid Id, string AccountName, string? Domain, string IdentityState, Guid? ReportOrganizationId, string? OrganizationName,
        Guid? CurrentOwnerTeamId, string? OwnerTeamName, Guid? CurrentOwnerPersonId, string? OwnerPersonName, string? OwnerPersonState,
        Guid? FollowupPersonId, string? FollowupPersonName, bool OwnershipProposed, int OpenRequests, DateOnly? NearestDue, bool HasOverdue,
        string LifecycleState, DateOnly? LastObservedOn, string? LastObservationPresence, byte[] RowVer);

    internal static SaRef? Ref(Guid? id, string? label, string? state = null) => id is { } value ? new SaRef(value, label ?? "—", state) : null;

    private static string Status(string lifecycle, bool overdue, int open) =>
        lifecycle == "ClosureVerified" ? "Kapanış doğrulandı" : overdue ? "Geciken iş" : open > 0 ? "Açık iş" : "Açık iş yok";

    /// <summary>Scope anchor, version, name and domain of one account (null when missing).</summary>
    public async Task<(AccountScopeAnchor Anchor, string Version, string Name, string? Domain)?> AnchorAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await AnchorAsync(connection, null, accountId, cancellationToken);
    }

    private static async Task<(AccountScopeAnchor Anchor, string Version, string Name, string? Domain)?> AnchorAsync(SqlConnection connection, SqlTransaction? transaction,
        Guid accountId, CancellationToken cancellationToken)
    {
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT ReportOrganizationId, CurrentOwnerTeamId, RowVer, AccountName, Domain FROM svcacct.Accounts WHERE Id = @accountId;
            SELECT TargetTeamId FROM svcacct.WorkRequests WHERE AccountId = @accountId AND Status = 'Open' AND TargetTeamId IS NOT NULL;
            SELECT TargetTeamId FROM svcacct.Handovers WHERE AccountId = @accountId AND Status IN ('Proposed','Accepted');
            """, new { accountId }, transaction, cancellationToken));
        (Guid? Org, Guid? Owner, byte[] RowVer, string Name, string? Domain)? account = await grid.ReadSingleOrDefaultAsync<(Guid? Org, Guid? Owner, byte[] RowVer, string Name, string? Domain)?>();
        if (account is not { } a)
        {
            return null;
        }

        Guid[] requestTeams = [.. await grid.ReadAsync<Guid>()];
        Guid[] handoverTeams = [.. await grid.ReadAsync<Guid>()];
        return (new AccountScopeAnchor(a.Org, a.Owner, requestTeams, handoverTeams), Version(a.RowVer), a.Name, a.Domain);
    }
}
