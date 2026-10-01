using System.Text.Json;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountInsightsTests
{
    private static readonly Guid _teamA = Guid.NewGuid();
    private static readonly Guid _teamB = Guid.NewGuid();
    private static readonly DateOnly _monday = new(2026, 9, 14);
    private static readonly DateTimeOffset _asOf = new(2026, 9, 19, 23, 0, 0, TimeSpan.FromHours(3));

    private static AccountFact Account(string label, Guid? team = null) => new(Guid.NewGuid(), label, team, null);

    private static Dictionary<Guid, string> Teams() => new() { [_teamA] = "SYN TEAM A", [_teamB] = "SYN TEAM B" };

    private static InsightFacts Insight(IReadOnlyList<UsageFact>? usages = null, IEnumerable<Guid>? sql = null, IReadOnlyList<ObservationFact>? observations = null,
        IEnumerable<Guid>? closed = null) =>
        new(usages ?? [], (sql ?? []).ToHashSet(), "SYN EXEC", observations ?? [], (closed ?? []).ToHashSet(),
            new Dictionary<Guid, string> { [_teamA] = "SYN ORG", [_teamB] = "SYN ORG" }, new RiskThresholds());

    private static ActionFact Action(Guid account, ServiceAccountActionType type, ServiceAccountActionResult result, DateOnly? actual, DateOnly? verified = null) =>
        new(Guid.NewGuid(), account, new ActionFacts(type, result, result == ServiceAccountActionResult.Planned ? ServiceAccountRecordKind.Intermediate
            : ServiceAccountRecordKind.Closure, actual, verified, verified is not null, verified is not null, true, false),
            actual is null ? TimePrecision.Unknown : TimePrecision.DateOnly, null, null);

    private static ServiceAccountReport Compute(ReportFacts facts) => ServiceAccountMetrics.Compute(facts, _monday, _asOf, "SYN");

    [Fact]
    public void Funnel_CountsEachAccountOnceAtItsHighestStage_AndKeepsIneligibleSeparate()
    {
        AccountFact unknown = Account("SYN_U"), review = Account("SYN_R"), eligible = Account("SYN_E"), handed = Account("SYN_H"),
            converted = Account("SYN_C"), verified = Account("SYN_V"), ineligible = Account("SYN_I"), outside = Account("SYN_X");
        TransitionFact[] transitions =
        [
            new(review.Id, GmsaSuitability.Review, false), new(eligible.Id, GmsaSuitability.Eligible, false), new(handed.Id, GmsaSuitability.Eligible, false),
            new(converted.Id, GmsaSuitability.Eligible, true), new(verified.Id, GmsaSuitability.Eligible, true), new(ineligible.Id, GmsaSuitability.Ineligible, false)
        ];
        ActionFact[] actions =
        [
            Action(converted.Id, ServiceAccountActionType.GmsaConversion, ServiceAccountActionResult.Performed, new(2026, 9, 15)),
            Action(verified.Id, ServiceAccountActionType.GmsaConversion, ServiceAccountActionResult.Verified, new(2026, 9, 15), new(2026, 9, 16))
        ];
        HandoverFact[] handovers = [new(handed.Id, HandoverStatus.Accepted, true)];

        ServiceAccountReport report = Compute(new([unknown, review, eligible, handed, converted, verified, ineligible, outside], [], actions, [], handovers,
            transitions, [], Teams(), Insight(sql: [unknown.Id])));

        report.MetricDefinitionVersion.Should().Be("sa-metrics-v2");
        report.Funnel!.Population.Should().Be(7, "the SQL-team account enters through the rule; an unrelated account does not");
        report.Funnel.Stages.Select(s => s.Count).Should().Equal(1, 1, 1, 1, 1, 1);
        report.Funnel.Ineligible.Should().Be(1);
        report.Funnel.Stages.Should().OnlyContain(s => s.Expected.Length > 0);
    }

    [Fact]
    public void RuleSummary_ListsAgainstRuleAccounts_AndCountsEveryConformance()
    {
        AccountFact against = Account("SYN_AGAINST", _teamA), planned = Account("SYN_PLANNED", _teamA), none = Account("SYN_NONE", _teamB);
        UsageFact[] usages =
        [
            new(Guid.NewGuid(), against.Id, UsageKind.ScheduledTask, null, null, false),
            new(Guid.NewGuid(), planned.Id, UsageKind.Database, DatabaseEngine.Oracle, null, false)
        ];
        RequestFact deletion = new(Guid.NewGuid(), planned.Id, ServiceAccountActionType.Deletion, ServiceAccountRequestStatus.Open, _teamA, null, null, null, "1");

        ServiceAccountReport report = Compute(new([against, planned, none], [deletion], [], [], [], [], [], Teams(), Insight(usages)));

        report.Rules!.Assessed.Should().Be(2);
        report.Rules.AgainstRule.Should().Be(1);
        report.Rules.Lines.Should().ContainSingle().Which.Should().Match<RuleLine>(l =>
            l.Account == "SYN_AGAINST" && l.RuleCodes == "KB-GOREV" && l.OwnerTeam == "SYN TEAM A" && l.Reason.Length > 0);
        report.Rules.ByConformance.Sum(c => c.Count).Should().Be(3);
        report.Directorate!.Single(r => r.Team == "SYN TEAM A").AgainstRule.Should().Be(1);
    }

    [Fact]
    public void Trend_ReconstructsWeekEnds_AndExcludesClosesWithoutTime()
    {
        AccountFact account = Account("SYN_T", _teamA);
        DateTimeOffset created = new(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(3));
        RequestFact[] requests =
        [
            new(Guid.NewGuid(), account.Id, ServiceAccountActionType.Review, ServiceAccountRequestStatus.Open, _teamA, new(2026, 9, 1), new(2026, 9, 5), null, "1",
                created),
            new(Guid.NewGuid(), account.Id, ServiceAccountActionType.Review, ServiceAccountRequestStatus.Closed, _teamA, null, null, null, "2", created,
                new(2026, 9, 9, 12, 0, 0, TimeSpan.FromHours(3))),
            new(Guid.NewGuid(), account.Id, ServiceAccountActionType.Review, ServiceAccountRequestStatus.Closed, _teamA, null, null, null, "3", created)
        ];
        ActionFact[] actions = [Action(account.Id, ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, new(2026, 9, 15), new(2026, 9, 16))];

        ReportTrend trend = Compute(new([account], requests, actions, [], [], [], [], Teams(), Insight())).Trend!;

        trend.Points.Should().HaveCount(ServiceAccountInsights.TrendWeeks);
        trend.Points[^1].WeekStart.Should().Be(_monday);
        trend.UnknownCloseTime.Should().Be(1);
        TrendPoint before = trend.Points.Single(p => p.WeekStart == new DateOnly(2026, 8, 17));
        TrendPoint sep7 = trend.Points.Single(p => p.WeekStart == new DateOnly(2026, 9, 7));
        TrendPoint last = trend.Points[^1];
        trend.Points.Single(p => p.WeekStart == new DateOnly(2026, 8, 10)).OpenAtWeekEnd.Should().Be(0);
        before.OpenAtWeekEnd.Should().Be(2);
        sep7.OpenAtWeekEnd.Should().Be(1, "the second request closed on 9 September");
        sep7.OverdueAtWeekEnd.Should().Be(1);
        last.VerifiedClosures.Should().Be(1);
        last.PerformedActions.Should().Be(1);
    }

    [Fact]
    public void Risk_UsesTheSourceDate_ExcludesClosedAccounts_AndIsNeverAClosure()
    {
        AccountFact old = Account("SYN_OLD", _teamA), never = Account("SYN_NEVER", _teamA), absent = Account("SYN_ABSENT"), fresh = Account("SYN_FRESH"),
            closed = Account("SYN_CLOSED");
        DateOnly source = new(2026, 9, 1);
        ObservationFact[] observations =
        [
            new(old.Id, source, true, new DateTime(2025, 8, 1), new DateTime(2026, 5, 1), new DateTime(2026, 6, 2)),
            new(never.Id, source, true, null, null, null),
            new(absent.Id, source, false, null, null, null),
            new(fresh.Id, source, true, new DateTime(2026, 8, 1), new DateTime(2026, 8, 30), null),
            new(closed.Id, source, true, null, null, null)
        ];

        ServiceAccountReport report = Compute(new([old, never, absent, fresh, closed], [], [], [], [], [], [], Teams(),
            Insight(observations: observations, closed: [closed.Id])));

        report.Risk!.Candidates.Should().Be(3);
        report.Risk.Lines.Single(l => l.Account == "SYN_OLD").Categories.Should().Be("Son oturum ≥ 90 gün, Parola ≥ 365 gün",
            "the later of the two logon values is used and ages are measured from the source date");
        report.Risk.Lines.Should().Contain(l => l.Account == "SYN_NEVER" && l.Categories == "Oturum tarihi yok");
        report.Risk.Lines.Should().Contain(l => l.Account == "SYN_ABSENT" && l.Categories == "Son listede yok");
        report.Summary.VerifiedClosureAccounts.Should().Be(0);
        report.Directorate!.Single(r => r.Team == "SYN TEAM A").RiskCandidates.Should().Be(2);
    }

    [Fact]
    public void Directorate_HasOneRowPerTeam_AndUnownedLast_WithoutPeople()
    {
        AccountFact a = Account("SYN_A", _teamA), b = Account("SYN_B");
        RequestFact[] requests =
        [
            new(Guid.NewGuid(), a.Id, ServiceAccountActionType.Review, ServiceAccountRequestStatus.Open, _teamB, new(2026, 9, 1), new(2026, 9, 2), null, "1"),
            new(Guid.NewGuid(), b.Id, ServiceAccountActionType.Evaluate, ServiceAccountRequestStatus.Open, _teamB, null, null, null, "2")
        ];

        DirectorateRow[] rows = [.. Compute(new([a, b], requests, [], [], [], [], [], Teams(), Insight())).Directorate!];

        rows.Select(r => r.Team).Should().Equal("SYN TEAM A", "SYN TEAM B", "Ekip belirlenmedi");
        rows[1].Should().Be(new DirectorateRow("SYN ORG", "SYN TEAM B", 0, 2, 1, 1, 0, 0, 0));
        rows[2].OwnedAccounts.Should().Be(1);
    }

    [Fact]
    public void VersionOneSnapshots_StillDeserialize_WithoutTheNewSections()
    {
        ServiceAccountReport v1 = Compute(new([Account("SYN_V1")], [], [], [], [], [], [], Teams())) with { MetricDefinitionVersion = "sa-metrics-v1" };
        string json = JsonSerializer.Serialize(v1, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("\"rules\":{");

        // A payload stored before version 2 has no such properties at all.
        string stored = json[..json.IndexOf(",\"rules\":", StringComparison.Ordinal)] + "}";
        ServiceAccountReport restored = JsonSerializer.Deserialize<ServiceAccountReport>(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        restored.Rules.Should().BeNull();
        restored.Directorate.Should().BeNull();
        restored.Summary.UniqueAccounts.Should().Be(1);
    }
}
