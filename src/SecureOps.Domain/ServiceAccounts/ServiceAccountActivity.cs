using System.Globalization;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>A confirmed ownership decision made in the application (history row "OwnershipConfirmed").</summary>
/// <param name="AccountId">Account.</param>
/// <param name="NewTeamId">Team that became the confirmed owner; null when only a person was confirmed.</param>
/// <param name="PreviousTeamId">Team of the previous confirmed ownership decision, when there was one.</param>
/// <param name="At">Decision instant.</param>
/// <param name="HasEvidence">The decision carries an evidence note or a decision reason.</param>
public sealed record ActivityOwnershipFact(Guid AccountId, Guid? NewTeamId, Guid? PreviousTeamId, DateTimeOffset At, bool HasEvidence);

/// <summary>An accepted handover (devir).</summary>
/// <param name="AccountId">Account.</param>
/// <param name="SourceTeamId">Handing-over team.</param>
/// <param name="TargetTeamId">Receiving team.</param>
/// <param name="DecidedOn">Decision business date.</param>
/// <param name="HasAcceptanceEvidence">Decision date, decider and note are all recorded.</param>
public sealed record ActivityHandoverFact(Guid AccountId, Guid? SourceTeamId, Guid TargetTeamId, DateOnly DecidedOn, bool HasAcceptanceEvidence);

/// <summary>A completed change plan that includes the account.</summary>
/// <param name="AccountId">Account.</param>
/// <param name="CompletedAt">Completion instant.</param>
public sealed record ActivityPlanFact(Guid AccountId, DateTimeOffset CompletedAt);

/// <summary>Inputs for the "what the team did in the period" page that the action table alone does not carry.</summary>
/// <param name="Ownership">Ownership decisions.</param>
/// <param name="Handovers">Accepted handovers.</param>
/// <param name="Plans">Completed plans.</param>
/// <param name="PlansAvailable">The change-plan tables exist and are readable; false means plans were not counted.</param>
public sealed record ActivityFacts(IReadOnlyList<ActivityOwnershipFact> Ownership, IReadOnlyList<ActivityHandoverFact> Handovers,
    IReadOnlyList<ActivityPlanFact> Plans, bool PlansAvailable);

/// <summary>Activity group keys.</summary>
public static class ActivityGroupKeys
{
    /// <summary>Deleted accounts.</summary>
    public const string Deleted = "Deleted";
    /// <summary>Handed-over accounts (old team to new team).</summary>
    public const string Handed = "Handed";
    /// <summary>Accounts moved to gMSA.</summary>
    public const string Gmsa = "Gmsa";
    /// <summary>Otherwise changed accounts.</summary>
    public const string Changed = "Changed";
    /// <summary>Reported but not verified actions.</summary>
    public const string Pending = "Pending";
}

/// <summary>One line of the activity page. Teams are team names only; there is no person column.</summary>
/// <param name="Account">Account label.</param>
/// <param name="Operation">What was done.</param>
/// <param name="Date">Business date of the operation (null when unknown).</param>
/// <param name="Teams">Old → new team for a handover, otherwise the performing team.</param>
/// <param name="Status">Verification state: verified, reported but not verified, or a decision record.</param>
/// <param name="Evidence">Whether evidence is recorded.</param>
public sealed record ActivityLine(string Account, string Operation, DateOnly? Date, string Teams, string Status, string Evidence);

/// <summary>One group of the activity page.</summary>
/// <param name="Key">See <see cref="ActivityGroupKeys"/>.</param>
/// <param name="Title">Heading.</param>
/// <param name="Accounts">Distinct accounts in the group.</param>
/// <param name="Lines">Lines before the display cap.</param>
/// <param name="Items">Displayed lines.</param>
/// <param name="NotShown">Lines beyond the display cap.</param>
public sealed record ActivityGroup(string Key, string Title, int Accounts, int Lines, IReadOnlyList<ActivityLine> Items, int NotShown);

/// <summary>One executive-summary card: what it counts, in one sentence.</summary>
/// <param name="Label">Name.</param>
/// <param name="Value">Figure.</param>
/// <param name="Sentence">One sentence saying what is counted.</param>
public sealed record ReportCard(string Label, long Value, string Sentence);

/// <summary>
/// The first page of the report: what the team did in the period (deleted, handed over, moved to gMSA, changed) and what is
/// reported but not yet verified. Team and account level only; nothing is counted or ranked per person.
/// </summary>
/// <param name="Groups">Deleted, handed over, gMSA, changed and pending groups in that order.</param>
/// <param name="AccountsActed">Distinct accounts with an operation in the period (the pending group is not included).</param>
/// <param name="Cards">The simplified executive summary (at most five cards).</param>
/// <param name="Notes">Scope and definition notes.</param>
public sealed record ServiceAccountActivity(IReadOnlyList<ActivityGroup> Groups, int AccountsActed, IReadOnlyList<ReportCard> Cards, IReadOnlyList<string> Notes);

/// <summary>Classification of the period's operations. Pure; inputs are already scope-filtered.</summary>
public static class ServiceAccountActivityRules
{
    /// <summary>Lines shown per group; the count still includes the rest.</summary>
    public const int MaxLinesPerGroup = 200;

    private const string _verified = "Doğrulandı";
    private const string _notVerified = "Bildirildi · doğrulanmadı";
    private const string _decision = "Karar kaydı";

    /// <summary>Builds the activity page for the business-date period [<paramref name="start"/>, <paramref name="endExclusive"/>).</summary>
    public static ServiceAccountActivity Build(ReportFacts facts, ActivityFacts inputs, ReportSummary summary, HandoverSummary handover, DateOnly start,
        DateOnly endExclusive, DateTimeOffset asOf)
    {
        var accounts = facts.Accounts.ToDictionary(a => a.Id);
        DateOnly today = ReportCalendar.LocalDate(asOf);
        bool InPeriod(DateOnly? date) => date is { } d && d >= start && d < endExclusive && d <= today;
        string Team(Guid? id) => id is { } value && facts.TeamNames.TryGetValue(value, out string? name) ? name : "Belirtilmemiş";
        List<(string Group, Guid AccountId, ActivityLine Line)> lines = [];
        void Add(string group, Guid accountId, string operation, DateOnly? date, string teams, string status, string evidence) =>
            lines.Add((group, accountId, new ActivityLine(accounts[accountId].Label, operation, date, teams, status, evidence)));

        ActionFact[] actions = [.. facts.Actions.Where(a => accounts.ContainsKey(a.AccountId) && ServiceAccountRules.IsPerformedReport(a.Facts))];
        foreach (ActionFact action in actions)
        {
            string? group = GroupOf(action.Facts.ActionType);
            if (group is null)
            {
                continue;
            }

            bool reportedInPeriod = ReportCalendar.Place(action.Precision, action.Facts.ActualOn, action.ActualAt, start, endExclusive, asOf) == WeekPlacement.InPeriod;
            bool verifiedInPeriod = IsVerified(action.Facts) && InPeriod(action.Facts.VerifiedOn);
            if (!reportedInPeriod && !verifiedInPeriod)
            {
                continue;
            }

            if (group == ActivityGroupKeys.Handed)
            {
                // A handover decision (below) is the record of a handover; the gMSA handover action only adds a line when no decision exists.
                continue;
            }

            Add(group, action.AccountId, ServiceAccountLabels.Action(action.Facts.ActionType), reportedInPeriod ? action.Facts.ActualOn : action.Facts.VerifiedOn,
                Performer(action, facts), StatusOf(action.Facts), EvidenceOf(action.Facts));
        }

        foreach (ActivityHandoverFact h in inputs.Handovers.Where(h => accounts.ContainsKey(h.AccountId) && InPeriod(h.DecidedOn)))
        {
            Add(ActivityGroupKeys.Handed, h.AccountId, "Devir kabul edildi", h.DecidedOn, $"{Team(h.SourceTeamId)} → {Team(h.TargetTeamId)}", _decision,
                h.HasAcceptanceEvidence ? "Var" : "Yok");
        }

        foreach (ActivityOwnershipFact o in inputs.Ownership.Where(o => accounts.ContainsKey(o.AccountId) && InPeriod(ReportCalendar.LocalDate(o.At))))
        {
            DateOnly date = ReportCalendar.LocalDate(o.At);
            string evidence = o.HasEvidence ? "Var" : "Yok";
            if (o.NewTeamId is { } to && o.PreviousTeamId is { } from && from != to)
            {
                Add(ActivityGroupKeys.Handed, o.AccountId, "Sahiplik devri", date, $"{Team(from)} → {Team(to)}", _decision, evidence);
            }
            else
            {
                Add(ActivityGroupKeys.Changed, o.AccountId, o.PreviousTeamId is null ? "Sahip ekip teyit edildi" : "Sahiplik yeniden teyit edildi", date,
                    o.NewTeamId is null ? "—" : Team(o.NewTeamId), _decision, evidence);
            }
        }

        // A gMSA handover action is a handover line only when the account has no handover or ownership-transfer line in the period.
        HashSet<Guid> handed = [.. lines.Where(l => l.Group == ActivityGroupKeys.Handed).Select(l => l.AccountId)];
        foreach (ActionFact action in actions.Where(a => a.Facts.ActionType == ServiceAccountActionType.GmsaHandover && !handed.Contains(a.AccountId)))
        {
            bool reportedInPeriod = ReportCalendar.Place(action.Precision, action.Facts.ActualOn, action.ActualAt, start, endExclusive, asOf) == WeekPlacement.InPeriod;
            bool verifiedInPeriod = IsVerified(action.Facts) && InPeriod(action.Facts.VerifiedOn);
            if (reportedInPeriod || verifiedInPeriod)
            {
                Add(ActivityGroupKeys.Handed, action.AccountId, ServiceAccountLabels.Action(action.Facts.ActionType), reportedInPeriod ? action.Facts.ActualOn : action.Facts.VerifiedOn,
                    Performer(action, facts), StatusOf(action.Facts), EvidenceOf(action.Facts));
            }
        }

        foreach ((Guid accountId, DateOnly verifiedOn) in actions.Where(a => ServiceAccountRules.IsVerifiedClosure(a.Facts)).GroupBy(a => a.AccountId)
            .Select(g => (g.Key, g.Min(a => a.Facts.VerifiedOn!.Value))).Where(c => InPeriod(c.Item2)))
        {
            Add(ActivityGroupKeys.Changed, accountId, "Hesap kapanışı doğrulandı", verifiedOn, "—", _verified, "Var");
        }

        foreach (ActivityPlanFact plan in inputs.Plans.Where(p => accounts.ContainsKey(p.AccountId) && InPeriod(ReportCalendar.LocalDate(p.CompletedAt))))
        {
            Add(ActivityGroupKeys.Changed, plan.AccountId, "gMSA geçiş planı tamamlandı", ReportCalendar.LocalDate(plan.CompletedAt), "—", "Plan kaydı", "—");
        }

        List<(string Group, Guid AccountId, ActivityLine Line)> pending = [.. actions
            .Where(a => GroupOf(a.Facts.ActionType) is not null && !IsVerified(a.Facts)
                && ReportCalendar.Place(a.Precision, a.Facts.ActualOn, a.ActualAt, start, endExclusive, asOf) != WeekPlacement.AfterCutoff)
            .Select(a => (ActivityGroupKeys.Pending, a.AccountId, new ActivityLine(accounts[a.AccountId].Label, ServiceAccountLabels.Action(a.Facts.ActionType),
                a.Facts.ActualOn, Performer(a, facts), PendingStatus(a.Facts.ActualOn, today), EvidenceOf(a.Facts))))];

        ActivityGroup[] groups =
        [
            Group(ActivityGroupKeys.Deleted, "Silinen hesaplar", lines),
            Group(ActivityGroupKeys.Handed, "Devredilen hesaplar (eski → yeni sorumlu ekip)", lines),
            Group(ActivityGroupKeys.Gmsa, "gMSA'ya geçen hesaplar", lines),
            Group(ActivityGroupKeys.Changed, "Değişen hesaplar", lines),
            Group(ActivityGroupKeys.Pending, "Bildirilen ama doğrulanmayan işlemler (tüm zamanlar)", pending, oldestFirst: true)
        ];
        int acted = lines.Select(l => l.AccountId).Distinct().Count();
        ReportCard[] cards =
        [
            new("Toplam hesap", summary.UniqueAccounts, "Kapsamınızdaki tekil servis hesabı sayısı."),
            new("Bu dönem işlem gören", acted, "Dönemde silinen, devredilen, gMSA'ya geçen veya değişen tekil hesap sayısı."),
            new("Doğrulanmayı bekleyen", groups[4].Lines, "Bildirilmiş ama henüz doğrulanmamış işlem sayısı (dönemden bağımsız)."),
            new("Sahip ekibi olmayan", summary.UniqueAccounts - summary.AccountsWithOwnerTeam, "Teyitli sahip ekibi bulunmayan hesap sayısı."),
            new("gMSA ilerlemesi", handover.GmsaCompleted, $"gMSA hedefli {handover.GmsaTargeted} hesaptan {handover.GmsaCompleted} tanesinin geçişi gerçekleşti, {handover.GmsaPending} tanesi bekliyor.")
        ];
        List<string> notes =
        [
            "Bu sayfa ekip ve hesap düzeyindedir; kişi bazlı sayım veya sıralama yoktur. Devir satırlarında yalnız ekip adı görünür.",
            "Bildirildi, doğrulandı demek değildir: doğrulama ayrı bir kayıttır (doğrulayan, tarih ve kanıt).",
            "Tarihi bilinmeyen işlem döneme girmez; yalnız doğrulanmayı bekleyenler listesinde görünür.",
            "Sahiplik devri yalnız uygulama içi karar kayıtlarından alınır; toplu içe aktarılan sahiplik bu sayfada sayılmaz.",
            "Plan tamamlandı, sunucuda değişiklik yapıldığının doğrulaması değildir; ürün sunucuda yazma yapmaz."
        ];
        if (!inputs.PlansAvailable)
        {
            notes.Add("Değişiklik planı tabloları kurulu değil ya da okunamıyor; plan tamamlanmaları sayılmadı.");
        }

        return new ServiceAccountActivity(groups, acted, cards, notes);
    }

    private static ActivityGroup Group(string key, string title, List<(string Group, Guid AccountId, ActivityLine Line)> source, bool oldestFirst = false)
    {
        var rows = source.Where(l => l.Group == key).ToList();
        ActivityLine[] ordered = [.. (oldestFirst
                ? rows.OrderBy(l => l.Line.Date is null ? 1 : 0).ThenBy(l => l.Line.Date)
                : rows.OrderBy(l => l.Line.Date))
            .ThenBy(l => l.Line.Account, StringComparer.Ordinal).ThenBy(l => l.Line.Operation, StringComparer.Ordinal).Select(l => l.Line)];
        return new ActivityGroup(key, title, rows.Select(l => l.AccountId).Distinct().Count(), ordered.Length, [.. ordered.Take(MaxLinesPerGroup)],
            Math.Max(0, ordered.Length - MaxLinesPerGroup));
    }

    private static string? GroupOf(ServiceAccountActionType type) => type switch
    {
        ServiceAccountActionType.Deletion => ActivityGroupKeys.Deleted,
        ServiceAccountActionType.GmsaConversion => ActivityGroupKeys.Gmsa,
        ServiceAccountActionType.GmsaHandover => ActivityGroupKeys.Handed,
        ServiceAccountActionType.PasswordChange or ServiceAccountActionType.OwnershipConfirmation => ActivityGroupKeys.Changed,
        _ => null
    };

    /// <summary>A verification needs the Verified result, a verification date and a recorded verifier.</summary>
    private static bool IsVerified(ActionFacts action) =>
        action.Result == ServiceAccountActionResult.Verified && action.VerifiedOn is not null && action.HasVerifier;

    private static string StatusOf(ActionFacts action) => IsVerified(action)
        ? _verified + " · " + action.VerifiedOn!.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        : _notVerified;

    private static string PendingStatus(DateOnly? reported, DateOnly today) => reported is { } date
        ? _notVerified + " · " + Math.Max(0, today.DayNumber - date.DayNumber).ToString(CultureInfo.InvariantCulture) + " gün"
        : _notVerified + " · tarih bilinmiyor";

    private static string EvidenceOf(ActionFacts action) =>
        (action.HasEvidence ? "Var" : "Yok") + (action.ActionType == ServiceAccountActionType.Deletion ? action.HasOrReference ? " · OR var" : " · OR yok" : string.Empty);

    private static string Performer(ActionFact action, ReportFacts facts) =>
        action.PerformerTeamId is { } id && facts.TeamNames.TryGetValue(id, out string? name) ? name : "—";
}
