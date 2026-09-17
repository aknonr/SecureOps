using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Fact]
    public async Task Policy_IsExplicitVersionBound_AndCannotReplaceSavedEvidenceSilently()
    {
        var options = new InUsePolicyOptions { Proposals = new() { ["COUNTRY"] = "Synthetic country" } };
        var policy = new InUsePolicy(Options.Create(options));
        var fixture = new Fixture(policy: policy);
        InUseRecord record = await fixture.ImportAsync();
        InUsePolicyProposal proposal = (await fixture.Service.GetAsync(_principal, _context, record.Id, _token)).Value!.PolicyProposal!;
        record = (await fixture.Service.SaveDraftAsync(_principal, _context, record.Id,
            new(record.Version, record.SourceVersion, [], "Partial draft"), _token)).Value!;
        record.Draft!.Policy.Should().BeNull();
        var accept = new SaveInUseDraftRequest(record.Version, record.SourceVersion, [], "") { ReviewedPolicyFingerprint = proposal.Fingerprint };
        record = (await fixture.Service.SaveDraftAsync(_principal, _context, record.Id, accept, _token)).Value!;
        record.Draft!.Policy.Should().BeEquivalentTo(proposal);
        (await fixture.Repository.GetAsync(record.Id, _token))!.PolicyProposal.Should().BeNull();
        options.Revision = "inuse-nms-v2";
        options.Proposals["COUNTRY"] = "Changed proposal";
        (await fixture.Service.SaveDraftAsync(_principal, _context, record.Id, accept with { ExpectedVersion = record.Version }, _token))
            .Error.Should().Be("InUsePolicyChanged");
        record = (await fixture.Service.SaveDraftAsync(_principal, _context, record.Id,
            new(record.Version, record.SourceVersion, [], "Retain reviewed snapshot"), _token)).Value!;
        record.Draft!.Policy!.Fingerprint.Should().Be(proposal.Fingerprint);
        record.PolicyProposal!.Fingerprint.Should().NotBe(proposal.Fingerprint);
        InUseReport report = (await fixture.Service.ExportAsync(_principal, _context, record.Id, new(record.Version), _token)).Value!;
        report.PreparedByLabel.Should().Contain(fixture.User.Id.ToString("N")[..8]);
        report.Sheets.Single(s => s.Name == "ReviewEvidence").Rows.Should().Contain(r => r.Any(c => c.Contains(proposal.Fingerprint)));
    }

    [Fact]
    public async Task Policy_MixedEnvironments_ExactIdsAndSourcePriority_RoundTripAsText()
    {
        var fixture = new Fixture();
        InUseRecord record = await fixture.ImportAsync();
        string[] environments = ["PROD", "DEV", "TEST", "unknown"];
        InUseServer[] servers = environments.Select((env, i) => new InUseServer((i + 1).ToString(), new Dictionary<string, InUseEvidence>
        {
            ["SI_ENVIRONMENT"] = new(env, "Synthetic source"),
            ["HOSTNAME"] = new("synthetic-" + i, "Synthetic source"),
            ["ENVANTER_ID"] = new("00012345678901234567890" + i, "Exact text"),
            ["ITMC_Service_ID"] = new("002915" + i, "Exact service relationship"),
            ["ITMC_Servis_Unsuru_ID"] = new("00112" + i, "Exact per-service aspect"),
            ["ITMC_Servis_Unsuru"] = new("[Genel] " + i, "Exact per-service aspect"),
            ["COUNTRY"] = new("Observed country", "Synthetic source")
        })).ToArray();
        record = record with { Source = record.Source with { Servers = servers }, Draft = new(record.SourceVersion, [], "", fixture.User.Id, DateTimeOffset.UtcNow) };
        var policy = new InUsePolicy(Options.Create(new InUsePolicyOptions { Proposals = new() { ["COUNTRY"] = "Proposed country" } }));
        InUsePolicyProposal proposal = policy.Propose(record);
        proposal.Fields.Where(f => f.Field == "check:MemoryAlarm").Select(f => f.Value).Should().Equal("Evet", "Hayır", "Hayır", null);
        proposal.Fields.Where(f => f.Field == "check:UpDownAlarm").Should().OnlyContain(f => f.Value == "Evet" && f.Origin == "MonitoringProposal");
        proposal.Fields.Should().NotContain(f => f.Field == "check:Verified" || f.Field == "UY_Owner Mail Address" || f.Field == "OS RELEASE");
        record = record with { Draft = record.Draft! with { Policy = proposal } };
        InUseReport report = InUseWorkbook.Create(record, fixture.User.Id, DateTimeOffset.UtcNow);
        InUseSheet nms = report.Sheets.Single(s => s.Name == "NMS");
        for (int i = 0; i < servers.Length; i++)
        {
            nms.Rows[i + 1][2].Should().Be(servers[i].Fields["ENVANTER_ID"].Value);
            nms.Rows[i + 1][3].Should().Be(servers[i].Fields["ITMC_Service_ID"].Value);
            nms.Rows[i + 1][5].Should().Be(servers[i].Fields["ITMC_Servis_Unsuru_ID"].Value);
            nms.Rows[i + 1][10].Should().Be("Observed country");
            nms.Rows[i + 1][0].Should().Be("Bilinmiyor / doğrulanmadı");
        }
        using ZipArchive zip = new(new MemoryStream(report.Content));
        using Stream stream = zip.GetEntry("xl/worksheets/sheet4.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (InUseServer server in servers)
        {
            XElement cell = xml.Descendants(ns + "c").Single(c => c.Value == server.Fields["ENVANTER_ID"].Value);
            cell.Attribute("t")!.Value.Should().Be("inlineStr");
        }
        xml.Descendants(ns + "f").Should().BeEmpty();
    }
}
