using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    private const int _historyLimit = 200;
    private const int _observationLimit = 60;
    private const int _sourceRowLimit = 60;

    /// <summary>Full account detail (caller has already passed the scope check). Permissions are added by the service.</summary>
    public async Task<AccountDetail?> AccountDetailAsync(Guid id, DateOnly today, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        bool names = await GmsaNameColumnsAsync(connection, null, cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd($"""
            SELECT a.Id, a.AccountName, a.Domain, a.Sid, a.IdentityState, a.ReportOrganizationId, o.Name AS OrganizationName, a.CurrentOwnerTeamId,
                t.Name AS OwnerTeamName, a.CurrentOwnerPersonId, p.DisplayName AS OwnerPersonName, p.VerificationState AS OwnerPersonState,
                a.ConsumerTeamId, ct.Name AS ConsumerTeamName, a.LifecycleState, a.Notes, a.LastObservedOn, a.LastObservationPresence, a.LegacyDisplayId, a.RowVer
            FROM svcacct.Accounts a LEFT JOIN svcacct.Organizations o ON o.Id = a.ReportOrganizationId
            LEFT JOIN svcacct.Teams t ON t.Id = a.CurrentOwnerTeamId LEFT JOIN svcacct.People p ON p.Id = a.CurrentOwnerPersonId
            LEFT JOIN svcacct.Teams ct ON ct.Id = a.ConsumerTeamId WHERE a.Id = @id;
            SELECT l.EntityType, l.EntityId, r.RecordType, r.Number, r.Url FROM svcacct.ExternalRecordLinks l
            JOIN svcacct.ExternalRecords r ON r.Id = l.ExternalRecordId WHERE l.AccountId = @id OR (l.EntityType = 'Account' AND l.EntityId = @id);
            SELECT w.Id, w.TeamId, t.Name AS TeamName, w.PersonId, p.DisplayName AS PersonName, p.VerificationState AS PersonState, w.State, w.Source,
                w.EffectiveFrom, w.EffectiveTo, w.EvidenceNote, w.ProposedAt, w.DecidedAt, w.DecisionReason, w.RowVer
            FROM svcacct.OwnershipAssignments w LEFT JOIN svcacct.Teams t ON t.Id = w.TeamId LEFT JOIN svcacct.People p ON p.Id = w.PersonId
            WHERE w.AccountId = @id ORDER BY w.ProposedAt DESC, w.Id;
            SELECT r.Id, r.ActionType, r.Status, r.CloseOutcome, r.TargetTeamId, t.Name AS TargetTeamName, r.FollowupPersonId, f.DisplayName AS FollowupName,
                r.ContactPersonId, c.DisplayName AS ContactName, r.PlanStart, r.PlanEnd, r.PlanAnnouncedOn, r.NextFollowupOn, r.FirstSentOn, r.LastReplyOn,
                r.Notes, r.LegacyDisplayId, r.CloseReason, r.RowVer, {GmsaNameColumn("r", names)}
            FROM svcacct.WorkRequests r LEFT JOIN svcacct.Teams t ON t.Id = r.TargetTeamId LEFT JOIN svcacct.People f ON f.Id = r.FollowupPersonId
            LEFT JOIN svcacct.People c ON c.Id = r.ContactPersonId WHERE r.AccountId = @id
            ORDER BY CASE WHEN r.Status = 'Open' THEN 0 ELSE 1 END, CASE WHEN r.PlanEnd IS NULL THEN 1 ELSE 0 END, r.PlanEnd, r.CreatedAt, r.Id;
            SELECT e.Id, e.RequestId, e.ActionType, e.Result, e.RecordKind, e.ActualOn, e.ActualAt, e.ActualPrecision, e.PerformerTeamId, t.Name AS PerformerTeamName,
                e.PerformerPersonId, pp.DisplayName AS PerformerName, e.EvidenceNote, e.VerifiedOn, e.VerifiedByPersonId, vp.DisplayName AS VerifierName,
                e.VerifiedByUserId, e.VerificationNote, e.VerificationEvidenceId, e.VoidedAt, e.VoidReason, e.SourceNote, e.LegacyDisplayId, e.RowVer
            FROM svcacct.ActionEvents e LEFT JOIN svcacct.Teams t ON t.Id = e.PerformerTeamId LEFT JOIN svcacct.People pp ON pp.Id = e.PerformerPersonId
            LEFT JOIN svcacct.People vp ON vp.Id = e.VerifiedByPersonId WHERE e.AccountId = @id
            ORDER BY CASE WHEN e.ActualOn IS NULL THEN 1 ELSE 0 END, e.ActualOn DESC, e.CreatedAt DESC, e.Id;
            SELECT c.Id, c.Direction, c.Kind, c.OccurredOn, c.OccurredAt, c.Precision, c.ContactTeamId, t.Name AS ContactTeamName, c.ContactPersonId,
                p.DisplayName AS ContactPersonName, c.Subject, c.Summary, c.Link, c.RecordScope, c.MeaningfulReply,
                CAST(CASE WHEN c.ProviderMessageId IS NULL THEN 0 ELSE 1 END AS bit) AS HasProvider, c.RowVer
            FROM svcacct.Communications c LEFT JOIN svcacct.Teams t ON t.Id = c.ContactTeamId LEFT JOIN svcacct.People p ON p.Id = c.ContactPersonId
            WHERE EXISTS (SELECT 1 FROM svcacct.CommunicationAccounts ca WHERE ca.CommunicationId = c.Id AND ca.AccountId = @id)
            ORDER BY CASE WHEN c.OccurredOn IS NULL THEN 1 ELSE 0 END, c.OccurredOn DESC, c.CreatedAt DESC, c.Id;
            SELECT ca.CommunicationId, a.Id, a.AccountName FROM svcacct.CommunicationAccounts ca JOIN svcacct.Accounts a ON a.Id = ca.AccountId
            WHERE ca.CommunicationId IN (SELECT CommunicationId FROM svcacct.CommunicationAccounts WHERE AccountId = @id);
            SELECT f.Id, f.Server, f.ComponentType, f.ComponentName, f.Environment, f.ScanAt, f.ScanOn, f.ScanResult, f.MatchResult, f.CoverageWindow,
                f.EvidenceNote, f.OwningTeamId, t.Name AS OwningTeamName, f.Status, f.JobReference, f.Notes, f.RowVer
            FROM svcacct.Findings f LEFT JOIN svcacct.Teams t ON t.Id = f.OwningTeamId WHERE f.AccountId = @id ORDER BY f.CreatedAt DESC, f.Id;
            SELECT TOP ({_observationLimit}) o.Id, o.BatchId, o.SourceProfile, o.SourceReportDate, o.Presence, o.PasswordLastSet, o.LastLogonAdOrLdap,
                o.LastLogonAd, o.Organization, o.GroupDirectorate, o.Comment, o.SourceTeam, o.ConsumerTeam, o.HandoverFlag, o.SourceRow, o.RecordedAt
            FROM svcacct.AccountObservations o WHERE o.AccountId = @id ORDER BY o.SourceReportDate DESC, o.RecordedAt DESC, o.Id;
            SELECT h.Id, h.SourceTeamId, st.Name AS SourceTeamName, h.TargetTeamId, tt.Name AS TargetTeamName, h.ConsumerTeamId, ct.Name AS ConsumerTeamName,
                h.CohortLabel, h.ProposedOn, h.Status, h.DecidedOn, h.DecisionNote, h.SourceNote, h.RowVer
            FROM svcacct.Handovers h LEFT JOIN svcacct.Teams st ON st.Id = h.SourceTeamId JOIN svcacct.Teams tt ON tt.Id = h.TargetTeamId
            LEFT JOIN svcacct.Teams ct ON ct.Id = h.ConsumerTeamId WHERE h.AccountId = @id ORDER BY h.CreatedAt DESC, h.Id;
            SELECT i.Id, i.HandoverId, i.Target, i.Suitability, i.DecisionNote, i.PlannedOn, i.CompletedActionId, i.RowVer, {GmsaNameColumn("i", names)}
            FROM svcacct.IdentityTransitions i WHERE i.AccountId = @id;
            SELECT e.Id, e.OwnerEntityType, e.OwnerEntityId, e.FileName, e.ContentType, e.SizeBytes, e.Sha256, e.Label, e.CreatedAt
            FROM svcacct.Evidence e WHERE e.AccountId = @id ORDER BY e.CreatedAt DESC, e.Id;
            SELECT TOP ({_sourceRowLimit}) r.BatchId, b.Profile, b.FileName, b.SourceReportDate, r.Sheet, r.RowNumber, r.EntityKind, r.Classification, r.OriginalJson
            FROM svcacct.ImportRows r JOIN svcacct.ImportBatches b ON b.Id = r.BatchId
            WHERE r.MatchAccountId = @id AND b.Status = 'Committed' ORDER BY b.CommittedAt DESC, r.RowKey;
            SELECT TOP ({_historyLimit}) h.EntityType, h.EntityId, h.Action, h.ChangesJson, h.Reason,
                COALESCE(u.DisplayName, u.LoginName, 'Kullanıcı') AS Actor, h.OccurredAt
            FROM svcacct.History h LEFT JOIN security.Users u ON u.UserId = h.ActorUserId
            WHERE h.AccountId = @id ORDER BY h.OccurredAt DESC, h.Id DESC;
            """, new { id }, null, cancellationToken));
        AccountRow? account = await grid.ReadSingleOrDefaultAsync<AccountRow>();
        if (account is null)
        {
            return null;
        }

        var refs = (await grid.ReadAsync<(string EntityType, Guid EntityId, string Type, string Number, string? Url)>()).ToList();
        IReadOnlyList<SaExternalRef> Refs(string type, Guid entity) =>
            [.. refs.Where(r => r.EntityType == type && r.EntityId == entity).Select(r => new SaExternalRef(r.Type, r.Number, r.Url))];
        OwnershipView[] ownership = [.. (await grid.ReadAsync<OwnershipRow>()).Select(o => new OwnershipView(o.Id, Ref(o.TeamId, o.TeamName),
            Ref(o.PersonId, o.PersonName, o.PersonState), o.State, o.Source, o.EffectiveFrom, o.EffectiveTo, o.EvidenceNote, o.ProposedAt, o.DecidedAt,
            o.DecisionReason, Version(o.RowVer)))];
        RequestView[] requests = [.. (await grid.ReadAsync<RequestRow>()).Select(r => RequestView(r, account.Id, account.AccountName, today, Refs("Request", r.Id)))];
        List<ActionRow> actionRows = [.. await grid.ReadAsync<ActionRow>()];
        HashSet<Guid> orLinkedActions = [.. refs.Where(r => r.EntityType == "Action" && r.Type == "OR").Select(r => r.EntityId)];
        ActionView[] actions = [.. actionRows.Select(a => ActionView(a, account.Id, orLinkedActions.Contains(a.Id), Refs("Action", a.Id)))];
        List<CommunicationRow> communicationRows = [.. await grid.ReadAsync<CommunicationRow>()];
        ILookup<Guid, SaRef> linked = (await grid.ReadAsync<(Guid CommunicationId, Guid Id, string Name)>())
            .ToLookup(l => l.CommunicationId, l => new SaRef(l.Id, l.Name));
        CommunicationView[] communications = [.. communicationRows.Select(c => CommunicationView(c, [.. linked[c.Id]]))];
        FindingView[] findings = [.. (await grid.ReadAsync<FindingRow>()).Select(f => FindingView(f, account.Id))];
        ObservationView[] observations = [.. await grid.ReadAsync<ObservationView>()];
        HandoverView[] handovers = [.. (await grid.ReadAsync<HandoverRow>()).Select(h => new HandoverView(h.Id, account.Id, account.AccountName,
            Ref(h.SourceTeamId, h.SourceTeamName), new SaRef(h.TargetTeamId, h.TargetTeamName), Ref(h.ConsumerTeamId, h.ConsumerTeamName), h.CohortLabel,
            h.ProposedOn, h.Status, h.DecidedOn, h.DecisionNote, h.SourceNote, Version(h.RowVer)))];
        TransitionView[] transitions = [.. (await grid.ReadAsync<TransitionRow>()).Select(t => new TransitionView(t.Id, account.Id, t.HandoverId, t.Target,
            t.Suitability, t.DecisionNote, t.PlannedOn, t.CompletedActionId, Version(t.RowVer), t.RequestedGmsaName))];
        EvidenceView[] evidence = [.. await grid.ReadAsync<EvidenceView>()];
        SourceRowView[] sources = [.. await grid.ReadAsync<SourceRowView>()];
        HistoryView[] history = [.. await grid.ReadAsync<HistoryView>()];
        AccountSummaryView summary = new(account.Id, account.AccountName, account.Domain, account.Sid, account.IdentityState,
            Ref(account.ReportOrganizationId, account.OrganizationName), Ref(account.CurrentOwnerTeamId, account.OwnerTeamName),
            Ref(account.CurrentOwnerPersonId, account.OwnerPersonName, account.OwnerPersonState), Ref(account.ConsumerTeamId, account.ConsumerTeamName),
            account.LifecycleState, account.Notes, account.LastObservedOn, account.LastObservationPresence,
            requests.Count(r => r.Status == "Open"), requests.Where(r => r.Status == "Open").Select(r => r.PlanEnd ?? r.NextFollowupOn).Where(d => d is not null).Min(),
            Refs("Account", account.Id), account.LegacyDisplayId, Version(account.RowVer));
        return new AccountDetail(summary, ownership, requests, actions, communications, findings, observations, handovers, transitions, evidence, sources,
            history, new AccountPermissions(false, false, false, false, false, false))
        { RequestedGmsaNameAvailable = names };
    }

    internal static RequestView RequestView(RequestRow r, Guid accountId, string accountName, DateOnly today, IReadOnlyList<SaExternalRef> references)
    {
        ServiceAccountActionType type = Enum.Parse<ServiceAccountActionType>(r.ActionType);
        bool open = r.Status == "Open";
        return new RequestView(r.Id, accountId, accountName, r.ActionType, ServiceAccountLabels.Action(type), r.Status, r.CloseOutcome,
            Ref(r.TargetTeamId, r.TargetTeamName), Ref(r.FollowupPersonId, r.FollowupName), Ref(r.ContactPersonId, r.ContactName), r.PlanStart, r.PlanEnd,
            r.PlanAnnouncedOn, r.NextFollowupOn, r.FirstSentOn, r.LastReplyOn, r.Notes, open && r.PlanEnd is { } end && end < today,
            open && (r.PlanStart is null || r.PlanEnd is null || type == ServiceAccountActionType.Evaluate), references, r.LegacyDisplayId, r.CloseReason,
            Version(r.RowVer), r.RequestedGmsaName);
    }

    internal static ActionView ActionView(ActionRow a, Guid accountId, bool hasOr, IReadOnlyList<SaExternalRef> references)
    {
        ServiceAccountActionType type = Enum.Parse<ServiceAccountActionType>(a.ActionType);
        ServiceAccountActionResult result = Enum.Parse<ServiceAccountActionResult>(a.Result);
        ActionFacts facts = new(type, result, Enum.Parse<ServiceAccountRecordKind>(a.RecordKind), a.ActualOn, a.VerifiedOn,
            a.VerifiedByUserId is not null || a.VerifiedByPersonId is not null,
            !string.IsNullOrWhiteSpace(a.EvidenceNote) || !string.IsNullOrWhiteSpace(a.VerificationNote) || a.VerificationEvidenceId is not null, hasOr, a.VoidedAt is not null);
        return new ActionView(a.Id, accountId, a.RequestId, a.ActionType, ServiceAccountLabels.Action(type), a.Result, ServiceAccountLabels.Result(result),
            a.RecordKind, a.ActualOn, a.ActualAt, a.ActualPrecision, Ref(a.PerformerTeamId, a.PerformerTeamName), Ref(a.PerformerPersonId, a.PerformerName),
            a.EvidenceNote, a.VerifiedOn, Ref(a.VerifiedByPersonId, a.VerifierName), a.VerificationNote, ServiceAccountRules.IsVerifiedClosure(facts),
            a.VoidedAt is not null, a.VoidReason, a.SourceNote, references, a.LegacyDisplayId, Version(a.RowVer));
    }

    internal static CommunicationView CommunicationView(CommunicationRow c, IReadOnlyList<SaRef> accounts) =>
        new(c.Id, c.Direction, c.Kind, c.OccurredOn, c.OccurredAt, c.Precision, Ref(c.ContactTeamId, c.ContactTeamName), Ref(c.ContactPersonId, c.ContactPersonName),
            c.Subject, c.Summary, c.Link, c.RecordScope, c.MeaningfulReply, accounts, c.HasProvider, Version(c.RowVer));

    internal static FindingView FindingView(FindingRow f, Guid accountId)
    {
        List<string> gaps = [];
        if (f.ScanResult is not "Success")
        {
            gaps.Add("Tarama başarılı değil: kullanılmadığı sonucu çıkarılamaz.");
        }

        if (string.IsNullOrWhiteSpace(f.CoverageWindow))
        {
            gaps.Add("Kapsam / zaman aralığı eksik.");
        }

        if (string.IsNullOrWhiteSpace(f.EvidenceNote))
        {
            gaps.Add("Kanıt eksik.");
        }

        if (f.MatchResult == "NoMatch")
        {
            gaps.Add("Eşleşme yok: hesap silinebilir anlamına gelmez.");
        }

        return new FindingView(f.Id, accountId, f.Server, f.ComponentType, f.ComponentName, f.Environment, f.ScanAt, f.ScanOn, f.ScanResult, f.MatchResult,
            f.CoverageWindow, f.EvidenceNote, Ref(f.OwningTeamId, f.OwningTeamName), f.Status, f.JobReference, f.Notes, gaps, Version(f.RowVer));
    }

    private sealed record AccountRow(Guid Id, string AccountName, string? Domain, string? Sid, string IdentityState, Guid? ReportOrganizationId,
        string? OrganizationName, Guid? CurrentOwnerTeamId, string? OwnerTeamName, Guid? CurrentOwnerPersonId, string? OwnerPersonName, string? OwnerPersonState,
        Guid? ConsumerTeamId, string? ConsumerTeamName, string LifecycleState, string? Notes, DateOnly? LastObservedOn, string? LastObservationPresence,
        string? LegacyDisplayId, byte[] RowVer);

    private sealed record OwnershipRow(Guid Id, Guid? TeamId, string? TeamName, Guid? PersonId, string? PersonName, string? PersonState, string State,
        string Source, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? EvidenceNote, DateTimeOffset ProposedAt, DateTimeOffset? DecidedAt,
        string? DecisionReason, byte[] RowVer);

    internal sealed record RequestRow(Guid Id, string ActionType, string Status, string? CloseOutcome, Guid? TargetTeamId, string? TargetTeamName,
        Guid? FollowupPersonId, string? FollowupName, Guid? ContactPersonId, string? ContactName, DateOnly? PlanStart, DateOnly? PlanEnd,
        DateOnly? PlanAnnouncedOn, DateOnly? NextFollowupOn, DateOnly? FirstSentOn, DateOnly? LastReplyOn, string? Notes, string? LegacyDisplayId,
        string? CloseReason, byte[] RowVer, string? RequestedGmsaName = null);

    internal sealed record ActionRow(Guid Id, Guid? RequestId, string ActionType, string Result, string RecordKind, DateOnly? ActualOn, DateTimeOffset? ActualAt,
        string ActualPrecision, Guid? PerformerTeamId, string? PerformerTeamName, Guid? PerformerPersonId, string? PerformerName, string? EvidenceNote,
        DateOnly? VerifiedOn, Guid? VerifiedByPersonId, string? VerifierName, Guid? VerifiedByUserId, string? VerificationNote, Guid? VerificationEvidenceId,
        DateTimeOffset? VoidedAt, string? VoidReason, string? SourceNote, string? LegacyDisplayId, byte[] RowVer);

    internal sealed record CommunicationRow(Guid Id, string Direction, string Kind, DateOnly? OccurredOn, DateTimeOffset? OccurredAt, string Precision,
        Guid? ContactTeamId, string? ContactTeamName, Guid? ContactPersonId, string? ContactPersonName, string? Subject, string? Summary, string? Link,
        string RecordScope, bool? MeaningfulReply, bool HasProvider, byte[] RowVer);

    internal sealed record FindingRow(Guid Id, string? Server, string? ComponentType, string? ComponentName, string? Environment, DateTimeOffset? ScanAt,
        DateOnly? ScanOn, string ScanResult, string MatchResult, string? CoverageWindow, string? EvidenceNote, Guid? OwningTeamId, string? OwningTeamName,
        string Status, string? JobReference, string? Notes, byte[] RowVer);

    private sealed record HandoverRow(Guid Id, Guid? SourceTeamId, string? SourceTeamName, Guid TargetTeamId, string TargetTeamName, Guid? ConsumerTeamId,
        string? ConsumerTeamName, string? CohortLabel, DateOnly? ProposedOn, string Status, DateOnly? DecidedOn, string? DecisionNote, string? SourceNote, byte[] RowVer);

    private sealed record TransitionRow(Guid Id, Guid? HandoverId, string Target, string Suitability, string? DecisionNote, DateOnly? PlannedOn,
        Guid? CompletedActionId, byte[] RowVer, string? RequestedGmsaName = null);
}
