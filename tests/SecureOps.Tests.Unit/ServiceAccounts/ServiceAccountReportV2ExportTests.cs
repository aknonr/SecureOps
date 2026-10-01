using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountReportV2ExportTests
{
    private static readonly DateTimeOffset _created = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid _team = Guid.NewGuid();

    private static ServiceAccountReport Report(bool withInsights)
    {
        AccountFact account = new(Guid.NewGuid(), "SYN_EXPORT", _team, null);
        UsageFact usage = new(Guid.NewGuid(), account.Id, UsageKind.ScheduledTask, null, null, false);
        ObservationFact observation = new(account.Id, new DateOnly(2026, 9, 1), true, null, null, null);
        InsightFacts insight = new([usage], new HashSet<Guid>(), "SYN EXEC", [observation], new HashSet<Guid>(),
            new Dictionary<Guid, string> { [_team] = "SYN ORG" }, new RiskThresholds());
        ReportFacts facts = new([account], [], [], [], [], [], [], new Dictionary<Guid, string> { [_team] = "SYN TEAM" }, withInsights ? insight : null);
        return ServiceAccountMetrics.Compute(facts, new DateOnly(2026, 9, 14), new DateTimeOffset(2026, 9, 19, 23, 0, 0, TimeSpan.FromHours(3)), "SYN");
    }

    [Fact]
    public void VersionTwoSnapshot_ExportsEveryNewSection_InBothFormats()
    {
        var document = ReportDocument.From(Report(true), Guid.NewGuid(), new string('a', 64), "SYN", _created, "Sentetik");

        document.Sections.Select(s => s.Title).Should().ContainInOrder("Direktörlük görünümü", "Bilgi bankası kuralları", "İncelenecek hesaplar", "gMSA hunisi",
            "Trend (son haftalar)", "Risk adayları", "Tanımlar ve notlar");
        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(ReportWorkbookWriter.Write(document), new SpreadsheetLimits(), null, out _);
        sheets.Single(s => s.Name == "İncelenecek hesaplar").Rows[1].Cells["E"].Text.Should().Be("KB-GOREV");
        sheets.Single(s => s.Name == "Risk adayları").Rows[1].Cells["C"].Text.Should().Be("Oturum tarihi yok");
        sheets.Single(s => s.Name == "Trend (son haftalar)").Rows.Should().HaveCount(1 + ServiceAccountInsights.TrendWeeks + 2);
        ReportPdfWriter.Write(document).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void VersionOneSnapshot_RendersWithoutTheNewSections()
    {
        var document = ReportDocument.From(Report(false) with { MetricDefinitionVersion = "sa-metrics-v1" }, Guid.NewGuid(), new string('b', 64), null,
            _created, "Sentetik");

        document.Sections.Select(s => s.Title).Should().NotContain(["Direktörlük görünümü", "gMSA hunisi", "Risk adayları"]);
        document.Sections[^1].Title.Should().Be("Tanımlar ve notlar");
    }
}
