namespace SecureOps.Domain.ServiceAccounts;

/// <summary>
/// The single metric definition implementation (SPEC "Metric definitions"). Inputs are already
/// scope-filtered by the caller's server-side scope; this class never widens them.
/// </summary>
public static class ServiceAccountMetrics
{
    /// <summary>Version stamped on every live report and snapshot (v2 adds rules, gMSA funnel, trend, risk and directorate sections).</summary>
    public const string DefinitionVersion = "sa-metrics-v2";

    private const int _maxDetailLines = 500;

    /// <summary>Computes the report for the week starting on the Monday of <paramref name="weekStart"/>.</summary>
    public static ServiceAccountReport Compute(ReportFacts facts, DateOnly weekStart, DateTimeOffset asOf, string scopeLabel) =>
        Compute(facts, ReportCalendar.WeekStart(weekStart), ReportCalendar.WeekStart(weekStart).AddDays(7), asOf, scopeLabel, ReportPeriods.Week);

    /// <summary>Computes the report for the business-date period [<paramref name="start"/>, <paramref name="endExclusive"/>).</summary>
    public static ServiceAccountReport Compute(ReportFacts facts, DateOnly start, DateOnly endExclusive, DateTimeOffset asOf, string scopeLabel, string period)
    {
        DateOnly monday = start;
        DateOnly reportDate = ReportCalendar.LocalDate(asOf);
        var accounts = facts.Accounts.ToDictionary(a => a.Id);
        RequestFact[] open = [.. facts.Requests.Where(r => r.Status == ServiceAccountRequestStatus.Open && accounts.ContainsKey(r.AccountId))];
        RequestFact[] dated = [.. open.Where(IsDatedPlan)];
        ActionFact[] actions = [.. facts.Actions.Where(a => accounts.ContainsKey(a.AccountId))];
        ActionFact[] performed = [.. actions.Where(a => ServiceAccountRules.IsPerformedReport(a.Facts))];
        CommunicationFact[] valid = [.. facts.Communications.Where(c => c.Kind != CommunicationKind.Draft)];

        ReportSummary summary = new(
            accounts.Count,
            accounts.Values.Count(a => a.OwnerTeamId is not null),
            accounts.Values.Count(a => a.ConfirmedPersonId is not null),
            accounts.Values.Where(a => a.ConfirmedPersonId is not null).Select(a => a.ConfirmedPersonId).Distinct().Count(),
            open.Length,
            dated.Length,
            dated.Select(r => r.AccountId).Distinct().Count(),
            open.Count(r => !IsDatedPlan(r)),
            open.Count(r => IsOverdue(r, reportDate)),
            performed.Length,
            ClosureAccounts(actions).Count,
            valid.Count(c => c.Precision != TimePrecision.Unknown),
            valid.Count(c => c.Precision == TimePrecision.Unknown),
            facts.Communications.Count(c => c.Kind == CommunicationKind.Draft),
            facts.Findings.Count(f => accounts.ContainsKey(f.AccountId) && f.Status is FindingStatus.Open or FindingStatus.InReview));

        ServiceAccountReport report = new(DefinitionVersion, monday, endExclusive, asOf, scopeLabel, summary,
            Weekly(facts, accounts, performed, actions, valid, monday, endExclusive, asOf),
            Workload(facts, accounts, open, reportDate), Plans(facts, accounts, dated, reportDate),
            Handovers(facts, accounts), Legacy(accounts.Values, facts.Requests),
            [
                "Planlar gerçekleşen işlem sayılmaz; tarihsiz işlem döneme dağıtılmaz.",
                "Doğrulanmış kapanış, işlem bildiriminden ayrı sayılır.",
                "Bir mail kaç hesaba bağlı olursa olsun bir kez sayılır.",
                "Geciken iş: açık talebin plan bitişi rapor tarihinden önce (takvim günü).",
                "Bulgular ve başarısız taramalar tamamlanan iş sayılmaz."
            ], period);
        report = report with { GmsaNames = facts.RequestedGmsaNames ? GmsaNames(facts, accounts) : null };
        if (facts.Insights is not { } insight)
        {
            return report;
        }

        Dictionary<Guid, AccountRuleEvaluation> rules = ServiceAccountInsights.Evaluate(facts, insight);
        Dictionary<Guid, string> funnel = ServiceAccountInsights.FunnelStages(facts, rules);
        Dictionary<Guid, string[]> risk = ServiceAccountInsights.RiskCategories(facts, insight);
        return report with
        {
            Rules = ServiceAccountInsights.Rules(facts, rules),
            Funnel = ServiceAccountInsights.Funnel(funnel),
            Trend = ServiceAccountInsights.Trend(facts, endExclusive, asOf),
            Risk = ServiceAccountInsights.Risk(facts, insight, risk),
            Directorate = ServiceAccountInsights.Directorate(facts, insight, rules, funnel, risk, reportDate)
        };
    }

    /// <summary>
    /// Requested gMSA names on open requests and on gMSA transitions in scope, ordered by account. The length is counted by the
    /// shared rule (<see cref="ServiceAccountGmsaName"/>), so a name Active Directory would shorten stands out in the report.
    /// </summary>
    public static IReadOnlyList<GmsaNameLine> GmsaNames(ReportFacts facts, IReadOnlyDictionary<Guid, AccountFact> accounts)
    {
        IEnumerable<GmsaNameLine> requests = facts.Requests
            .Where(r => r.Status == ServiceAccountRequestStatus.Open && r.RequestedGmsaName is not null && accounts.ContainsKey(r.AccountId))
            .Select(r => new GmsaNameLine(accounts[r.AccountId].Label, ServiceAccountLabels.Action(r.ActionType) + " talebi", r.RequestedGmsaName!,
                ServiceAccountGmsaName.Length(r.RequestedGmsaName), "Açık talep"));
        IEnumerable<GmsaNameLine> transitions = facts.Transitions
            .Where(t => t.RequestedGmsaName is not null && accounts.ContainsKey(t.AccountId))
            .Select(t => new GmsaNameLine(accounts[t.AccountId].Label, "gMSA geçiş izlemesi", t.RequestedGmsaName!, ServiceAccountGmsaName.Length(t.RequestedGmsaName),
                t.Completed ? "Geçiş tamamlandı" : "Uygunluk: " + ServiceAccountLabels.Suitability(t.Suitability)));
        return [.. requests.Concat(transitions).OrderBy(l => l.Account, StringComparer.Ordinal).ThenBy(l => l.Source, StringComparer.Ordinal)
            .ThenBy(l => l.RequestedName, StringComparer.Ordinal).Take(_maxDetailLines)];
    }

    /// <summary>True when an open request has a valid plan range and a determined action.</summary>
    public static bool IsDatedPlan(RequestFact request) =>
        request.PlanStart is not null && request.PlanEnd is not null && request.ActionType != ServiceAccountActionType.Evaluate;

    /// <summary>Calendar-day overdue rule.</summary>
    public static bool IsOverdue(RequestFact request, DateOnly reportDate) =>
        request.Status == ServiceAccountRequestStatus.Open && request.PlanEnd is { } end && end < reportDate;

    private static WeeklyMovement Weekly(ReportFacts facts, Dictionary<Guid, AccountFact> accounts, ActionFact[] performed,
        ActionFact[] actions, CommunicationFact[] valid, DateOnly monday, DateOnly endExclusive, DateTimeOffset asOf)
    {
        PlacementCounts actionCounts = Count(performed.Select(a => ReportCalendar.Place(a.Precision, a.Facts.ActualOn, a.ActualAt, monday, endExclusive, asOf)));
        // A verified closure is one account; the verification date places it.
        PlacementCounts closureCounts = Count(ClosureAccounts(actions).Values.Select(verifiedOn =>
            ReportCalendar.Place(TimePrecision.DateOnly, verifiedOn, null, monday, endExclusive, asOf)));
        WeekPlacement[] mailPlacement = [.. valid.Select(c => ReportCalendar.Place(c.Precision, c.OccurredOn, c.OccurredAt, monday, endExclusive, asOf))];
        CommunicationFact[] inPeriodMails = [.. valid.Where((_, i) => mailPlacement[i] == WeekPlacement.InPeriod)];
        ReportActionLine[] lines = [.. performed
            .Where(a => ReportCalendar.Place(a.Precision, a.Facts.ActualOn, a.ActualAt, monday, endExclusive, asOf) == WeekPlacement.InPeriod)
            .OrderBy(a => a.Facts.ActualOn).ThenBy(a => accounts[a.AccountId].Label, StringComparer.Ordinal).ThenBy(a => a.Id)
            .Take(_maxDetailLines)
            .Select(a => new ReportActionLine(accounts[a.AccountId].Label, ServiceAccountLabels.Action(a.Facts.ActionType),
                ServiceAccountLabels.Result(a.Facts.Result), a.Facts.ActualOn, Team(facts, a.PerformerTeamId)))];
        return new WeeklyMovement(actionCounts, closureCounts, Count(mailPlacement),
            inPeriodMails.Count(c => c.Direction == CommunicationDirection.Outgoing),
            inPeriodMails.Count(c => c.Direction == CommunicationDirection.Incoming), lines);
    }

    /// <summary>Distinct accounts with a verified closure, keyed to the earliest qualifying verification date.</summary>
    private static Dictionary<Guid, DateOnly> ClosureAccounts(IEnumerable<ActionFact> actions) => actions
        .Where(a => ServiceAccountRules.IsVerifiedClosure(a.Facts))
        .GroupBy(a => a.AccountId)
        .ToDictionary(g => g.Key, g => g.Min(a => a.Facts.VerifiedOn!.Value));

    private static PlacementCounts Count(IEnumerable<WeekPlacement> placements)
    {
        WeekPlacement[] all = [.. placements];
        return new PlacementCounts(
            all.Count(p => p == WeekPlacement.InPeriod),
            all.Count(p => p == WeekPlacement.Earlier),
            all.Count(p => p == WeekPlacement.LaterBeforeCutoff),
            all.Count(p => p == WeekPlacement.AfterCutoff),
            all.Count(p => p == WeekPlacement.UnknownDate),
            all.Length);
    }

    private static TeamWorkloadRow[] Workload(ReportFacts facts, Dictionary<Guid, AccountFact> accounts, RequestFact[] open, DateOnly reportDate)
    {
        IEnumerable<Guid?> teams = accounts.Values.Select(a => a.OwnerTeamId).Concat(open.Select(r => r.TargetTeamId)).Distinct();
        return [.. teams
            .Select(team => new TeamWorkloadRow(
                Team(facts, team) ?? "Ekip belirlenmedi",
                accounts.Values.Count(a => a.OwnerTeamId == team),
                open.Count(r => r.TargetTeamId == team),
                open.Count(r => r.TargetTeamId == team && IsOverdue(r, reportDate))))
            .OrderByDescending(r => r.OpenRequestsAsTarget + r.OwnedAccounts)
            .ThenBy(r => r.Team, StringComparer.Ordinal)];
    }

    private static PlanLine[] Plans(ReportFacts facts, Dictionary<Guid, AccountFact> accounts, RequestFact[] dated, DateOnly reportDate) =>
        [.. dated
            .OrderBy(r => r.PlanEnd).ThenBy(r => accounts[r.AccountId].Label, StringComparer.Ordinal).ThenBy(r => r.Id)
            .Take(_maxDetailLines)
            .Select(r => new PlanLine(accounts[r.AccountId].Label, ServiceAccountLabels.Action(r.ActionType), r.PlanStart!.Value,
                r.PlanEnd!.Value, Team(facts, r.TargetTeamId), IsOverdue(r, reportDate)))];

    private static HandoverSummary Handovers(ReportFacts facts, Dictionary<Guid, AccountFact> accounts)
    {
        HandoverFact[] handovers = [.. facts.Handovers.Where(h => accounts.ContainsKey(h.AccountId))];
        TransitionFact[] gmsa = [.. facts.Transitions.Where(t => accounts.ContainsKey(t.AccountId))];
        TransitionFact[] pending = [.. gmsa.Where(t => !t.Completed)];
        return new HandoverSummary(
            handovers.Select(h => h.AccountId).Distinct().Count(),
            handovers.Where(h => h.Status == HandoverStatus.Accepted && h.HasAcceptanceEvidence).Select(h => h.AccountId).Distinct().Count(),
            handovers.Where(h => h.Status == HandoverStatus.Rejected).Select(h => h.AccountId).Distinct().Count(),
            gmsa.Length,
            gmsa.Count(t => t.Completed),
            pending.Length,
            [.. Enum.GetValues<GmsaSuitability>().Select(s => new NamedCount(ServiceAccountLabels.Suitability(s), pending.Count(t => t.Suitability == s)))]);
    }

    private static LegacyOwnershipProjection Legacy(IEnumerable<AccountFact> accounts, IEnumerable<RequestFact> requests)
    {
        AccountFact[] all = [.. accounts];
        // The legacy view named a responsible person (proposal or confirmation) and fell back to a request follow-up person.
        var named = all.Where(a => (a.ConfirmedPersonId ?? a.NamedPersonId) is not null)
            .ToDictionary(a => a.Id, a => (a.ConfirmedPersonId ?? a.NamedPersonId)!.Value);
        var fallback = requests
            .Where(r => r.FollowupPersonId is not null && !named.ContainsKey(r.AccountId))
            .GroupBy(r => r.AccountId)
            .Where(g => all.Any(a => a.Id == g.Key))
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.OrderKey, StringComparer.Ordinal).Last().FollowupPersonId!.Value);
        Guid[] people = [.. named.Values.Concat(fallback.Values).Distinct()];
        return new LegacyOwnershipProjection(named.Count, fallback.Count, named.Count + fallback.Count, people.Length,
            "Eski birleşik görünüm: girdide adı geçen sorumlu (öneri veya teyit) ve sahipsiz hesaplarda talep takipçisi. Takipçi sahip değildir; bu görünüm teyitli sahiplik sayısı değildir.");
    }

    private static string? Team(ReportFacts facts, Guid? id) => id is { } value && facts.TeamNames.TryGetValue(value, out string? name) ? name : null;
}
