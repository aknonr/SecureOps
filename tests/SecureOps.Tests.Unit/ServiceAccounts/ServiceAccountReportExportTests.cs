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
