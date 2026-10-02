using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Stored immutable snapshot.</summary>
public sealed record SnapshotRecord(Guid Id, string Kind, DateOnly PeriodStart, DateOnly PeriodEnd, DateTimeOffset AsOf, string ScopeJson, string ScopeHash,
    string MetricDefinitionVersion, string InputWatermark, string PayloadJson, string PayloadSha256, string? Label, DateTimeOffset CreatedAt, Guid CreatedBy);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>
    /// Loads report inputs with the same scope predicate as every other query, plus optional organization/team
    /// filters applied in SQL. One communication is one row regardless of how many accounts it links. An account filter
    /// narrows the same facts to one account so the account detail evaluates rules with the report implementation.
    /// </summary>
    public async Task<(ReportFacts Facts, string Watermark)> ReportFactsAsync(ServiceAccountScope scope, Guid? organizationId, Guid? teamId,
        RiskThresholds thresholds, Guid? accountId, CancellationToken cancellationToken)
    {
        DynamicParameters parameters = ScopeParameters(scope, new { organizationId, teamId, accountId, coordination = ServiceAccountImportProfiles.CoordinationList });
        string accounts = $"""
            SELECT a.Id FROM svcacct.Accounts a WHERE {ScopePredicate}
              AND (@organizationId IS NULL OR a.ReportOrganizationId = @organizationId)
              AND (@accountId IS NULL OR a.Id = @accountId)
              AND (@teamId IS NULL OR a.CurrentOwnerTeamId = @teamId OR EXISTS (SELECT 1 FROM svcacct.WorkRequests tr WHERE tr.AccountId = a.Id AND tr.TargetTeamId = @teamId))
            """;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd($"""
            SELECT Id INTO #scoped FROM ({accounts}) s;
            SELECT a.Id, CASE WHEN a.Domain IS NULL THEN a.AccountName ELSE a.Domain + N'\' + a.AccountName END AS Label, a.CurrentOwnerTeamId AS OwnerTeamId, a.CurrentOwnerPersonId AS ConfirmedPersonId,
                (SELECT TOP 1 o.PersonId FROM svcacct.OwnershipAssignments o WHERE o.AccountId = a.Id AND o.State = 'Proposed' AND o.PersonId IS NOT NULL
                    ORDER BY o.ProposedAt DESC, o.Id) AS NamedPersonId
            FROM svcacct.Accounts a JOIN #scoped s ON s.Id = a.Id;
            SELECT r.Id, r.AccountId, r.ActionType, r.Status, r.TargetTeamId, r.PlanStart, r.PlanEnd, r.FollowupPersonId,
                CONCAT(COALESCE(r.LegacyDisplayId, N''), N'|', CONVERT(nvarchar(40), r.CreatedAt, 126), N'|', CONVERT(nvarchar(36), r.Id)) AS OrderKey,
                r.CreatedAt, r.ClosedAt
            FROM svcacct.WorkRequests r JOIN #scoped s ON s.Id = r.AccountId;
            SELECT e.Id, e.AccountId, e.ActionType, e.Result, e.RecordKind, e.ActualOn, e.VerifiedOn,
                CAST(CASE WHEN e.VerifiedByUserId IS NOT NULL OR e.VerifiedByPersonId IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasVerifier,
                CAST(CASE WHEN NULLIF(e.EvidenceNote, N'') IS NOT NULL OR NULLIF(e.VerificationNote, N'') IS NOT NULL OR e.VerificationEvidenceId IS NOT NULL
                    OR EXISTS (SELECT 1 FROM svcacct.Evidence x WHERE x.OwnerEntityType = 'Action' AND x.OwnerEntityId = e.Id) THEN 1 ELSE 0 END AS bit) AS HasEvidence,
                CAST(CASE WHEN EXISTS (SELECT 1 FROM svcacct.ExternalRecordLinks l JOIN svcacct.ExternalRecords x ON x.Id = l.ExternalRecordId
                    WHERE l.EntityType = 'Action' AND l.EntityId = e.Id AND x.RecordType = 'OR') THEN 1 ELSE 0 END AS bit) AS HasOr,
                CAST(CASE WHEN e.VoidedAt IS NOT NULL THEN 1 ELSE 0 END AS bit) AS Voided, e.ActualPrecision, e.ActualAt, e.PerformerTeamId
            FROM svcacct.ActionEvents e JOIN #scoped s ON s.Id = e.AccountId;
            SELECT c.Id, c.Direction, c.Kind, c.Precision, c.OccurredOn, c.OccurredAt,
                (SELECT COUNT(*) FROM svcacct.CommunicationAccounts ca JOIN #scoped s ON s.Id = ca.AccountId WHERE ca.CommunicationId = c.Id) AS Links
            FROM svcacct.Communications c
            WHERE EXISTS (SELECT 1 FROM svcacct.CommunicationAccounts ca JOIN #scoped s ON s.Id = ca.AccountId WHERE ca.CommunicationId = c.Id)
               OR (NOT EXISTS (SELECT 1 FROM svcacct.CommunicationAccounts ca WHERE ca.CommunicationId = c.Id) AND @organizationId IS NULL
                   AND (@ScopeAll = 1 OR c.ContactTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ScopeTeams)))
                   AND (@teamId IS NULL OR c.ContactTeamId = @teamId));
            SELECT h.AccountId, h.Status,
                CAST(CASE WHEN h.Status = 'Accepted' AND h.DecidedOn IS NOT NULL AND h.DecidedBy IS NOT NULL AND NULLIF(h.DecisionNote, N'') IS NOT NULL THEN 1 ELSE 0 END AS bit)
            FROM svcacct.Handovers h JOIN #scoped s ON s.Id = h.AccountId;
            SELECT t.AccountId, t.Suitability,
                CAST(CASE WHEN EXISTS (SELECT 1 FROM svcacct.ActionEvents e WHERE e.Id = t.CompletedActionId AND e.ActionType = 'GmsaConversion'
                    AND e.Result IN ('Performed','Verified') AND e.VoidedAt IS NULL) THEN 1 ELSE 0 END AS bit)
            FROM svcacct.IdentityTransitions t JOIN #scoped s ON s.Id = t.AccountId WHERE t.Target = 'gMSA';
            SELECT f.AccountId, f.Status FROM svcacct.Findings f JOIN #scoped s ON s.Id = f.AccountId;
            SELECT Id, Name FROM svcacct.Teams;
            SELECT CONCAT(N'h', COALESCE(MAX(Id), 0)) FROM svcacct.History;
            SELECT u.Id, u.AccountId, u.UsageKind, u.DatabaseEngine, u.NeedVerified, CAST(CASE WHEN u.ExceptionReason IS NULL THEN 0 ELSE 1 END AS bit)
            FROM svcacct.AccountUsages u JOIN #scoped s ON s.Id = u.AccountId WHERE u.RemovedAt IS NULL;
            SELECT a.Id FROM svcacct.Accounts a JOIN #scoped s ON s.Id = a.Id
            WHERE a.CurrentOwnerTeamId IN (SELECT TeamId FROM svcacct.TeamRoles WHERE Role = 'SqlTeam' AND RevokedAt IS NULL)
               OR EXISTS (SELECT 1 FROM svcacct.Handovers h WHERE h.AccountId = a.Id AND h.Status IN ('Proposed','Accepted')
                          AND h.SourceTeamId IN (SELECT TeamId FROM svcacct.TeamRoles WHERE Role = 'SqlTeam' AND RevokedAt IS NULL));
            SELECT TOP (1) t.Name FROM svcacct.TeamRoles r JOIN svcacct.Teams t ON t.Id = r.TeamId WHERE r.Role = 'GmsaExecutor' AND r.RevokedAt IS NULL;
            SELECT o.AccountId, o.SourceReportDate, CAST(CASE WHEN o.Presence = 'Present' THEN 1 ELSE 0 END AS bit), o.PasswordLastSet, o.LastLogonAdOrLdap, o.LastLogonAd
            FROM (SELECT x.*, ROW_NUMBER() OVER (PARTITION BY x.AccountId ORDER BY CASE WHEN x.SourceReportDate IS NULL THEN 1 ELSE 0 END, x.SourceReportDate DESC,
                    x.RecordedAt DESC, x.Id) AS n
                  FROM svcacct.AccountObservations x JOIN #scoped s ON s.Id = x.AccountId WHERE x.SourceProfile = @coordination) o
            WHERE o.n = 1;
            SELECT a.Id FROM svcacct.Accounts a JOIN #scoped s ON s.Id = a.Id WHERE a.LifecycleState = 'ClosureVerified';
            SELECT t.Id, o.Name FROM svcacct.Teams t JOIN svcacct.Organizations o ON o.Id = t.OrganizationId;
            DROP TABLE #scoped;
            """, parameters, null, cancellationToken, _commitTimeoutSeconds));
        AccountFact[] accountFacts = [.. await grid.ReadAsync<AccountFact>()];
        RequestFact[] requests = [.. (await grid.ReadAsync<RequestFactRow>())
            .Select(r => new RequestFact(r.Id, r.AccountId, Enum.Parse<ServiceAccountActionType>(r.ActionType), Enum.Parse<ServiceAccountRequestStatus>(r.Status),
                r.TargetTeamId, r.PlanStart, r.PlanEnd, r.FollowupPersonId, r.OrderKey, r.CreatedAt, r.ClosedAt))];
        ActionFact[] actions = [.. (await grid.ReadAsync<ActionFactRow>()).Select(a => new ActionFact(a.Id, a.AccountId,
            new ActionFacts(Enum.Parse<ServiceAccountActionType>(a.ActionType), Enum.Parse<ServiceAccountActionResult>(a.Result),
                Enum.Parse<ServiceAccountRecordKind>(a.RecordKind), a.ActualOn, a.VerifiedOn, a.HasVerifier, a.HasEvidence, a.HasOr, a.Voided),
            Enum.Parse<TimePrecision>(a.ActualPrecision), a.ActualAt, a.PerformerTeamId))];
        CommunicationFact[] communications = [.. (await grid.ReadAsync<(Guid Id, string Direction, string Kind, string Precision, DateOnly? On, DateTimeOffset? At, int Links)>())
            .Select(c => new CommunicationFact(c.Id, Enum.Parse<CommunicationDirection>(c.Direction), Enum.Parse<CommunicationKind>(c.Kind),
                Enum.Parse<TimePrecision>(c.Precision), c.On, c.At, c.Links))];
        HandoverFact[] handovers = [.. (await grid.ReadAsync<(Guid AccountId, string Status, bool Evidence)>())
            .Select(h => new HandoverFact(h.AccountId, Enum.Parse<HandoverStatus>(h.Status), h.Evidence))];
        TransitionFact[] transitions = [.. (await grid.ReadAsync<(Guid AccountId, string Suitability, bool Completed)>())
            .Select(t => new TransitionFact(t.AccountId, Enum.Parse<GmsaSuitability>(t.Suitability), t.Completed))];
        FindingFact[] findings = [.. (await grid.ReadAsync<(Guid AccountId, string Status)>()).Select(f => new FindingFact(f.AccountId, Enum.Parse<FindingStatus>(f.Status)))];
        var teams = (await grid.ReadAsync<(Guid Id, string Name)>()).ToDictionary(t => t.Id, t => t.Name);
        string watermark = await grid.ReadSingleAsync<string>();
        UsageFact[] usages = [.. (await grid.ReadAsync<(Guid Id, Guid AccountId, string Kind, string? Engine, bool? NeedVerified, bool Excepted)>())
            .Select(u => new UsageFact(u.Id, u.AccountId, Enum.Parse<UsageKind>(u.Kind), u.Engine is null ? null : Enum.Parse<DatabaseEngine>(u.Engine), u.NeedVerified,
                u.Excepted))];
        HashSet<Guid> sqlTeamAccounts = [.. await grid.ReadAsync<Guid>()];
        string? executor = await grid.ReadSingleOrDefaultAsync<string>();
        ObservationFact[] observations = [.. (await grid.ReadAsync<(Guid AccountId, DateOnly? Source, bool Present, DateTime? Password, DateTime? LogonAny, DateTime? LogonAd)>())
            .Select(o => new ObservationFact(o.AccountId, o.Source, o.Present, o.Password, o.LogonAny, o.LogonAd))];
        HashSet<Guid> closed = [.. await grid.ReadAsync<Guid>()];
        var organizations = (await grid.ReadAsync<(Guid Id, string Name)>()).ToDictionary(t => t.Id, t => t.Name);
        InsightFacts insight = new(usages, sqlTeamAccounts, executor, observations, closed, organizations, thresholds);
        return (new ReportFacts(accountFacts, requests, actions, communications, handovers, transitions, findings, teams, insight), watermark);
    }

    private sealed record RequestFactRow(Guid Id, Guid AccountId, string ActionType, string Status, Guid? TargetTeamId, DateOnly? PlanStart, DateOnly? PlanEnd,
        Guid? FollowupPersonId, string OrderKey, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt);

    private sealed record ActionFactRow(Guid Id, Guid AccountId, string ActionType, string Result, string RecordKind, DateOnly? ActualOn, DateOnly? VerifiedOn,
        bool HasVerifier, bool HasEvidence, bool HasOr, bool Voided, string ActualPrecision, DateTimeOffset? ActualAt, Guid? PerformerTeamId);

    /// <summary>Stores an immutable snapshot and its audit in one transaction.</summary>
    public async Task SaveSnapshotAsync(SnapshotRecord snapshot, SaActor actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken);
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ReportSnapshots(Id, Kind, PeriodStart, PeriodEnd, AsOf, ScopeJson, ScopeHash, MetricDefinitionVersion, InputWatermark, PayloadJson,
                PayloadSha256, Label, CreatedAt, CreatedBy)
            VALUES(@Id, @Kind, @PeriodStart, @PeriodEnd, @AsOf, @ScopeJson, @ScopeHash, @MetricDefinitionVersion, @InputWatermark, @PayloadJson, @PayloadSha256,
                @Label, @CreatedAt, @CreatedBy);
            """, snapshot, transaction, cancellationToken));
        await AuditAsync(connection, transaction, "ReportSnapshotCreated", new { SnapshotId = snapshot.Id, snapshot.Kind, snapshot.PeriodStart, snapshot.PayloadSha256 },
            actor, snapshot.CreatedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Reads one snapshot.</summary>
    public async Task<SnapshotRecord?> SnapshotAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SnapshotRecord>(Cmd("""
            SELECT Id, Kind, PeriodStart, PeriodEnd, AsOf, ScopeJson, ScopeHash, MetricDefinitionVersion, InputWatermark, PayloadJson, PayloadSha256, Label, CreatedAt, CreatedBy
            FROM svcacct.ReportSnapshots WHERE Id = @id;
            """, new { id }, null, cancellationToken));
    }

    /// <summary>Lists recent snapshots (metadata only; the service filters by scope coverage).</summary>
    public async Task<IReadOnlyList<SnapshotRecord>> SnapshotsAsync(int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<SnapshotRecord>(Cmd("""
            SELECT TOP (@take) Id, Kind, PeriodStart, PeriodEnd, AsOf, ScopeJson, ScopeHash, MetricDefinitionVersion, InputWatermark, N'{}' AS PayloadJson,
                PayloadSha256, Label, CreatedAt, CreatedBy
            FROM svcacct.ReportSnapshots ORDER BY CreatedAt DESC, Id;
            """, new { take }, null, cancellationToken))];
    }
}
