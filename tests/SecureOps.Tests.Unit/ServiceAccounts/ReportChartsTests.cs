using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ReportChartsTests
{
    private static readonly Guid _teamA = Guid.NewGuid(), _teamB = Guid.NewGuid();

    private static ServiceAccountReport Report(bool withInsights)
    {
        AccountFact first = new(Guid.NewGuid(), "SYN_CHART_1", _teamA, null);
        AccountFact second = new(Guid.NewGuid(), "SYN_CHART_2", _teamA, null);
        AccountFact third = new(Guid.NewGuid(), "SYN_CHART_3", _teamB, null);
        UsageFact usage = new(Guid.NewGuid(), first.Id, UsageKind.ScheduledTask, null, null, false);
        InsightFacts insight = new([usage], new HashSet<Guid>(), "SYN EXEC", [], new HashSet<Guid>(),
            new Dictionary<Guid, string> { [_teamA] = "SYN ORG", [_teamB] = "SYN ORG" }, new RiskThresholds());
        ReportFacts facts = new([first, second, third], [], [], [], [], [], [],
            new Dictionary<Guid, string> { [_teamA] = "SYN TEAM A", [_teamB] = "SYN TEAM B" }, withInsights ? insight : null);
        return ServiceAccountMetrics.Compute(facts, new DateOnly(2026, 9, 14), new DateTimeOffset(2026, 9, 19, 23, 0, 0, TimeSpan.FromHours(3)), "SYN");
    }

    [Fact]
    public void EveryChartValue_IsCopiedFromThePayload_NotRecomputed()
    {
        ServiceAccountReport report = Report(true);
        IReadOnlyList<ReportChart> charts = ReportCharts.Build(report);

        charts.Select(c => c.Key).Should().Equal("gmsa", "funnel", "trend", "teams", "conformance");
        charts.Single(c => c.Key == "gmsa").Series.Single().Values.Should()
            .Equal(report.Handover.GmsaTargeted, report.Handover.GmsaCompleted, report.Handover.GmsaPending);
        charts.Single(c => c.Key == "funnel").Series.Single().Values.Should().Equal(report.Funnel!.Stages.Select(s => (long)s.Count));

        ReportChart trend = charts.Single(c => c.Key == "trend");
        trend.Kind.Should().Be(ReportChartKind.Line);
        trend.Categories.Should().HaveCount(Math.Min(ReportCharts.MaxWeeks, report.Trend!.Points.Count));
        trend.Series.Select(s => s.Name).Should().Equal("Açık talep", "Geciken", "Doğrulanmış kapanış", "Gerçekleşen işlem");
        trend.Series[0].Values.Should().Equal(report.Trend.Points.OrderBy(p => p.WeekStart).TakeLast(ReportCharts.MaxWeeks).Select(p => (long)p.OpenAtWeekEnd));

        ReportChart teams = charts.Single(c => c.Key == "teams");
        teams.Categories.Should().Equal(["SYN TEAM A", "SYN TEAM B"], "the order follows owned accounts, as in the executive-summary table");
        teams.Series[0].Values.Should().Equal(2, 1);
        charts.Single(c => c.Key == "conformance").Series.Single().Values.Should().Equal(report.Rules!.ByConformance.Select(c => (long)c.Count));
    }

    [Fact]
    public void Charts_StayWithinTheirFormLimits()
    {
        foreach (ReportChart chart in ReportCharts.Build(Report(true)))
        {
            chart.Series.Should().NotBeEmpty();
            chart.Series.Should().OnlyContain(s => s.Values.Count == chart.Categories.Count, chart.Key);
            int limit = chart.Kind switch { ReportChartKind.Bar => 1, ReportChartKind.Column => 3, _ => 4 };
            chart.Series.Count.Should().BeLessThanOrEqualTo(limit, "one axis and at most four identities per chart");
            chart.Peak.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void VersionOneSnapshot_GetsOnlyTheChartsItsDataSupports()
    {
        IReadOnlyList<ReportChart> charts = ReportCharts.Build(Report(false) with { MetricDefinitionVersion = "sa-metrics-v1" });

        charts.Select(c => c.Key).Should().Equal("gmsa", "teams");
    }
}
