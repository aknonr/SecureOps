using System.Text;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ReportPdfChartTests
{
    private static readonly DateTimeOffset _created = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    private static ReportDocument Document(params ReportChart[] charts) =>
        new("Servis Hesapları Haftalık Rapor", [("Kapsam", "SYN")], [new ReportSection("Özet", ["Gösterge", "Değer"], [["Tekil hesap", 3]])], _created,
            new ReportDashboard("SYN · dönem", [new("Tekil hesap", 3)], [], charts));

    private static ReportChart Trend => new("trend", "Trend (son 3 hafta)", ReportChartKind.Line, ["01.09", "08.09", "15.09"],
        [new("Açık talep", [4, 6, 3]), new("Geciken", [1, 0, 2])]);

    private static ReportChart Teams => new("teams", "Ekip iş yükü (ilk 2)", ReportChartKind.Column, ["SYN EKİP A", "SYN EKİP B"],
        [new("Sahip olduğu hesap", [12, 7]), new("Muhatap açık talep", [3, 1]), new("Geciken", [1, 0])]);

    private static ReportChart Funnel => new("funnel", "gMSA hunisi (9 hesap)", ReportChartKind.Bar, ["Bilinmiyor", "Uygun", "Dönüştü"], [new("Hesap", [5, 3, 1])]);

    private static ReportChart Zero => new("gmsa", "gMSA geçişi", ReportChartKind.Bar, ["gMSA hedefli"], [new("Hesap", [0])]);

    [Fact]
    public void ChartPage_FollowsTheFirstPage_AndPrintsEveryValueAndCategory()
    {
        byte[] pdf = ReportPdfWriter.Write(Document(Funnel, Trend, Teams));
        string raw = Encoding.Latin1.GetString(pdf);

        raw.Should().Contain("/Count 2", "the first page holds this small document; the chart page follows it");
        IReadOnlyList<string> lines = ReportPdfWriter.ExtractLines(pdf);
        lines[0].Should().Be("Servis Hesapları Haftalık Rapor");
        int heading = lines.ToList().FindIndex(l => l.StartsWith("GRAFİKLER", StringComparison.Ordinal));
        heading.Should().BeGreaterThan(lines.ToList().IndexOf("Sayfa 1/2"), "the chart page is the second page");
        lines.Should().Contain(["gMSA hunisi (9 hesap)", "Trend (son 3 hafta)", "Ekip iş yükü (ilk 2)", "Bilinmiyor", "SYN EKİP A", "15.09", "12", "7", "Sayfa 2/2"]);
        lines.Should().Contain("Açık talep").And.Contain("Geciken", "multi-series charts carry a legend");
    }

    [Fact]
    public void ChartPage_IsDeterministic_AndKeepsTheXrefTableValid()
    {
        ReportDocument document = Document(Funnel, Trend, Teams);
        byte[] pdf = ReportPdfWriter.Write(document);

        ReportPdfWriter.Write(document).Should().Equal(pdf);
        string raw = Encoding.Latin1.GetString(pdf);
        int startxref = int.Parse(raw[(raw.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        string[] offsets = [.. raw[startxref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ", StringComparison.Ordinal))];
        for (int i = 0; i < offsets.Length; i++)
        {
            raw[int.Parse(offsets[i][..10], System.Globalization.CultureInfo.InvariantCulture)..].Should().StartWith($"{i + 1} 0 obj");
        }

        raw.Should().NotContain("/JavaScript").And.NotContain("/URI").And.NotContain("/EmbeddedFile").And.NotContain("/FlateDecode");
    }

    [Fact]
    public void AllZeroCharts_AreLeftOut_AndNoChartsMeansNoChartPage()
    {
        ReportPdfWriter.ExtractLines(ReportPdfWriter.Write(Document(Zero))).Should().NotContain(l => l.StartsWith("GRAFİKLER", StringComparison.Ordinal));
        Encoding.Latin1.GetString(ReportPdfWriter.Write(Document(Zero, Funnel))).Should().Contain("/Count 2");
        Encoding.Latin1.GetString(ReportPdfWriter.Write(Document())).Should().Contain("/Count 1");
    }

    [Fact]
    public void SnapshotDocument_DrawsTheSameChartsAsTheWorkbook()
    {
        AccountFact account = new(Guid.NewGuid(), "SYN_PDF_1", null, null);
        ServiceAccountReport report = ServiceAccountMetrics.Compute(new ReportFacts([account], [], [], [], [], [], [], new Dictionary<Guid, string>()),
            new DateOnly(2026, 9, 14), new DateTimeOffset(2026, 9, 19, 23, 0, 0, TimeSpan.FromHours(3)), "SYN");
        var document = ReportDocument.From(report, Guid.NewGuid(), new string('d', 64), "SYN", _created, "Sentetik");

        IReadOnlyList<string> lines = ReportPdfWriter.ExtractLines(ReportPdfWriter.Write(document));

        foreach (ReportChart chart in document.Dashboard!.Charts!.Where(c => !c.IsEmpty))
        {
            lines.Should().Contain(chart.Title);
        }
    }
}
