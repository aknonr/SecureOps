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
        report.Sheets[0].Rows.Single(r => r[0] == "SERVICE NAME (ÜRÜN/UYGULAMA)")[1].Should().Be("Görüntü & Kontrol");
        report.Sheets[0].Rows.Single(r => r[0] == "CITY")[1].Should().Be("'=1+1");
        report.Sheets[3].Rows[1][3].Should().Be("&amp;id");
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
        labels[users[2].Id].Should().StartWith("Profil adı bekleniyor");
        labels[users[4].Id].Should().Be("synthetic.login");
    }
}
