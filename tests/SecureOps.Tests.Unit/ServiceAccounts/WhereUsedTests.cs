using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.WhereUsed;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// "Hangi sunucularda?" rules (synthetic data): safe server-name joining, SPN host parsing, one row per server with every source kept
/// as its own evidence line, explicit conflicts, and no source that ever says "not used".
/// </summary>
public sealed class WhereUsedTests
{
    private static readonly DateTimeOffset _t1 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _t2 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("SYN-APP01", "syn-app01")]
    [InlineData("  Syn-App01. ", "syn-app01")]
    [InlineData("\\\\SYN-APP01", "syn-app01")]
    [InlineData("syn-app01.corp.example.", "syn-app01.corp.example")]
    public void Normalize_FoldsCaseDotsAndUncPrefix(string input, string key) =>
        WhereUsedServerNames.Normalize(input)!.Value.Key.Should().Be(key);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Normalize_EmptyIsNull(string? input) => WhereUsedServerNames.Normalize(input).Should().BeNull();

    [Fact]
    public void Normalize_IpAddressAndFreeText_AreNotHostNames()
    {
        WhereUsedServerNames.Normalize("192.0.2.3")!.Value.IsHostName.Should().BeFalse();
        WhereUsedServerNames.Normalize("SRV01 (eski)")!.Value.IsHostName.Should().BeFalse();
        WhereUsedServerNames.Normalize("a..b")!.Value.IsHostName.Should().BeFalse();
        WhereUsedServerNames.Normalize("syn-app01.corp.example")!.Value.IsHostName.Should().BeTrue();
    }

    [Fact]
    public void Merge_ShortNameJoinsExactlyOneFqdn()
    {
        IReadOnlyDictionary<string, string> map = WhereUsedServerNames.Merge(Names("SYN-APP01", "syn-app01.corp.example", "SYN-DB02"));

        map["syn-app01"].Should().Be("syn-app01.corp.example");
        map["syn-db02"].Should().Be("syn-db02");
    }

    [Fact]
    public void Merge_TwoFqdnsWithSameLabel_KeepEverythingSeparate()
    {
        IReadOnlyDictionary<string, string> map = WhereUsedServerNames.Merge(Names("syn-app01", "syn-app01.dom1.example", "syn-app01.dom2.example"));

        map["syn-app01"].Should().Be("syn-app01", "it cannot be told which FQDN it meant");
        map["syn-app01.dom1.example"].Should().Be("syn-app01.dom1.example");
        map["syn-app01.dom2.example"].Should().Be("syn-app01.dom2.example");
    }

    [Fact]
    public void Merge_NeverJoinsByPrefixOrIpOrFreeText()
    {
        IReadOnlyDictionary<string, string> map = WhereUsedServerNames.Merge(Names("syn-app1", "syn-app10.corp.example", "192.0.2.1", "192.0.2.1.corp.example"));

        map["syn-app1"].Should().Be("syn-app1");
        map["192.0.2.1"].Should().Be("192.0.2.1");
    }

    [Theory]
    [InlineData("MSSQLSvc/syn-db01.corp.example:1433", "syn-db01.corp.example", "MSSQLSvc")]
    [InlineData("HTTP/syn-web01", "syn-web01", "HTTP")]
    [InlineData("MSSQLSvc/syn-db01.corp.example:INST1", "syn-db01.corp.example", "MSSQLSvc")]
    [InlineData("MSSQLSvc/syn-db01:1433/service", "syn-db01", "MSSQLSvc")]
    [InlineData("  HTTP/SYN-WEB01  ", "SYN-WEB01", "HTTP")]
    public void SpnHost_ExtractsHostWithoutPortOrServiceName(string spn, string host, string serviceClass) =>
        ServicePrincipalNameHosts.TryParse(spn).Should().Be(new SpnHost(host, serviceClass));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nohost")]
    [InlineData("/host")]
    [InlineData("HTTP/")]
    [InlineData("HTTP/:80")]
    [InlineData("HTTP/[::1]:80")]
    public void SpnHost_Unreadable_IsNull(string? spn) => ServicePrincipalNameHosts.TryParse(spn).Should().BeNull();

    [Fact]
    public void SpnHosts_AreDeduplicatedAndUnreadableOnesCounted()
    {
        (IReadOnlyList<SpnHost> hosts, int unreadable) = ServicePrincipalNameHosts.Parse(
            ["HTTP/syn-web01", "http/SYN-WEB01:8080", "MSSQLSvc/syn-web01:1433", "broken"]);

        hosts.Select(h => $"{h.Class}/{h.Host}").Should().Equal("HTTP/syn-web01", "MSSQLSvc/syn-web01");
        unreadable.Should().Be(1);
    }

    [Fact]
    public void Build_KeepsEverySourceAsItsOwnEvidenceLine_AndJoinsShortAndFqdnNames()
    {
        var link = Guid.NewGuid();
        WhereUsedData data = Data(
            scans: [new WhereUsedScanRow(link, "Combined", "Discovery", _t1, _t1, _t1)],
            servers: [new WhereUsedScanServerRow(link, "SYN-APP01", "Success", _t1, 2)],
            items: [new WhereUsedScanItemRow(link, "SYN-APP01", "WindowsService", "SynSvc"), new WhereUsedScanItemRow(link, "SYN-APP01", "IisAppPool", "SynPool")],
            findings: [new WhereUsedFindingRow("syn-app01.corp.example", "WindowsService", "SynSvc", _t2, "Success", "Match")],
            usages: [new WhereUsedUsageRow("Syn-App01", "ScheduledTask", "SynTask", _t2)]);

        (IReadOnlyList<WhereUsedServer> servers, WhereUsedSummary summary, _) = WhereUsedBuilder.Build(data,
            new WhereUsedSpnInput(WhereUsedSourceStates.Available, "x", [new SpnHost("SYN-APP01.corp.example", "HTTP")]));

        servers.Should().ContainSingle();
        WhereUsedServer row = servers[0];
        row.Name.Should().Be("syn-app01.corp.example");
        row.AlsoSeenAs.Should().Equal("SYN-APP01");
        row.Status.Should().Be(WhereUsedStatuses.Found);
        row.SourceCount.Should().Be(4);
        row.Evidence.Select(e => e.Source).Should().Equal(WhereUsedSources.Scan, WhereUsedSources.Finding, WhereUsedSources.Manual, WhereUsedSources.Spn);
        row.Evidence[0].Should().Match<WhereUsedEvidence>(e => e.Tool == "Combined" && e.ComponentCount == 2 && e.Components.Count == 2);
        row.LastSeenAt.Should().Be(_t2);
        row.LastCheckedAt.Should().Be(_t2);
        summary.Should().Match<WhereUsedSummary>(s => s.Servers == 1 && s.Found == 1 && s.Conflicts == 0 && s.LastScanAt == _t1);
    }

    [Fact]
    public void Build_FoundByOneSourceAndNotFoundInCompleteScan_IsAnExplicitConflict()
    {
        var link = Guid.NewGuid();
        WhereUsedData data = Data(
            scans: [new WhereUsedScanRow(link, "Jea", "Discovery", _t2, _t2, _t2)],
            servers: [new WhereUsedScanServerRow(link, "syn-app01", "Success", _t2, 0)],
            usages: [new WhereUsedUsageRow("syn-app01", "WindowsService", "SynSvc", _t1)]);

        (IReadOnlyList<WhereUsedServer> servers, WhereUsedSummary summary, _) = WhereUsedBuilder.Build(data, NoSpn());

        WhereUsedServer row = servers.Single();
        row.Status.Should().Be(WhereUsedStatuses.Conflict);
        row.Conflict.Should().BeTrue();
        row.Found.Should().BeTrue();
        row.ConflictText.Should().Contain("Elle kayıt").And.Contain("Tarama-Web").And.Contain("kullanılmadığını göstermez");
        row.LastSeenAt.Should().Be(_t1, "the scan that did not find it is not 'seen'");
        row.LastCheckedAt.Should().Be(_t2);
        summary.Conflicts.Should().Be(1);
    }

    [Fact]
    public void Build_PartialUnreachableAndNoResult_AreNoInformationNeverNotFound()
    {
        var link = Guid.NewGuid();
        WhereUsedData data = Data(
            scans: [new WhereUsedScanRow(link, "Combined", "Discovery", _t1, _t1, _t1)],
            servers:
            [
                new WhereUsedScanServerRow(link, "syn-a", "Partial", _t1, 0),
                new WhereUsedScanServerRow(link, "syn-b", "Unreachable", null, 0),
                new WhereUsedScanServerRow(link, "syn-c", "NoResult", null, 0),
                new WhereUsedScanServerRow(link, "syn-d", "Failed", _t1, 0),
                new WhereUsedScanServerRow(link, "syn-e", "Success", _t1, 0)
            ]);

        IReadOnlyList<WhereUsedServer> servers = WhereUsedBuilder.Build(data, NoSpn()).Servers;

        servers.Where(s => s.Name != "syn-e").Should().OnlyContain(s => s.Status == WhereUsedStatuses.NoInformation && !s.Found);
        servers.Single(s => s.Name == "syn-e").Status.Should().Be(WhereUsedStatuses.NotFoundInScanned);
        servers.Single(s => s.Name == "syn-e").StatusLabel.Should().Contain("kullanılmıyor demek değildir");
        servers.Single(s => s.Name == "syn-b").LastCheckedAt.Should().Be(_t1, "the attempt time is when it was looked at");
        servers.Should().OnlyContain(s => !s.Found && s.LastSeenAt == null);
    }

    [Fact]
    public void Build_SpnAlone_IsDefinedOnlyAndNeverFound_AndDoesNotCreateAConflict()
    {
        var link = Guid.NewGuid();
        WhereUsedData data = Data(
            scans: [new WhereUsedScanRow(link, "Combined", "Discovery", _t1, _t1, _t1)],
            servers: [new WhereUsedScanServerRow(link, "syn-web01", "Success", _t1, 0)]);

        IReadOnlyList<WhereUsedServer> servers = WhereUsedBuilder.Build(data, new WhereUsedSpnInput(WhereUsedSourceStates.Available, "x",
            [new SpnHost("SYN-WEB01", "HTTP"), new SpnHost("syn-other", "HTTP")])).Servers;

        WhereUsedServer web = servers.Single(s => s.Name == "syn-web01");
        web.Status.Should().Be(WhereUsedStatuses.NotFoundInScanned, "an SPN is a definition, not a use, and not evidence against the scan");
        web.Conflict.Should().BeFalse();
        web.Evidence.Should().Contain(e => e.Source == WhereUsedSources.Spn && e.Outcome == WhereUsedOutcomes.Defined);
        WhereUsedServer other = servers.Single(s => s.Name == "syn-other");
        other.Status.Should().Be(WhereUsedStatuses.DefinedOnly);
        other.Found.Should().BeFalse();
        other.LastSeenAt.Should().BeNull();
    }

    [Fact]
    public void Build_FindingsOutcome_FollowsMatchAndCompleteness()
    {
        WhereUsedData data = Data(findings:
        [
            new WhereUsedFindingRow("syn-a", null, null, _t1, "Success", "Match"),
            new WhereUsedFindingRow("syn-b", null, null, _t1, "Success", "NoMatch"),
            new WhereUsedFindingRow("syn-c", null, null, _t1, "Partial", "NoMatch"),
            new WhereUsedFindingRow("syn-d", null, null, _t1, "Success", "Uncertain")
        ]);

        IReadOnlyList<WhereUsedServer> servers = WhereUsedBuilder.Build(data, NoSpn()).Servers;

        servers.Single(s => s.Name == "syn-a").Status.Should().Be(WhereUsedStatuses.Found);
        servers.Single(s => s.Name == "syn-b").Status.Should().Be(WhereUsedStatuses.NotFoundInScanned);
        servers.Single(s => s.Name == "syn-c").Status.Should().Be(WhereUsedStatuses.NoInformation);
        servers.Single(s => s.Name == "syn-d").Status.Should().Be(WhereUsedStatuses.NoInformation);
    }

    [Fact]
    public void Build_OrdersConflictsAndFoundFirst_ThenNewestSeen()
    {
        WhereUsedData data = Data(
            findings:
            [
                new WhereUsedFindingRow("syn-old", null, null, _t1, "Success", "Match"),
                new WhereUsedFindingRow("syn-new", null, null, _t2, "Success", "Match"),
                new WhereUsedFindingRow("syn-none", null, null, _t2, "Success", "NoMatch")
            ],
            usages: [new WhereUsedUsageRow("syn-none", "Other", null, _t1)]);

        IReadOnlyList<WhereUsedServer> servers = WhereUsedBuilder.Build(data, NoSpn()).Servers;

        servers.Select(s => s.Name).Should().Equal("syn-none", "syn-new", "syn-old");
        servers[0].Status.Should().Be(WhereUsedStatuses.Conflict);
    }

    [Fact]
    public void Build_SourceStatuses_AreHonestAboutWhatCouldNotBeRead()
    {
        (_, _, IReadOnlyList<WhereUsedSourceStatus> sources) = WhereUsedBuilder.Build(
            Data(scansInstalled: false, scansTotal: 0), WhereUsedSpnInput.Unknown(WhereUsedSourceStates.Unknown, "SPN bilinmiyor"));

        sources.Single(s => s.Source == WhereUsedSources.Scan).State.Should().Be(WhereUsedSourceStates.NotInstalled);
        sources.Single(s => s.Source == WhereUsedSources.Finding).State.Should().Be(WhereUsedSourceStates.Empty);
        sources.Single(s => s.Source == WhereUsedSources.Manual).State.Should().Be(WhereUsedSourceStates.Empty);
        sources.Single(s => s.Source == WhereUsedSources.Spn).State.Should().Be(WhereUsedSourceStates.Unknown);
    }

    [Fact]
    public void Build_OlderScansAndTruncatedComponents_ArePartialNotHidden()
    {
        var link = Guid.NewGuid();
        WhereUsedData data = Data(scansTotal: 25, itemsTruncated: true, scans: [new WhereUsedScanRow(link, "Combined", "Discovery", _t1, _t1, _t1)],
            usagesWithoutServer: 2, findingsWithoutServer: 3);

        (_, WhereUsedSummary summary, IReadOnlyList<WhereUsedSourceStatus> sources) = WhereUsedBuilder.Build(data, NoSpn());

        summary.Should().Match<WhereUsedSummary>(s => s.ScansIncluded == 1 && s.ScansOmitted == 24 && s.ComponentsTruncated && s.UsagesWithoutServer == 2 && s.FindingsWithoutServer == 3);
        sources.Single(s => s.Source == WhereUsedSources.Scan).State.Should().Be(WhereUsedSourceStates.Partial);
        sources.Single(s => s.Source == WhereUsedSources.Scan).Text.Should().Contain("24 eski tarama");
    }

    [Fact]
    public void Filter_BySourceAndFoundOnly()
    {
        WhereUsedData data = Data(
            findings: [new WhereUsedFindingRow("syn-a", null, null, _t1, "Success", "Match"), new WhereUsedFindingRow("syn-b", null, null, _t1, "Success", "NoMatch")],
            usages: [new WhereUsedUsageRow("syn-b", "Other", null, _t1)]);
        IReadOnlyList<WhereUsedServer> servers = WhereUsedBuilder.Build(data, new WhereUsedSpnInput(WhereUsedSourceStates.Available, "x", [new SpnHost("syn-c", "HTTP")])).Servers;

        WhereUsedBuilder.Filter(servers, WhereUsedSources.Spn, false).Select(s => s.Name).Should().Equal("syn-c");
        WhereUsedBuilder.Filter(servers, null, true).Select(s => s.Name).Should().BeEquivalentTo(["syn-a", "syn-b"]);
        WhereUsedBuilder.Filter(servers, WhereUsedSources.Finding, true).Should().HaveCount(2);
        WhereUsedBuilder.Filter(servers, WhereUsedSources.Spn, true).Should().BeEmpty();
        WhereUsedBuilder.IsSource("Dna").Should().BeFalse();
    }

    [Fact]
    public void Build_NoEvidenceAnywhere_ReturnsNoRows_NotAVerdict()
    {
        (IReadOnlyList<WhereUsedServer> servers, WhereUsedSummary summary, _) = WhereUsedBuilder.Build(Data(), NoSpn());

        servers.Should().BeEmpty();
        summary.Should().Match<WhereUsedSummary>(s => s.Servers == 0 && s.LastScanAt == null);
    }

    private static WhereUsedSpnInput NoSpn() => WhereUsedSpnInput.Unknown(WhereUsedSourceStates.NotConfigured, "dizin yok");

    private static IEnumerable<ServerNameKey> Names(params string[] names) => names.Select(n => WhereUsedServerNames.Normalize(n)!.Value);

    private static WhereUsedData Data(bool scansInstalled = true, int? scansTotal = null, WhereUsedScanRow[]? scans = null, WhereUsedScanServerRow[]? servers = null,
        WhereUsedScanItemRow[]? items = null, bool itemsTruncated = false, WhereUsedFindingRow[]? findings = null, int findingsWithoutServer = 0,
        WhereUsedUsageRow[]? usages = null, int usagesWithoutServer = 0) =>
        new(scansInstalled, scansTotal ?? scans?.Length ?? 0, scans ?? [], servers ?? [], items ?? [], itemsTruncated, findings ?? [], findingsWithoutServer, usages ?? [],
            usagesWithoutServer);
}
