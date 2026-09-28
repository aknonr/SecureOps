using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed class InUseDisplayTests
{
    [Theory]
    [InlineData("G&#246;r&#252;nt&#252; &amp; Kontrol", "Görüntü & Kontrol")]
    [InlineData("Görüntü & Kontrol", "Görüntü & Kontrol")]
    [InlineData("Normal text", "Normal text")]
    [InlineData("&lt;script&gt;alert(1)&lt;/script&gt;", "<script>alert(1)</script>")]
    [InlineData("&amp;#246;", "&#246;")]
    public void Display_DecodesExactlyOnce(string raw, string expected) => InUseDisplayText.Decode(raw).Should().Be(expected);

    [Fact]
    public async Task OldSnapshots_KeepIdentityHashAndRawEvidence_WhilePreviewAndXlsxAgree()
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(default)).Records[0];
        source = source with
        {
            Requester = new("G&#246;zlem &amp; Kontrol", "TuruncuHat: KEY.p_rel_requester"),
            Servers = [new("100", new Dictionary<string, InUseEvidence>
            {
                ["SERVICE NAME (ÜRÜN/UYGULAMA)"] = new("G&#246;r&#252;nt&#252; &amp; Kontrol", "TuruncuHat: KEY.service"),
                ["HOSTNAME"] = new("&lt;script&gt;alert(1)&lt;/script&gt;", "TuruncuHat: SET.name"),
                ["CITY"] = new("&#61;1+1", "TuruncuHat: KEY.city"),
                ["ITMC_Service_ID"] = new("&amp;id", "TuruncuHat: SET.id")
            })]
        };
        var record = new InUseRecord(Guid.NewGuid(), source, "unchanged-hash", 2, 3, null, null,
            new(2, [], "retained notes", Guid.NewGuid(), DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        string stored = JsonSerializer.Serialize(record);
        InUseRecord old = JsonSerializer.Deserialize<InUseRecord>(stored)!;
        InUseReport report = InUseWorkbook.Create(old, Guid.NewGuid(), DateTimeOffset.UtcNow);
        report.Sheets[3].Rows.Single(r => r[0] == "SERVICE NAME (ÜRÜN/UYGULAMA)")[1].Should().Be("Görüntü & Kontrol");
        report.Sheets[3].Rows.Single(r => r[0] == "CITY")[1].Should().Be("'=1+1");
        report.Sheets[0].Rows[1][3].Should().Be("&amp;id");
        using var zip = new ZipArchive(new MemoryStream(report.Content));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using Stream input = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(input);
        xml.Descendants(ns + "t").Select(t => t.Value).Should().Equal(report.Sheets[0].Rows.SelectMany(r => r));
        xml.Descendants(ns + "f").Should().BeEmpty();
        xml.Descendants("script").Should().BeEmpty();
        JsonSerializer.Serialize(old).Should().Be(stored);
    }

    [Fact]
    public void Profiles_DuplicateMissingNamesAndPrefixCollisions_RemainDistinct()
    {
        ApplicationUser User(string id, string? name, string? login = null) => new(Guid.Parse(id), "oidc:opaque-" + id, "oidc",
            AccessStatus.Approved, default, default, null, 1, [], [], login, name);
        ApplicationUser[] users = [User("10000000-0000-0000-0000-000000000001", "Deniz Örnek"),
            User("20000000-0000-0000-0000-000000000001", "Deniz Örnek"),
            User("30000000-0000-0000-0000-000000000001", null), User("30000000-0000-0000-0000-000000000002", null),
            User("40000000-0000-0000-0000-000000000001", null, "synthetic.login")];
        IReadOnlyDictionary<Guid, string> labels = InUseAssigneeLabels.Create(users);
        labels.Keys.Should().BeEquivalentTo(users.Select(u => u.Id));
        labels.Values.Distinct().Should().HaveCount(5);
        labels.Values.Should().NotContain(v => v.Contains("oidc:"));
        labels[users[0].Id].Should().StartWith("Deniz Örnek");
        labels[users[2].Id].Should().StartWith("Kullanıcı adı çözümlenemedi");
        labels[users[4].Id].Should().Be("synthetic.login");
    }

    [Theory]
    [InlineData("G&#246;zlem &#350;ah&#305;s &amp; Kontrol", "Gözlem Şahıs & Kontrol")]
    [InlineData("Gözlem Şahıs", "Gözlem Şahıs")]
    [InlineData("&lt;b&gt;", "<b>")]
    [InlineData("&amp;lt;b&amp;gt;", "&lt;b&gt;")]
    public async Task Reporter_CurrentAndRetained_UseSamePlainTextInNewExcel_WithoutRewritingSnapshots(string raw, string expected)
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(default)).Records[0];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        source = source with
        {
            Servers = new[] { "ExactMatch", "Forbidden", "Stale" }.Select((state, i) => new InUseServer($"{100 + i}", new Dictionary<string, InUseEvidence>())
            { RelatedRequestReporter = new(source.Id, $"{100 + i}", "OR-200", "OrCode", "200", "OR-200", raw, "800", state, "Returned", "Returned", now.AddMinutes(-5)) }).ToArray()
        };
        var record = new InUseRecord(Guid.NewGuid(), source, "preserved-source-hash", 2, 3, null, null,
            new(2, [], "", Guid.NewGuid(), now), now);
        string original = JsonSerializer.Serialize(record);
        InUseReport report = InUseWorkbook.Create(record, Guid.NewGuid(), now);
        IReadOnlyList<string>[] rows = report.EvidenceSheets[1].Rows.Where(r => r[1] == InUseRelatedRequestReporter.Label).ToArray();
        rows.Select(r => r[2]).Should().Equal(expected, expected, expected);
        rows.Select(r => r[3]).Should().Equal("RFC eşleşti", "Erişim reddedildi", "Güncel değil");
        using var zip = new ZipArchive(new MemoryStream(report.Content));
        zip.GetEntry("xl/worksheets/sheet6.xml").Should().BeNull();
        JsonSerializer.Deserialize<InUseReport>(JsonSerializer.Serialize(report))!.EvidenceSheets
            .Should().BeEquivalentTo(report.EvidenceSheets);
        JsonSerializer.Serialize(record).Should().Be(original);
    }
}
