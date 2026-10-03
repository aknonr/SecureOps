using System.Globalization;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountReportExportTests
{
    private static readonly DateTimeOffset _created = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Xlsx_HasNoFormulasMacrosOrLinks_UsesDateSerials_NeutralizesFormulaText_AndIsDeterministic()
    {
        ReportDocument document = Document("=HYPERLINK(\"x\")");
        byte[] first = ReportWorkbookWriter.Write(document);
        byte[] second = ReportWorkbookWriter.Write(document);
        first.Should().Equal(second);
        using ZipArchive zip = new(new MemoryStream(first));
        zip.Entries.Select(e => e.FullName).Should().NotContain(n => n.Contains("vba", StringComparison.OrdinalIgnoreCase)
            || n.Contains("calcChain", StringComparison.Ordinal) || n.Contains("externalLink", StringComparison.Ordinal));
        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
        {
            using StreamReader reader = new(entry.Open());
            string xml = reader.ReadToEnd();
            xml.Should().NotContain("<f>").And.NotContain("<f ").And.NotContain("shared");
        }

        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(first, new SpreadsheetLimits(), null, out _);
        SheetData plans = sheets.Single(s => s.Name == "Tarihli açık planlar");
        SheetCell start = plans.Rows[1].Cells["C"];
        start.Number.Should().Be(new DateOnly(2026, 9, 30).DayNumber - new DateOnly(1899, 12, 30).DayNumber);
        using (StreamReader styles = new(zip.GetEntry("xl/styles.xml")!.Open()))
        {
            styles.ReadToEnd().Should().Contain("numFmtId=\"14\"");
        }

        sheets.SelectMany(s => s.Rows).SelectMany(r => r.Cells.Values).Select(c => c.Text).Should().Contain("'=HYPERLINK(\"x\")");
    }

    [Fact]
    public void Pdf_IsStructurallyValid_KeepsTurkishText_AndReconcilesWithXlsx()
    {
        ReportDocument document = Document("İşlem ğüşıöç ĞÜŞİÖÇ");
        byte[] pdf = ReportPdfWriter.Write(document);
        ReportPdfWriter.Write(document).Should().Equal(pdf);
        string raw = Encoding.Latin1.GetString(pdf);
        raw.Should().StartWith("%PDF-1.4").And.EndWith("%%EOF\n");
        int startxref = int.Parse(raw[(raw.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], CultureInfo.InvariantCulture);
        raw[startxref..].Should().StartWith("xref");
        string[] offsets = raw[startxref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ", StringComparison.Ordinal)).ToArray();
        offsets.Should().NotBeEmpty();
        for (int i = 0; i < offsets.Length; i++)
        {
            int offset = int.Parse(offsets[i][..10], CultureInfo.InvariantCulture);
            raw[offset..].Should().StartWith($"{i + 1} 0 obj");
        }

        IReadOnlyList<string> lines = ReportPdfWriter.ExtractLines(pdf);
        lines.Should().Contain(l => l.Contains("İşlem ğüşıöç ĞÜŞİÖÇ", StringComparison.Ordinal));
        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(ReportWorkbookWriter.Write(document), new SpreadsheetLimits(), null, out _);
        foreach (SheetRow row in sheets.Single(s => s.Name == "Özet").Rows.Skip(1))
        {
            string label = row.Cells["A"].Text!;
            string value = row.Cells["B"].Display!;
            lines.Should().Contain(l => l.StartsWith(label, StringComparison.Ordinal) && l.TrimEnd().EndsWith("| " + value, StringComparison.Ordinal),
                $"PDF and XLSX must show the same snapshot value for {label}");
        }
    }

    [Fact]
    public void Xlsx_TableSheets_HaveAShadedFrozenFilteredHeader_AndTheCoverStartsWithTheTitle()
    {
        ReportDocument document = Document("SYN_SVC_01");
        document.Dashboard.Should().NotBeNull("snapshots render an executive summary first");
        byte[] bytes = ReportWorkbookWriter.Write(document);
        using ZipArchive zip = new(new MemoryStream(bytes));
        string Part(string name)
        {
            using StreamReader reader = new(zip.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }

        string workbook = Part("xl/workbook.xml");
        string cover = Part("xl/worksheets/sheet2.xml");
        string table = Part("xl/worksheets/sheet3.xml");
        cover.Should().NotContain("<pane").And.NotContain("<autoFilter");
        table.Should().Contain("state=\"frozen\"").And.Contain("<autoFilter ref=\"A1:");
        table.IndexOf("<sheetViews>", StringComparison.Ordinal).Should().BeLessThan(table.IndexOf("<cols>", StringComparison.Ordinal), "schema order");
        table.IndexOf("</sheetData>", StringComparison.Ordinal).Should().BeLessThan(table.IndexOf("<autoFilter", StringComparison.Ordinal), "schema order");
        workbook.Should().Contain("_xlnm._FilterDatabase").And.Contain("localSheetId=\"2\"", "sheet 1 is the executive summary, 2 the cover");
        workbook.IndexOf("</sheets>", StringComparison.Ordinal).Should().BeLessThan(workbook.IndexOf("<definedNames>", StringComparison.Ordinal));
        Part("xl/styles.xml").Should().Contain("<cellXfs count=\"9\">").And.Contain("patternType=\"solid\"");
        SpreadsheetReader.Read(bytes, new SpreadsheetLimits(), ["Rapor"], out _).Single().Rows[0].Cells["A"].Text.Should().Be(document.Title);
    }

    [Fact]
    public void Pdf_IsLandscape_UsesABoldFontForHeadings_AndKeepsDashes()
    {
        ReportDocument document = Document("SYN_SVC_01") with { Title = "Servis hesapları — SYN" };
        byte[] pdf = ReportPdfWriter.Write(document);
        string raw = Encoding.Latin1.GetString(pdf);
        raw.Should().Contain("/MediaBox [0 0 841.89 595.28]").And.Contain("/BaseFont /Courier-Bold").And.Contain(" re f");
        IReadOnlyList<string> lines = ReportPdfWriter.ExtractLines(pdf);
        lines[0].Should().Be("Servis hesapları — SYN");
        lines.Should().Contain(l => l.StartsWith("Sayfa 1/", StringComparison.Ordinal));
    }

    [Fact]
    public void Dashboard_IsTheFirstSheet_WithMergedTiles_ValuesOnly_AndTheSameFiguresInThePdf()
    {
        ReportDocument document = Document("SYN_SVC_01") with
        {
            Dashboard = new ReportDashboard("SYN ORG · dönem", [new("Tekil hesap", 390), new("Açık talep", 123), new("Geciken iş", 7, "takvim günü")],
                [new ReportSection("Ekiplerde hesap ve bekleyen iş", ["Ekip", "Hesap"], [[new ReportCell("SYN WASAS"), new ReportCell(Number: 120)]])])
        };
        byte[] bytes = ReportWorkbookWriter.Write(document);
        using ZipArchive zip = new(new MemoryStream(bytes));
        using StreamReader reader = new(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        string sheet = reader.ReadToEnd();
        sheet.Should().Contain("<mergeCells").And.NotContain("<f>").And.NotContain("<f ");
        sheet.IndexOf("</sheetData>", StringComparison.Ordinal).Should().BeLessThan(sheet.IndexOf("<mergeCells", StringComparison.Ordinal));
        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(bytes, new SpreadsheetLimits(), null, out _);
        sheets[0].Name.Should().Be("Yönetici özeti");
        sheets[0].Rows.SelectMany(r => r.Cells.Values).Should().Contain(c => c.Number == 390).And.Contain(c => c.Text == "Geciken iş");
        sheets.Select(s => s.Name).Should().Contain("Özet", "the detail sheets stay");

        IReadOnlyList<string> lines = ReportPdfWriter.ExtractLines(ReportPdfWriter.Write(document));
        lines.Should().Contain("Tekil hesap").And.Contain("390").And.Contain(l => l.StartsWith("YÖNETİCİ ÖZETİ · EKİPLERDE", StringComparison.Ordinal));
    }

    private static ReportDocument Document(string account)
    {
        var a = Guid.NewGuid();
        ReportFacts facts = new(
            [new AccountFact(a, account, null, null)],
            [new RequestFact(Guid.NewGuid(), a, ServiceAccountActionType.PasswordChange, ServiceAccountRequestStatus.Open, null, new(2026, 9, 30), new(2026, 9, 30), null, "1")],
            [new ActionFact(Guid.NewGuid(), a, new ActionFacts(ServiceAccountActionType.PasswordChange, ServiceAccountActionResult.Performed,
                ServiceAccountRecordKind.Intermediate, new(2026, 9, 15), null, false, true, false, false), TimePrecision.DateOnly, null, null)],
            [new CommunicationFact(Guid.NewGuid(), CommunicationDirection.Outgoing, CommunicationKind.FirstRequest, TimePrecision.DateOnly, new(2026, 9, 16), null, 1)],
            [], [], [], new Dictionary<Guid, string>());
        ServiceAccountReport report = ServiceAccountMetrics.Compute(facts, new(2026, 9, 14), new(2026, 9, 19, 23, 0, 0, TimeSpan.FromHours(3)), "Sentetik kapsam");
        return ReportDocument.From(report, Guid.NewGuid(), new string('a', 64), "Sentetik nüsha", _created, "Sentetik Kullanıcı");
    }
}
