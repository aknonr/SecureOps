using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    private const int _summaryItems = 10;

    /// <summary>Scope-filtered entry summary: work targeted at the caller's direct teams and, for coordinators, scope-wide attention counts.</summary>
    public async Task<ServiceAccountWorkSummary> WorkSummaryAsync(ServiceAccountScope scope, DateOnly today, CancellationToken cancellationToken)
    {
        bool coordinator = scope.HasOrganizationLevel;
        DynamicParameters parameters = ScopeParameters(scope, new
        {
            today,
            myTeams = System.Text.Json.JsonSerializer.Serialize(scope.DirectTeams.Select(t => t.ToString("D"))),
            teamOnly = !coordinator,
            take = _summaryItems
        });
        // Only the closed ScopePredicate constant is interpolated.
        string sql = $"""
            DECLARE @mine TABLE (Id uniqueidentifier PRIMARY KEY);
            INSERT INTO @mine SELECT DISTINCT CONVERT(uniqueidentifier, value) FROM OPENJSON(@myTeams);
            DECLARE @scoped TABLE (Id uniqueidentifier PRIMARY KEY);
            INSERT INTO @scoped SELECT a.Id FROM svcacct.Accounts a WHERE {ScopePredicate};

            SELECT
                (SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN @scoped s ON s.Id = r.AccountId WHERE r.Status = 'Open' AND r.TargetTeamId IN (SELECT Id FROM @mine)) AS TeamOpen,
                (SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN @scoped s ON s.Id = r.AccountId WHERE r.Status = 'Open' AND r.TargetTeamId IN (SELECT Id FROM @mine) AND r.PlanEnd < @today) AS TeamOverdue,
                (SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN @scoped s ON s.Id = r.AccountId WHERE r.Status = 'Open' AND r.TargetTeamId IN (SELECT Id FROM @mine) AND r.NextFollowupOn <= @today) AS TeamFollowupDue,
                (SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN @scoped s ON s.Id = r.AccountId WHERE r.Status = 'Open' AND r.TargetTeamId IN (SELECT Id FROM @mine)
                    AND (r.PlanStart IS NULL OR r.PlanEnd IS NULL OR r.ActionType = 'Evaluate')) AS TeamAwaiting,
                (SELECT COUNT(*) FROM svcacct.Handovers h JOIN @scoped s ON s.Id = h.AccountId WHERE h.Status = 'Proposed' AND h.TargetTeamId IN (SELECT Id FROM @mine)) AS IncomingHandovers,
                (SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN @scoped s ON s.Id = r.AccountId WHERE r.Status = 'Open' AND r.PlanEnd < @today) AS ScopeOverdue,
                (SELECT COUNT(*) FROM svcacct.WorkRequests r JOIN @scoped s ON s.Id = r.AccountId WHERE r.Status = 'Open' AND r.NextFollowupOn <= @today) AS ScopeFollowupDue,
                (SELECT COUNT(DISTINCT o.AccountId) FROM svcacct.OwnershipAssignments o JOIN @scoped s ON s.Id = o.AccountId WHERE o.State = 'Proposed') AS ProposedOwnership,
                (SELECT COUNT(*) FROM svcacct.ActionEvents e JOIN @scoped s ON s.Id = e.AccountId WHERE e.Result = 'Performed' AND e.VoidedAt IS NULL) AS AwaitingVerification;

            SELECT TOP (@take) r.Id AS RequestId, r.AccountId, a.AccountName, r.ActionType, COALESCE(r.PlanEnd, r.NextFollowupOn) AS Due,
                CAST(CASE WHEN r.PlanEnd < @today THEN 1 ELSE 0 END AS bit) AS Overdue,
                CAST(CASE WHEN r.NextFollowupOn <= @today THEN 1 ELSE 0 END AS bit) AS FollowupDue, r.TargetTeamId, t.Name AS TargetTeamName
            FROM svcacct.WorkRequests r
            JOIN @scoped s ON s.Id = r.AccountId
            JOIN svcacct.Accounts a ON a.Id = r.AccountId
            LEFT JOIN svcacct.Teams t ON t.Id = r.TargetTeamId
            WHERE r.Status = 'Open' AND (@teamOnly = 0 OR r.TargetTeamId IN (SELECT Id FROM @mine))
            ORDER BY CASE WHEN r.PlanEnd < @today OR r.NextFollowupOn <= @today THEN 0 ELSE 1 END,
                CASE WHEN COALESCE(r.PlanEnd, r.NextFollowupOn) IS NULL THEN 1 ELSE 0 END, COALESCE(r.PlanEnd, r.NextFollowupOn), r.Id;
            """;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd(sql, parameters, null, cancellationToken));
        SummaryCounts counts = await grid.ReadSingleAsync<SummaryCounts>();
        SummaryRow[] rows = [.. await grid.ReadAsync<SummaryRow>()];
        return new ServiceAccountWorkSummary(today, scope.DirectTeams.Count > 0, counts.TeamOpen, counts.TeamOverdue, counts.TeamFollowupDue, counts.TeamAwaiting,
            counts.IncomingHandovers, coordinator, coordinator ? counts.ScopeOverdue : 0, coordinator ? counts.ScopeFollowupDue : 0,
            coordinator ? counts.ProposedOwnership : 0, coordinator ? counts.AwaitingVerification : 0,
            [.. rows.Select(r => new WorkSummaryItem(r.RequestId, r.AccountId, r.AccountName, r.ActionType,
                Enum.TryParse(r.ActionType, false, out ServiceAccountActionType type) ? ServiceAccountLabels.Action(type) : r.ActionType,
                r.Due, r.Overdue, r.FollowupDue, Ref(r.TargetTeamId, r.TargetTeamName)))]);
    }

    private sealed record SummaryCounts(int TeamOpen, int TeamOverdue, int TeamFollowupDue, int TeamAwaiting, int IncomingHandovers, int ScopeOverdue,
        int ScopeFollowupDue, int ProposedOwnership, int AwaitingVerification);

    private sealed record SummaryRow(Guid RequestId, Guid AccountId, string AccountName, string ActionType, DateOnly? Due, bool Overdue, bool FollowupDue,
        Guid? TargetTeamId, string? TargetTeamName);
}
