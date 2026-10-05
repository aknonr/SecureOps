using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseWorkbookCompatibilityTests
{
    [Fact]
    public async Task CorporateWorkbook_FourSheets_ReadableTextAndSeparateEvidence()
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(TestContext.Current.CancellationToken)).Records[0];
        var actor = Guid.NewGuid();
        var record = new InUseRecord(Guid.NewGuid(), source, "synthetic-hash", 1, 2, null, null,
            new(1, [], "Review evidence retained", actor, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        InUseReport report = InUseWorkbook.Create(record, actor, DateTimeOffset.UtcNow);
        using var zip = new ZipArchive(new MemoryStream(report.Content));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument Read(string part)
        {
            using Stream stream = zip.GetEntry(part)!.Open();
            return XDocument.Load(stream);
        }
        Read("xl/workbook.xml").Descendants(ns + "sheet").Select(s => (string?)s.Attribute("name"))
            .Should().Equal("NMS", "CheckList_THY", "CheckList_TEKNIK", "Sunucular");
        XDocument styles = Read("xl/styles.xml");
        styles.Descendants(ns + "b").Should().ContainSingle();
        styles.Descendants(ns + "alignment").Should().OnlyContain(a => (string?)a.Attribute("wrapText") == "1");
        for (int sheet = 1; sheet <= 4; sheet++)
        {
            XDocument xml = Read($"xl/worksheets/sheet{sheet}.xml");
            xml.Descendants(ns + "t").Select(t => t.Value).Should().Equal(report.Sheets[sheet - 1].Rows.SelectMany(r => r));
            xml.Descendants(ns + "f").Should().BeEmpty();
            xml.Descendants(ns + "mergeCells").Should().BeEmpty();
            if (sheet is 2 or 3)
            { xml.Descendants(ns + "c").Should().BeEmpty(); }
            else
            {
                xml.Descendants(ns + "col").Should().HaveCount(sheet == 1 ? 22 : source.Servers.Count + 1);
                xml.Descendants(ns + "pane").Should().ContainSingle(p => (string?)p.Attribute("state") == "frozen");
                xml.Descendants(ns + "row").Should().OnlyContain(r => (double)r.Attribute("ht")! >= 30);
                xml.Descendants(ns + "c").Should().OnlyContain(c => (string?)c.Attribute("t") == "inlineStr");
            }
        }
        report.EvidenceSheets.Select(s => s.Name).Should().Equal("Provenance", "ReviewEvidence");
        report.EvidenceSheets[0].Rows.Should().Contain(r => r.Contains("Review evidence retained"));
        zip.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)).Should().Be(4);
    }

    [Theory]
    [InlineData("Deniz Örnek", "Deniz_Örnek")]
    [InlineData("../../CON\\:<bad>|?*", "CON___bad")]
    [InlineData("CON", "_CON")]
    [InlineData("LPT1", "_LPT1")]
    [InlineData(null, "preparer")]
    public void Download_UsesFrozenMetadata_SafeOriginalActorAndUtc(string? label, string expectedActor)
    {
        var actor = Guid.Parse("12345678-1111-2222-3333-444444444444");
        var report = new InUseReport(Guid.NewGuid(), 13, 3, "hash", "legacy.xlsx", [1, 2], [])
        {
            SourceCode = "OR-0000123",
            PreparedBy = actor,
            PreparedByLabel = label,
            PreparedAt = DateTimeOffset.Parse("2026-09-18T20:29:46+03:00", System.Globalization.CultureInfo.InvariantCulture)
        };
        string name = InUseReportNames.Download(report);
        name.Should().Be($"InUse_OR-0000123_{expectedActor}-12345678_20260918_172946Z_v13.xlsx");
        name.Should().NotContainAny("/", "\\", ":", "<", ">", "|", "?", "*");
        report.FileName.Should().Be("legacy.xlsx");
        report.Content.Should().Equal(1, 2);
    }

    [Fact]
    public void Download_OldArchiveUsesRetainedCode_AndLabelsCannotRemoveIdentityTimeVersion()
    {
        var report = new InUseReport(Guid.NewGuid(), 13, 3, "hash", "old-guid.xlsx", [],
            [new("Provenance", [new[] { "Code", "OR-0000456" }])])
        { PreparedBy = Guid.NewGuid(), PreparedByLabel = new string('x', 500) };
        string name = InUseReportNames.Download(report);
        name.Should().StartWith("InUse_OR-0000456_").And.EndWith("_time-unknown_v13.xlsx");
        name.Length.Should().BeLessThan(180);
        InUseReportNames.Download(report with { PreparedBy = Guid.NewGuid() }).Should().NotBe(name);
    }
}
