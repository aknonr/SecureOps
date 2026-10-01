namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Latest coordination-list observation of an account (append-only source fact, not a human action).</summary>
/// <param name="AccountId">Account.</param>
/// <param name="SourceReportDate">Source report date; ages are measured from it, never from today.</param>
/// <param name="Present">Present in that list.</param>
/// <param name="PasswordLastSet">Observed last password change.</param>
/// <param name="LastLogonAdOrLdap">Observed AD or LDAP last logon.</param>
/// <param name="LastLogonAd">Observed AD last logon.</param>
public sealed record ObservationFact(Guid AccountId, DateOnly? SourceReportDate, bool Present, DateTime? PasswordLastSet, DateTime? LastLogonAdOrLdap,
    DateTime? LastLogonAd);

/// <summary>Risk-list thresholds in calendar days.</summary>
/// <param name="LogonDays">No logon for at least this many days.</param>
/// <param name="PasswordDays">Password unchanged for at least this many days.</param>
public sealed record RiskThresholds(int LogonDays = RiskThresholds.DefaultLogonDays, int PasswordDays = RiskThresholds.DefaultPasswordDays)
{
    /// <summary>Default logon threshold.</summary>
    public const int DefaultLogonDays = 90;
    /// <summary>Default password-age threshold.</summary>
    public const int DefaultPasswordDays = 365;
}

/// <summary>Inputs added in metric version 2. All are scope-filtered by the caller like <see cref="ReportFacts"/>.</summary>
/// <param name="Usages">Active usages.</param>
/// <param name="SqlTeamAccounts">Accounts owned by (or handed over from) a team configured as an SQL team.</param>
/// <param name="ExecutorTeam">Configured gMSA executing team label.</param>
/// <param name="Observations">Latest coordination-list observation per account.</param>
/// <param name="ClosedAccounts">Accounts whose closure is verified.</param>
/// <param name="TeamOrganizations">Team → organization label.</param>
/// <param name="Thresholds">Risk thresholds.</param>
public sealed record InsightFacts(
    IReadOnlyList<UsageFact> Usages,
    IReadOnlySet<Guid> SqlTeamAccounts,
    string? ExecutorTeam,
    IReadOnlyList<ObservationFact> Observations,
    IReadOnlySet<Guid> ClosedAccounts,
    IReadOnlyDictionary<Guid, string> TeamOrganizations,
    RiskThresholds Thresholds);

/// <summary>Knowledge-base rule summary.</summary>
public sealed record RuleSummary(string RuleSetVersion, int Assessed, int AgainstRule, IReadOnlyList<NamedCount> ByPath, IReadOnlyList<NamedCount> ByConformance,
    IReadOnlyList<RuleLine> Lines);

/// <summary>One account against its rule (listed when unplanned or missing information).</summary>
public sealed record RuleLine(string Account, string? OwnerTeam, string Path, string Conformance, string RuleCodes, string Reason);

/// <summary>One gMSA funnel stage.</summary>
public sealed record FunnelStage(string Code, string Label, int Count, string Expected);

/// <summary>gMSA funnel: each account counted once at the highest stage it reached.</summary>
public sealed record GmsaFunnel(int Population, IReadOnlyList<FunnelStage> Stages, int Ineligible, string Note);

/// <summary>One trend week.</summary>
public sealed record TrendPoint(DateOnly WeekStart, int OpenAtWeekEnd, int OverdueAtWeekEnd, int VerifiedClosures, int PerformedActions);

/// <summary>Weekly trend recomputed from current records.</summary>
public sealed record ReportTrend(IReadOnlyList<TrendPoint> Points, int UnknownCloseTime, string Note);

/// <summary>One risk candidate.</summary>
public sealed record RiskLine(string Account, string? OwnerTeam, string Categories, DateOnly? SourceDate, DateOnly? LastLogon, DateOnly? PasswordLastSet, int OpenRequests);

/// <summary>Risk candidates from source observations; candidates are findings to review, never closures.</summary>
public sealed record RiskSummary(int LogonDays, int PasswordDays, int Candidates, IReadOnlyList<NamedCount> ByCategory, IReadOnlyList<RiskLine> Lines, string Note);

/// <summary>Directorate view row: one team, workload and open topics; no person-level figures.</summary>
public sealed record DirectorateRow(string Organization, string Team, int OwnedAccounts, int OpenRequestsAsTarget, int OverdueAsTarget, int AwaitingDateAsTarget,
    int AgainstRule, int GmsaPending, int RiskCandidates);

/// <summary>Metric version 2 sections: rules, gMSA funnel, trend, risk list and directorate view.</summary>
public static class ServiceAccountInsights
{
    /// <summary>Number of weeks in the trend.</summary>
    public const int TrendWeeks = 12;

    private const int _maxLines = 500;
    private const string _noTeam = "Ekip belirlenmedi";

    private static readonly (string Code, string Label, string Expected)[] _stages =
    [
        ("Unknown", "Bilinmiyor", "Uygunluk incelemesi başlatılmalı: kimlik doğrulama, servis türü, bağımlılıklar, DBA/uygulama onayı."),
        ("Review", "İnceleniyor", "Uygun / uygun değil kararı gerekçesiyle girilmeli."),
        ("Eligible", "Uygun", "Yürütücü ekibe devir ve kabul kanıtı bekleniyor."),
        ("HandedOver", "Devredildi", "gMSA dönüşümü yapılıp işlem olarak bildirilmeli (OR kaydıyla)."),
        ("Converted", "Dönüştü", "Doğrulayan, tarih ve kanıtla doğrulama bekleniyor."),
        ("Verified", "Doğrulandı", "Tamamlandı.")
    ];

    /// <summary>Evaluates rules for every account in scope.</summary>
    public static Dictionary<Guid, AccountRuleEvaluation> Evaluate(ReportFacts facts, InsightFacts insight)
    {
        ILookup<Guid, UsageFact> usages = insight.Usages.ToLookup(u => u.AccountId);
        Dictionary<Guid, AccountWorkState> work = Work(facts);
        return facts.Accounts.ToDictionary(a => a.Id, a => ServiceAccountUsageRules.Evaluate(a.Id, usages[a.Id], insight.SqlTeamAccounts.Contains(a.Id),
            insight.ExecutorTeam, work.GetValueOrDefault(a.Id) ?? AccountWorkState.None));
    }

    /// <summary>Work state per account from requests, handovers, transitions and actions.</summary>
    public static Dictionary<Guid, AccountWorkState> Work(ReportFacts facts)
    {
        ILookup<Guid, RequestFact> requests = facts.Requests.ToLookup(r => r.AccountId);
        ILookup<Guid, HandoverFact> handovers = facts.Handovers.ToLookup(h => h.AccountId);
        ILookup<Guid, ActionFact> actions = facts.Actions.ToLookup(a => a.AccountId);
        var transitions = facts.Transitions.GroupBy(t => t.AccountId).ToDictionary(g => g.Key, g => g.First());
        return facts.Accounts.ToDictionary(a => a.Id, a => new AccountWorkState(
            requests[a.Id].Where(r => r.Status == ServiceAccountRequestStatus.Open).Select(r => r.ActionType).ToHashSet(),
            transitions.ContainsKey(a.Id),
            transitions.GetValueOrDefault(a.Id)?.Completed == true
                || actions[a.Id].Any(x => x.Facts.ActionType == ServiceAccountActionType.GmsaConversion && ServiceAccountRules.IsPerformedReport(x.Facts)),
            actions[a.Id].Any(x => x.Facts.ActionType == ServiceAccountActionType.GmsaConversion && ServiceAccountRules.IsVerifiedClosure(x.Facts)),
            actions[a.Id].Any(x => x.Facts.ActionType == ServiceAccountActionType.Deletion && ServiceAccountRules.IsVerifiedClosure(x.Facts)),
            handovers[a.Id].Any(h => h.Status == HandoverStatus.Proposed),
            handovers[a.Id].Any(h => h.Status == HandoverStatus.Accepted && h.HasAcceptanceEvidence)));
    }

    /// <summary>Rule summary with the against-rule list.</summary>
    public static RuleSummary Rules(ReportFacts facts, IReadOnlyDictionary<Guid, AccountRuleEvaluation> rules)
    {
        AccountRuleEvaluation[] all = [.. rules.Values];
        AccountRuleEvaluation[] assessed = [.. all.Where(r => r.Conformance != RuleConformance.NotAssessed)];
        var accounts = facts.Accounts.ToDictionary(a => a.Id);
        RuleLine[] lines = [.. assessed
            .Where(r => r.Conformance is RuleConformance.Unplanned or RuleConformance.IncompleteInformation)
            .Select(r => (Rule: r, Account: accounts[r.AccountId]))
            .OrderByDescending(x => x.Rule.Conformance).ThenBy(x => Team(facts, x.Account.OwnerTeamId) ?? "￿", StringComparer.Ordinal)
            .ThenBy(x => x.Account.Label, StringComparer.Ordinal)
            .Take(_maxLines)
            .Select(x => new RuleLine(x.Account.Label, Team(facts, x.Account.OwnerTeamId), ServiceAccountUsageRules.PathLabel(x.Rule.Path!.Value),
                ServiceAccountUsageRules.ConformanceLabel(x.Rule.Conformance),
                string.Join(", ", x.Rule.Items.Where(i => !i.Excepted).Select(i => i.RuleCode).Distinct()),
                x.Rule.Items.Where(i => !i.Excepted && i.Path == x.Rule.Path).Select(i => i.Reason).FirstOrDefault() ?? string.Empty))];
        return new RuleSummary(ServiceAccountUsageRules.RuleSetVersion, assessed.Length, assessed.Count(r => r.Conformance == RuleConformance.Unplanned),
            [.. Enum.GetValues<RecommendedPath>().Select(p => new NamedCount(ServiceAccountUsageRules.PathLabel(p), assessed.Count(r => r.Path == p)))],
            [.. Enum.GetValues<RuleConformance>().Select(c => new NamedCount(ServiceAccountUsageRules.ConformanceLabel(c), all.Count(r => r.Conformance == c)))],
            lines);
    }

    /// <summary>gMSA stage per account in the funnel population; null when the account is not in it.</summary>
    public static Dictionary<Guid, string> FunnelStages(ReportFacts facts, IReadOnlyDictionary<Guid, AccountRuleEvaluation> rules)
    {
        Dictionary<Guid, AccountWorkState> work = Work(facts);
        var transitions = facts.Transitions.GroupBy(t => t.AccountId).ToDictionary(g => g.Key, g => g.First());
        HashSet<Guid> requested = [.. facts.Requests.Where(r => r.ActionType is ServiceAccountActionType.GmsaHandover or ServiceAccountActionType.GmsaConversion)
            .Select(r => r.AccountId)];
        Dictionary<Guid, string> stages = [];
        foreach (AccountFact account in facts.Accounts)
        {
            AccountWorkState state = work[account.Id];
            bool member = transitions.ContainsKey(account.Id) || requested.Contains(account.Id) || state.GmsaConverted
                || rules.GetValueOrDefault(account.Id)?.Items.Any(i => !i.Excepted && i.Path == RecommendedPath.GmsaEvaluation) == true;
            if (!member)
            {
                continue;
            }

            GmsaSuitability suitability = transitions.GetValueOrDefault(account.Id)?.Suitability ?? GmsaSuitability.Unknown;
            stages[account.Id] = state.GmsaVerifiedClosure ? "Verified"
                : state.GmsaConverted ? "Converted"
                : suitability == GmsaSuitability.Ineligible ? "Ineligible"
                : state.HandoverAccepted ? "HandedOver"
                : suitability.ToString();
        }

        return stages;
    }

    /// <summary>Turkish label of a funnel stage code.</summary>
    public static string StageLabel(string code) => code == "Ineligible" ? "Uygun değil" : _stages.FirstOrDefault(s => s.Code == code).Label ?? code;

    /// <summary>What is expected at a funnel stage.</summary>
    public static string StageExpected(string code) => code == "Ineligible" ? "Uygun değil kararı verildi; bilgi bankasındaki diğer yollar değerlendirilir."
        : _stages.FirstOrDefault(s => s.Code == code).Expected ?? string.Empty;

    /// <summary>gMSA funnel.</summary>
    public static GmsaFunnel Funnel(IReadOnlyDictionary<Guid, string> stages) => new(stages.Count,
        [.. _stages.Select(s => new FunnelStage(s.Code, s.Label, stages.Values.Count(v => v == s.Code), s.Expected))],
        stages.Values.Count(v => v == "Ineligible"),
        "Her hesap ulaştığı en ileri aşamada bir kez sayılır. Kural önerisi uygunluk değildir; uygunluk gerekçeli karar ister. Uygun değil kararı huniden ayrı sayılır.");

    /// <summary>Weekly trend for the <see cref="TrendWeeks"/> weeks ending with the week of the report end.</summary>
    public static ReportTrend Trend(ReportFacts facts, DateOnly endExclusive, DateTimeOffset asOf)
    {
        var accounts = facts.Accounts.Select(a => a.Id).ToHashSet();
        RequestFact[] requests = [.. facts.Requests.Where(r => accounts.Contains(r.AccountId))];
        RequestFact[] known = [.. requests.Where(r => r.CreatedAt is not null && (r.Status == ServiceAccountRequestStatus.Open || r.ClosedAt is not null))];
        ActionFact[] actions = [.. facts.Actions.Where(a => accounts.Contains(a.AccountId))];
        DateOnly[] closures = [.. actions.Where(a => ServiceAccountRules.IsVerifiedClosure(a.Facts)).GroupBy(a => a.AccountId)
            .Select(g => g.Min(a => a.Facts.VerifiedOn!.Value))];
        DateOnly asOfDate = ReportCalendar.LocalDate(asOf);
        DateOnly last = ReportCalendar.WeekStart(endExclusive.AddDays(-1));
        TrendPoint[] points = [.. Enumerable.Range(0, TrendWeeks).Select(i =>
        {
            DateOnly week = last.AddDays(-7 * (TrendWeeks - 1 - i));
            DateTimeOffset end = ReportCalendar.StartOf(week.AddDays(7));
            DateTimeOffset cut = end < asOf ? end : asOf;
            DateOnly cutDate = ReportCalendar.LocalDate(cut);
            RequestFact[] open = [.. known.Where(r => r.CreatedAt <= cut && (r.Status == ServiceAccountRequestStatus.Open || r.ClosedAt > cut))];
            return new TrendPoint(week, open.Length, open.Count(r => r.PlanEnd is { } planEnd && planEnd < cutDate),
                closures.Count(d => d >= week && d < week.AddDays(7) && d <= asOfDate),
                actions.Count(a => ServiceAccountRules.IsPerformedReport(a.Facts)
                    && ReportCalendar.Place(a.Precision, a.Facts.ActualOn, a.ActualAt, week, week.AddDays(7), asOf) == WeekPlacement.InPeriod));
        })];
        return new ReportTrend(points, requests.Length - known.Length,
            "Trend güncel kayıtlardan yeniden hesaplanır: sonradan değişen plan tarihleri geçmiş haftaları da etkiler. O gün gönderilen rakamlar için kayıtlı nüshaları karşılaştırın. Kapanış zamanı bilinmeyen eski talepler trende katılmaz.");
    }

    /// <summary>Risk categories per account (closed accounts excluded).</summary>
    public static Dictionary<Guid, string[]> RiskCategories(ReportFacts facts, InsightFacts insight)
    {
        var accounts = facts.Accounts.Select(a => a.Id).ToHashSet();
        Dictionary<Guid, string[]> result = [];
        foreach (ObservationFact o in insight.Observations.Where(o => accounts.Contains(o.AccountId) && !insight.ClosedAccounts.Contains(o.AccountId)))
        {
            List<string> categories = [];
            if (!o.Present)
            {
                categories.Add("Son listede yok");
            }
            else if (o.SourceReportDate is { } source)
            {
                DateOnly? logon = LastLogon(o);
                if (logon is null)
                {
                    categories.Add("Oturum tarihi yok");
                }
                else if (source.DayNumber - logon.Value.DayNumber >= insight.Thresholds.LogonDays)
                {
                    categories.Add($"Son oturum ≥ {insight.Thresholds.LogonDays} gün");
                }

                if (o.PasswordLastSet is { } password && source.DayNumber - DateOnly.FromDateTime(password).DayNumber >= insight.Thresholds.PasswordDays)
                {
                    categories.Add($"Parola ≥ {insight.Thresholds.PasswordDays} gün");
                }
            }

            if (categories.Count > 0)
            {
                result[o.AccountId] = [.. categories];
            }
        }

        return result;
    }

    /// <summary>Risk summary.</summary>
    public static RiskSummary Risk(ReportFacts facts, InsightFacts insight, IReadOnlyDictionary<Guid, string[]> categories)
    {
        var accounts = facts.Accounts.ToDictionary(a => a.Id);
        var observations = insight.Observations.GroupBy(o => o.AccountId).ToDictionary(g => g.Key, g => g.First());
        var open = facts.Requests.Where(r => r.Status == ServiceAccountRequestStatus.Open).GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g => g.Count());
        string[] labels = ["Son oturum ≥ " + insight.Thresholds.LogonDays + " gün", "Oturum tarihi yok", "Parola ≥ " + insight.Thresholds.PasswordDays + " gün", "Son listede yok"];
        RiskLine[] lines = [.. categories
            .Select(c => (Account: accounts[c.Key], Categories: c.Value, Observation: observations[c.Key]))
            .OrderByDescending(x => x.Categories.Length).ThenBy(x => x.Account.Label, StringComparer.Ordinal)
            .Take(_maxLines)
            .Select(x => new RiskLine(x.Account.Label, Team(facts, x.Account.OwnerTeamId), string.Join(", ", x.Categories), x.Observation.SourceReportDate,
                LastLogon(x.Observation), x.Observation.PasswordLastSet is { } p ? DateOnly.FromDateTime(p) : null, open.GetValueOrDefault(x.Account.Id)))];
        return new RiskSummary(insight.Thresholds.LogonDays, insight.Thresholds.PasswordDays, categories.Count,
            [.. labels.Select(l => new NamedCount(l, categories.Values.Count(v => v.Contains(l))))], lines,
            "Yaşlar kaynak listenin tarihine göre hesaplanır. AD son oturum bilgisi gecikmeli çoğaltılır; oturum görünmemesi hesabın kullanılmadığını kanıtlamaz. Adaylar inceleme içindir, kapanış sayılmaz.");
    }

    /// <summary>Directorate view: one row per team in scope plus the accounts without an owner team.</summary>
    public static DirectorateRow[] Directorate(ReportFacts facts, InsightFacts insight, IReadOnlyDictionary<Guid, AccountRuleEvaluation> rules,
        IReadOnlyDictionary<Guid, string> funnel, IReadOnlyDictionary<Guid, string[]> risk, DateOnly reportDate)
    {
        var accounts = facts.Accounts.ToDictionary(a => a.Id);
        RequestFact[] open = [.. facts.Requests.Where(r => r.Status == ServiceAccountRequestStatus.Open && accounts.ContainsKey(r.AccountId))];
        IEnumerable<Guid?> teams = accounts.Values.Select(a => a.OwnerTeamId).Concat(open.Select(r => r.TargetTeamId)).Distinct();
        return [.. teams
            .Select(team =>
            {
                AccountFact[] owned = [.. accounts.Values.Where(a => a.OwnerTeamId == team)];
                RequestFact[] target = [.. open.Where(r => r.TargetTeamId == team)];
                return new DirectorateRow(
                    team is { } id && insight.TeamOrganizations.TryGetValue(id, out string? org) ? org : "—",
                    Team(facts, team) ?? _noTeam,
                    owned.Length,
                    target.Length,
                    target.Count(r => ServiceAccountMetrics.IsOverdue(r, reportDate)),
                    target.Count(r => !ServiceAccountMetrics.IsDatedPlan(r)),
                    owned.Count(a => rules.GetValueOrDefault(a.Id)?.Conformance == RuleConformance.Unplanned),
                    owned.Count(a => funnel.TryGetValue(a.Id, out string? stage) && stage is not ("Verified" or "Ineligible")),
                    owned.Count(a => risk.ContainsKey(a.Id)));
            })
            .OrderBy(r => r.Team == _noTeam).ThenBy(r => r.Organization, StringComparer.Ordinal).ThenBy(r => r.Team, StringComparer.Ordinal)];
    }

    private static DateOnly? LastLogon(ObservationFact o)
    {
        DateTime? latest = o.LastLogonAdOrLdap is { } a && o.LastLogonAd is { } b ? (a > b ? a : b) : o.LastLogonAdOrLdap ?? o.LastLogonAd;
        return latest is { } value ? DateOnly.FromDateTime(value) : null;
    }

    private static string? Team(ReportFacts facts, Guid? id) => id is { } value && facts.TeamNames.TryGetValue(value, out string? name) ? name : null;
}
