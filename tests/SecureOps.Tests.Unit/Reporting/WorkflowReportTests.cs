using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Tests.Unit.Reporting;

public sealed class WorkflowReportTests
{
    [Fact]
    public void UnknownOrigin_PreservesLegacyPreparationSerializationAndFingerprint()
    {
        var content = new AnnouncementContent("OCO-1", "scope", "subject", "2026-09-18", "2026-09-18T01:00:00Z",
            "2026-09-18T02:00:00Z", "description", "impact", "checks", "", [], [], "bundle");
        var draft = new AnnouncementDraft(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, content, "operator@example.invalid", "hash");
        var preparation = new PreparedAnnouncement(Guid.NewGuid(), draft, DateTimeOffset.UtcNow, "Synthetic", "", "<p>test</p>", [1, 2], new Dictionary<string, string>()) { Fingerprint = "" };
        string current = JsonSerializer.Serialize(preparation);
        current.Should().NotContain("Synthetic\":");
        using var legacy = JsonDocument.Parse(current);
        legacy.RootElement.GetProperty("Draft").TryGetProperty("Synthetic", out _).Should().BeFalse();
        string fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(current)));
        AnnouncementService.PreparationFingerprint(preparation).Should().Be(fingerprint);
        PreparedAnnouncement reloaded = JsonSerializer.Deserialize<PreparedAnnouncement>(current)!;
        reloaded.Draft.Synthetic.Should().BeNull();
        AnnouncementService.PreparationFingerprint(reloaded).Should().Be(fingerprint);
        AnnouncementService.PreparationFingerprint(preparation with { Draft = draft with { Synthetic = true } }).Should().NotBe(fingerprint);
    }

    [Fact]
    public void Validation_BoundsDatesZonesFiltersAndPageArithmetic()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var request = new WorkflowReportRequest(now.AddDays(-1), now);
        WorkflowReportService.Valid(request).Should().BeTrue();
        WorkflowReportService.Valid(request with { From = now }).Should().BeFalse();
        WorkflowReportService.Valid(request with { From = now.AddDays(-93) }).Should().BeFalse();
        WorkflowReportService.Valid(request with { TimeZone = "Local" }).Should().BeFalse();
        WorkflowReportService.Valid(new WorkflowReportFilter(Page: int.MaxValue)).Should().BeFalse();
        WorkflowReportService.Valid(new WorkflowReportFilter(Module: "private-module")).Should().BeFalse();
        WorkflowMetricCatalog.Definitions.Select(x => x.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Workbook_UsesExactIdsSafeStringsAndVisibleSnapshotContext()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var report = new WorkflowReport(id, now, now.AddHours(1), new(now.AddDays(-1), now), [],
            [new("InUse.Archived", "000123456789012345678901234", "InUse", id, "=HYPERLINK(\"bad\")", "InUse", "Archived", "operator", "reviewer", now, "confirmed")], 1, 1, 25, ["HistoryIncomplete"]);
        using var zip = new ZipArchive(new MemoryStream(WorkflowReportWorkbook.Create(report)));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet3.xml")!.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        xml.Descendants(ns + "f").Should().BeEmpty();
        xml.Descendants(ns + "c").Should().OnlyContain(c => c.Attribute("t")!.Value == "inlineStr");
        xml.Descendants(ns + "t").Select(x => x.Value).Should().Contain("000123456789012345678901234").And.Contain("'=HYPERLINK(\"bad\")");
        using var context = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        context.ReadToEnd().Should().Contain(id.ToString("D")).And.Contain("UTC+03:00");
    }
}
