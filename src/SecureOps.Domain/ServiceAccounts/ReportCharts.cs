using System.Globalization;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Chart form; each one is drawn the same way on screen, in XLSX and in PDF.</summary>
public enum ReportChartKind
{
    /// <summary>Horizontal bars, one series (magnitude per category).</summary>
    Bar,

    /// <summary>Vertical grouped columns, up to three series on one axis.</summary>
    Column,

    /// <summary>Lines over weeks, up to four series on one axis.</summary>
    Line
}

/// <summary>One named series; values align with <see cref="ReportChart.Categories"/>.</summary>
public sealed record ReportChartSeries(string Name, IReadOnlyList<long> Values);

/// <summary>
/// Render-neutral chart. Every value is copied from the report payload (the same figure appears in a detail section);
/// nothing is computed for the picture.
/// </summary>
/// <param name="Key">Stable identifier, e.g. <c>trend</c>.</param>
/// <param name="Title">Turkish title.</param>
/// <param name="Kind">Form.</param>
/// <param name="Categories">Axis categories.</param>
/// <param name="Series">One to four series (one for <see cref="ReportChartKind.Bar"/>).</param>
/// <param name="Note">Short qualifier shown under the chart.</param>
public sealed record ReportChart(string Key, string Title, ReportChartKind Kind, IReadOnlyList<string> Categories,
    IReadOnlyList<ReportChartSeries> Series, string? Note = null)
{
    /// <summary>Largest value, at least 1, so a scale never divides by zero.</summary>
    public long Peak => Math.Max(1, Series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max());

    /// <summary>True when every value is zero; renderers show the table only.</summary>
    public bool IsEmpty => Series.All(s => s.Values.All(v => v == 0));
}

/// <summary>Builds the charts of a report from its payload. Version 1 snapshots get only the charts their data supports.</summary>
public static class ReportCharts
{
    /// <summary>Teams shown in the workload chart (same order and limit as the executive-summary table).</summary>
    public const int MaxTeams = 10;

    /// <summary>Weeks shown in the trend chart.</summary>
    public const int MaxWeeks = 12;

    /// <summary>Rounds an axis top up to an even 1-10 × 10^k step so the mid gridline is a whole, readable number.</summary>
    public static long AxisTop(long peak)
    {
        long magnitude = 1;
        while (magnitude * 10 <= peak)
        {
            magnitude *= 10;
        }

        foreach (long step in new long[] { 1, 2, 4, 5, 6, 8, 10 })
        {
            if (step * magnitude >= peak && step * magnitude % 2 == 0)
            {
                return step * magnitude;
            }
        }

        return 10 * magnitude;
    }

    /// <summary>Charts in display order.</summary>
    public static IReadOnlyList<ReportChart> Build(ServiceAccountReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        List<ReportChart> charts =
        [
            new("gmsa", "gMSA geçişi (devir kapsamı)", ReportChartKind.Bar,
                ["gMSA hedefli", "Geçişi gerçekleşen", "Bekleyen"],
                [new("Hesap", [report.Handover.GmsaTargeted, report.Handover.GmsaCompleted, report.Handover.GmsaPending])])
        ];

        if (report.Funnel is { Stages.Count: > 0 } funnel)
        {
            charts.Add(new("funnel", $"gMSA hunisi ({funnel.Population} hesap)", ReportChartKind.Bar,
                [.. funnel.Stages.Select(s => s.Label)], [new("Hesap", [.. funnel.Stages.Select(s => (long)s.Count)])],
                $"Uygun değil (huniden ayrı): {funnel.Ineligible}"));
        }

        if (report.Trend is { Points.Count: > 0 } trend)
        {
            TrendPoint[] points = [.. trend.Points.OrderBy(p => p.WeekStart).TakeLast(MaxWeeks)];
            charts.Add(new("trend", $"Trend (son {points.Length} hafta)", ReportChartKind.Line,
                [.. points.Select(p => p.WeekStart.ToString("dd.MM", CultureInfo.InvariantCulture))],
                [
                    new("Açık talep", [.. points.Select(p => (long)p.OpenAtWeekEnd)]),
                    new("Geciken", [.. points.Select(p => (long)p.OverdueAtWeekEnd)]),
                    new("Doğrulanmış kapanış", [.. points.Select(p => (long)p.VerifiedClosures)]),
                    new("Gerçekleşen işlem", [.. points.Select(p => (long)p.PerformedActions)])
                ], "Hafta sonu durumu; kapanış zamanı bilinmeyen talepler trende girmez."));
        }

        TeamWorkloadRow[] teams = [.. report.TeamWorkload.OrderByDescending(t => t.OwnedAccounts).ThenBy(t => t.Team, StringComparer.Ordinal).Take(MaxTeams)];
        if (teams.Length > 0)
        {
            charts.Add(new("teams", $"Ekip iş yükü (ilk {teams.Length})", ReportChartKind.Column,
                [.. teams.Select(t => t.Team)],
                [
                    new("Sahip olduğu hesap", [.. teams.Select(t => (long)t.OwnedAccounts)]),
                    new("Muhatap açık talep", [.. teams.Select(t => (long)t.OpenRequestsAsTarget)]),
                    new("Geciken", [.. teams.Select(t => (long)t.OverdueAsTarget)])
                ], report.TeamWorkload.Count > teams.Length ? $"Tüm {report.TeamWorkload.Count} ekip detay tablosunda." : null));
        }

        if (report.Rules is { ByConformance.Count: > 0 } rules)
        {
            charts.Add(new("conformance", "Kural uyumu", ReportChartKind.Bar,
                [.. rules.ByConformance.Select(c => c.Label)], [new("Hesap", [.. rules.ByConformance.Select(c => (long)c.Count)])],
                $"Değerlendirilen hesap: {rules.Assessed} · kural sürümü {rules.RuleSetVersion}"));
        }

        return charts;
    }
}
