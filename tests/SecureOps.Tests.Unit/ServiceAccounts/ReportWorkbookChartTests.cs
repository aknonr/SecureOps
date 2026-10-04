using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ReportWorkbookChartTests
{
    private static readonly DateTimeOffset _created = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly XNamespace _c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly Guid _teamA = Guid.NewGuid(), _teamB = Guid.NewGuid();

    private static ReportDocument Document(bool withInsights = true)
    {
        AccountFact first = new(Guid.NewGuid(), "SYN_XLSX_1", _teamA, null);
        AccountFact second = new(Guid.NewGuid(), "SYN_XLSX_2", _teamA, null);
        AccountFact third = new(Guid.NewGuid(), "SYN_XLSX_3", _teamB, null);
        UsageFact usage = new(Guid.NewGuid(), first.Id, UsageKind.ScheduledTask, null, null, false);
        InsightFacts insight = new([usage], new HashSet<Guid>(), "SYN EXEC", [], new HashSet<Guid>(),
            new Dictionary<Guid, string> { [_teamA] = "SYN ORG", [_teamB] = "SYN ORG" }, new RiskThresholds());
        ReportFacts facts = new([first, second, third], [], [], [], [], [], [],
            new Dictionary<Guid, string> { [_teamA] = "SYN TEAM <A>", [_teamB] = "SYN TEAM B" }, withInsights ? insight : null);
        ServiceAccountReport report = ServiceAccountMetrics.Compute(facts, new DateOnly(2026, 9, 14), new DateTimeOffset(2026, 9, 19, 23, 0, 0, TimeSpan.FromHours(3)), "SYN");
        return ReportDocument.From(report, Guid.NewGuid(), new string('c', 64), "SYN", _created, "Sentetik");
    }

    [Fact]
    public void Workbook_WithCharts_PassesTheOpenXmlSchemaValidator()
    {
        byte[] bytes = ReportWorkbookWriter.Write(Document());

        using var workbook = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        ValidationErrorInfo[] errors = [.. new OpenXmlValidator(FileFormatVersions.Office2016).Validate(workbook)];

        errors.Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}").Should().BeEmpty();
        workbook.WorkbookPart!.WorksheetParts.SelectMany(w => w.DrawingsPart?.ChartParts ?? []).Should().NotBeEmpty();
    }

    [Fact]
    public void EveryChartSeries_PointsAtTheDataSheet_AndCachesTheSameValuesAsTheReport()
    {
        ReportDocument document = Document();
        byte[] bytes = ReportWorkbookWriter.Write(document);
        ReportChart[] expected = [.. document.Dashboard!.Charts!.Where(c => !c.IsEmpty)];

        using ZipArchive zip = new(new MemoryStream(bytes));
        XDocument Part(string name) => XDocument.Load(zip.GetEntry(name)!.Open());
        zip.Entries.Count(e => e.FullName.StartsWith("xl/charts/chart", StringComparison.Ordinal)).Should().Be(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            XDocument chart = Part($"xl/charts/chart{i + 1}.xml");
            chart.Descendants(_c + "title").Single().Value.Should().Be(expected[i].Title);
            XElement[] series = [.. chart.Descendants(_c + "ser")];
            series.Should().HaveCount(expected[i].Series.Count);
            for (int s = 0; s < series.Length; s++)
            {
                series[s].Element(_c + "val")!.Descendants(_c + "f").Single().Value.Should().StartWith("'Grafik verisi'!$");
                series[s].Element(_c + "val")!.Descendants(_c + "v").Select(v => long.Parse(v.Value, System.Globalization.CultureInfo.InvariantCulture))
                    .Should().Equal(expected[i].Series[s].Values);
                series[s].Element(_c + "cat")!.Descendants(_c + "v").Select(v => v.Value).Should().Equal(expected[i].Categories);
            }
        }

        // The referenced cells hold the same numbers (Excel recalculates the chart from them).
        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(bytes, new SpreadsheetLimits(), null, out _);
        sheets[0].Name.Should().Be("Yönetici özeti");
        sheets[^1].Name.Should().Be("Grafik verisi");
        sheets[^1].Rows.Should().Contain(r => r.Cells.Values.Any(c => c.Text == "SYN TEAM <A>"), "XML-special characters survive as data");
    }

    [Fact]
    public void ChartWorkbook_IsDeterministic_AndHasNoFormulasMacrosOrExternalLinks()
    {
        ReportDocument document = Document();
        byte[] first = ReportWorkbookWriter.Write(document);

        ReportWorkbookWriter.Write(document).Should().Equal(first);
        using ZipArchive zip = new(new MemoryStream(first));
        zip.Entries.Select(e => e.FullName).Should().NotContain(n => n.Contains("vbaProject", StringComparison.Ordinal) || n.Contains("externalLink", StringComparison.Ordinal)
            || n.Contains("calcChain", StringComparison.Ordinal));
        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)))
        {
            new StreamReader(entry.Open()).ReadToEnd().Should().NotContain("<f>", entry.FullName);
        }
    }

    [Fact]
    public void ListExportWithoutExecutiveSummary_HasNoChartParts()
    {
        ReportDocument document = Document() with { Dashboard = null };

        using ZipArchive zip = new(new MemoryStream(ReportWorkbookWriter.Write(document)));

        zip.Entries.Should().NotContain(e => e.FullName.StartsWith("xl/charts/", StringComparison.Ordinal) || e.FullName.StartsWith("xl/drawings/", StringComparison.Ordinal));
        SpreadsheetReader.Read(ReportWorkbookWriter.Write(document), new SpreadsheetLimits(), null, out _).Should().NotContain(s => s.Name == "Grafik verisi");
    }

    [Fact]
    public void WorkbookWithoutCharts_StillPassesTheValidator()
    {
        byte[] bytes = ReportWorkbookWriter.Write(Document(withInsights: false) with { Dashboard = null });

        using var workbook = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        new OpenXmlValidator(FileFormatVersions.Office2016).Validate(workbook).Select(e => e.Description).Should().BeEmpty();
    }
}
