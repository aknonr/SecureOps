using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Theory]
    [InlineData("DEV", "TEST", "TEST")]
    [InlineData("PROD", "DEV", "PROD")]
    [InlineData("DEV", "", null)]
    [InlineData("PROD", "", "PROD")]
    public async Task RequiredEnvironment_DoesNotTreatUnknownAsNonProduction(string first, string second, string? expected)
    {
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(default)).Records[0];
        source = source with
        {
            Servers = new[] { first, second }.Select((value, index) => new InUseServer(index.ToString(),
            new Dictionary<string, InUseEvidence> { ["SI_ENVIRONMENT"] = new(value, "fixture") })).ToArray()
        };
        InUseRequiredFields.Environment(source).Should().Be(expected);
    }

    [Fact]
    public async Task WorkflowProgress_PreservesReview_WhileServerChangeRequiresReviewAndTrackingIsSeparate()
    {
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        InUseAnswer[] answers = record.Source.Servers.SelectMany(s => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, "No", "retained"))).ToArray();
        record = (await f.Service.SaveDraftAsync(_principal, _context, record.Id,
            new(record.Version, record.SourceVersion, answers, "retained notes"), _token)).Value!;
        InUseDraft draft = record.Draft!;
        InUseSource changed = record.Source with
        {
            Title = "Next workflow stage",
            Lifecycle = new("Open", "synthetic source"),
            WasasActivity = new("Completed", "synthetic authoritative activity"),
            CurrentStage = new("Next team pending", "synthetic source")
        };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([changed], true));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        InUseRecord next = (await f.Repository.GetAsync(record.Id, _token))!;
        next.SourceVersion.Should().BeGreaterThan(record.SourceVersion);
        next.SourceHash.Should().NotBe(record.SourceHash);
        next.Draft.Should().BeEquivalentTo(draft);
        next.ReviewCurrent.Should().BeTrue();
        next.Status.Should().Be("Draft");
        next.SourceChanges.Should().Contain(c => c.Field == "OR başlığı" && c.Before == record.Source.Title && c.After == changed.Title);
        (await f.Repository.QueryAsync(new(View: "tracking"), f.User.Id, _token)).Total.Should().Be(1);
        (await f.Repository.QueryAsync(new(View: "review"), f.User.Id, _token)).Items.Should().NotContain(r => r.Id == record.Id);
        InUseReport report = (await f.Service.ExportAsync(_principal, _context, next.Id, new(next.Version), _token)).Value!;
        report.SourceVersion.Should().Be(next.SourceVersion);
        InUseServer server = changed.Servers[0];
        var fields = server.Fields.ToDictionary(p => p.Key, p => p.Value);
        fields["STATUS"] = new("In Use", "synthetic inventory progression");
        changed = changed with { Servers = changed.Servers.Select(s => s.Id == server.Id ? s with { Fields = fields } : s).ToArray() };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([changed], true));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        next = (await f.Repository.GetAsync(record.Id, _token))!;
        next.ReviewCurrent.Should().BeTrue();
        next.Draft.Should().BeEquivalentTo(draft);
        InUseSourceChanges.Fields(next.Source.Servers[0], next.Draft).Should().Contain("STATUS");
        InUseSourceChanges.AffectsReview("STATUS").Should().BeFalse();
        fields = fields.ToDictionary(p => p.Key, p => p.Value);
        fields["SI_ENVIRONMENT"] = new("PROD", "synthetic changed source");
        changed = changed with { Servers = changed.Servers.Select(s => s.Id == server.Id ? s with { Fields = fields } : s).ToArray() };
        f.Source.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(new InUseBatch([changed], true));
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        next = (await f.Repository.GetAsync(record.Id, _token))!;
        next.Status.Should().Be("Stale");
        next.Draft.Should().BeEquivalentTo(draft);
        InUseSourceChanges.Fields(next.Source.Servers[0], next.Draft).Should().Contain("SI_ENVIRONMENT");
        (await f.Service.ExportAsync(_principal, _context, next.Id, new(next.Version), _token)).Error.Should().Be("InUseConflict");
    }

    [Theory]
    [InlineData("Synthetic Creator", "CONTOSO\\creator", "Synthetic Creator (CONTOSO\\creator)")]
    [InlineData(null, "CONTOSO\\creator", "CONTOSO\\creator")]
    [InlineData("S-1-5-21-1234", null, "Kullanıcı adı çözümlenemedi")]
    [InlineData("11111111-1111-1111-1111-111111111111", null, "Kullanıcı adı çözümlenemedi")]
    public async Task Workbook_FourServers_ResolvedActorsAndPrintLayout_KeepCorporateShape(string? name, string? account, string expected)
    {
        InUsePersonLabel.Format(name, account).Should().Be(expected);
        var f = new Fixture();
        InUseRecord record = await f.ImportAsync();
        InUseServer seed = record.Source.Servers[0];
        InUseServer[] servers = Enumerable.Range(1, 4).Select(i => seed with
        {
            Id = i.ToString(),
            Fields = new Dictionary<string, InUseEvidence>
            { ["HOSTNAME"] = new("synthetic-" + i, "fixture"), ["ENVANTER_ID"] = new("000" + i, "fixture") }
        }).ToArray();
        var reviewer = Guid.NewGuid();
        var creator = Guid.NewGuid();
        record = record with
        {
            Source = record.Source with { Servers = servers },
            Draft = new(record.SourceVersion,
            servers.SelectMany(s => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, s.Id == "1" ? "Yes" : "No", ""))).ToArray(),
            "", reviewer, DateTimeOffset.UtcNow)
            { ReviewedByLabel = "Synthetic Reviewer", ReviewedByAccount = "CONTOSO\\reviewer" }
        };
        InUseReport report = InUseWorkbook.Create(record, creator, DateTimeOffset.UtcNow, name, account);
        report.Sheets.Select(s => s.Name).Should().Equal("NMS", "CheckList_THY", "CheckList_TEKNIK", "Sunucular");
        report.Sheets[0].Rows.Should().HaveCount(5).And.OnlyContain(r => r.Count == 22);
        report.Sheets[3].Rows.Should().HaveCount(29).And.OnlyContain(r => r.Count == 5);
        report.Sheets[3].Rows[14].Skip(1).Should().Equal("Evet", "Hayır", "Hayır", "Hayır");
        report.EvidenceSheets[0].Rows.Should().Contain(r => r[0] == "PreparedBy" && r[1] == creator.ToString("D"));
        report.EvidenceSheets[0].Rows.Should().Contain(r => r[0] == "ReviewedBy" && r[1] == reviewer.ToString("D"));
        report.EvidenceSheets[0].Rows.Should().Contain(r => r[0] == "Raporu hazırlayan" && r[1] == expected);
        using var zip = new ZipArchive(new MemoryStream(report.Content));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using Stream input = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(input);
        xml.Descendants(ns + "autoFilter").Single().Attribute("ref")!.Value.Should().Be("A1:V5");
        xml.Descendants(ns + "oddFooter").Single().Value.Should().Contain(expected).And.Contain("Synthetic Reviewer (CONTOSO\\reviewer)").And.NotContain(creator.ToString());
        xml.Descendants(ns + "pageSetup").Single().Attribute("orientation")!.Value.Should().Be("landscape");
        xml.Descendants(ns + "pane").Should().ContainSingle();
        using Stream properties = zip.GetEntry("docProps/core.xml")!.Open();
        XDocument.Load(properties).Root!.Value.Should().Contain(expected);
    }
}
