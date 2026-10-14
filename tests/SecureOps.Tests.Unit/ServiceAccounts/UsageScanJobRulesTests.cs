using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>Pure rules of the web usage scan job: input bounds, status mapping, retry selection, estimate and honest texts.</summary>
public sealed class UsageScanJobRulesTests
{
    [Fact]
    public void ParseServers_TrimsDropsBlanksAndRepeats_KeepsOrder_AndNamesTheFirstBadLine()
    {
        UsageScanJobRules.ParseServers(["  syn-app-01 ", "", "SYN-APP-01", "syn-app-02.contoso.local", "   "]).Should()
            .BeEquivalentTo((new[] { "syn-app-01", "syn-app-02.contoso.local" }, (string?)null, (int?)null));
        UsageScanJobRules.ParseServers(["syn-app-01", "bad name;rm"]).Should().BeEquivalentTo((Array.Empty<string>(), "serverName", 2));
        UsageScanJobRules.ParseServers(["-leading"]).Error.Should().Be("serverName");
        UsageScanJobRules.ParseServers(["", " "]).Error.Should().Be("serversEmpty");
        UsageScanJobRules.ParseServers(null).Error.Should().Be("serversEmpty");
        UsageScanJobRules.ParseServers(Enumerable.Range(1, 501).Select(i => $"syn-{i}")).Error.Should().Be("serversTooMany");
        UsageScanJobRules.ParseServers(Enumerable.Range(1, 500).Select(i => $"syn-{i}")).Servers.Should().HaveCount(500);
    }

    [Theory]
    [InlineData("svc_app", "SYN", "SYN\\svc_app")]
    [InlineData("svc_app", null, "svc_app")]
    [InlineData("svc_app", "corp.contoso.local", "svc_app")]
    [InlineData("SYN\\svc_app", null, "SYN\\svc_app")]
    [InlineData("gmsa-app$", "SYN", "SYN\\gmsa-app$")]
    [InlineData("bad name", "SYN", null)]
    [InlineData("svc'; whoami", null, null)]
    public void SearchName_UsesANetBiosDomainOrTheBareName_AndRefusesUnsafeNames(string name, string? domain, string? expected) =>
        UsageScanJobRules.SearchName(name, domain).Should().Be(expected);

    [Fact]
    public void Patterns_AreTheUploadContractPatterns()
    {
        // Every name the job accepts must also pass the upload parser, or the evidence would be refused at the end.
        string[] servers = ["a", "syn-app-01", "syn.app.01.contoso.local", new string('a', 253)];
        string[] accounts = ["svc", "SYN\\svc_app", "gmsa-x$", "ABCDEFGHIJKLMNO\\x"];
        servers.Should().OnlyContain(s => UsageScanJobRules.IsServerName(s));
        accounts.Should().OnlyContain(a => UsageScanJobRules.IsAccountName(a));
        foreach (string server in servers)
        {
            byte[] bundle = UsageScanJobBundle.Build(accounts, null, [server], new Dictionary<string, byte[]>(),
                new Dictionary<string, UsageCollectionStatus> { [server] = UsageCollectionStatus.Unreachable }, DateTimeOffset.UtcNow);
            UsageScanParser.Parse(bundle, int.MaxValue, DateTimeOffset.UtcNow).Servers.Should().ContainSingle();
        }

        UsageScanJobRules.IsServerName("syn_app").Should().BeFalse();
        UsageScanJobRules.IsAccountName("ABCDEFGHIJKLMNOP\\x").Should().BeFalse("the NetBIOS part is at most 15 characters");
    }

    [Theory]
    [InlineData(UsageCollectionStatus.Unreachable, ScanServerResult.Unreachable)]
    [InlineData(UsageCollectionStatus.NoEndpoint, ScanServerResult.NoResult)]
    [InlineData(UsageCollectionStatus.Forbidden, ScanServerResult.NoResult)]
    [InlineData(UsageCollectionStatus.Timeout, ScanServerResult.NoResult)]
    [InlineData(UsageCollectionStatus.Malformed, ScanServerResult.NoResult)]
    [InlineData(UsageCollectionStatus.NotRun, ScanServerResult.NoResult)]
    public void EvidenceReason_KeepsEveryMissingAnswerAsNoInformation(UsageCollectionStatus status, ScanServerResult expected) =>
        UsageScanJobRules.EvidenceReason(status).Should().Be(expected);

    [Theory]
    [InlineData(UsageCollectionStatus.Ok, ScanServerResult.Success, false)]
    [InlineData(UsageCollectionStatus.Ok, ScanServerResult.Partial, true)]
    [InlineData(UsageCollectionStatus.Ok, ScanServerResult.Failed, true)]
    [InlineData(UsageCollectionStatus.Unreachable, null, true)]
    [InlineData(UsageCollectionStatus.NotRun, null, true)]
    [InlineData(UsageCollectionStatus.Malformed, null, true)]
    public void NeedsRetry_RepeatsOnlyWhatWasNotReadCompletely(UsageCollectionStatus status, ScanServerResult? result, bool expected) =>
        UsageScanJobRules.NeedsRetry(status, result).Should().Be(expected);

    [Fact]
    public void Tile_SaysNotFoundOnlyAfterACompleteRead()
    {
        UsageScanJobRules.Tile(UsageScanJobServerState.Pending, null, null, 0).Should().Be(UsageScanTileKind.Pending);
        UsageScanJobRules.Tile(UsageScanJobServerState.Connecting, null, null, 0).Should().Be(UsageScanTileKind.Connecting);
        UsageScanJobRules.Tile(UsageScanJobServerState.Done, UsageCollectionStatus.Ok, ScanServerResult.Success, 0).Should().Be(UsageScanTileKind.NotFound);
        UsageScanJobRules.Tile(UsageScanJobServerState.Done, UsageCollectionStatus.Ok, ScanServerResult.Success, 2).Should().Be(UsageScanTileKind.Found);
        UsageScanJobRules.Tile(UsageScanJobServerState.Done, UsageCollectionStatus.Ok, ScanServerResult.Partial, 0).Should().Be(UsageScanTileKind.Uncertain);
        UsageScanJobRules.Tile(UsageScanJobServerState.Done, UsageCollectionStatus.Ok, ScanServerResult.Failed, 0).Should().Be(UsageScanTileKind.NotCovered);
        foreach (UsageCollectionStatus status in Enum.GetValues<UsageCollectionStatus>().Where(s => s != UsageCollectionStatus.Ok))
        {
            UsageScanJobRules.Tile(UsageScanJobServerState.Done, status, null, 0).Should().Be(UsageScanTileKind.NotCovered, status.ToString());
        }
    }

    [Fact]
    public void Texts_AreTurkish_Distinct_AndNeverSayNotUsed()
    {
        string[] labels = [.. Enum.GetValues<UsageScanTileKind>().SelectMany(k => Enum.GetValues<UsageCollectionStatus>().Select(s => UsageScanJobRules.TileLabel(k, s, 3))),
            .. Enum.GetValues<UsageCollectionStatus>().Select(UsageScanJobRules.StatusLabel),
            .. Enum.GetValues<UsageScanJobState>().Select(UsageScanJobRules.StateLabel),
            .. new[] { "collectorUnavailable", "accessRevoked", "workerLost", "collectorFailed", "evidenceRefused", "dispatchFailed", "notStarted", "x" }
                .Select(UsageScanJobRules.FailureLabel)];
        labels.Should().NotContain(l => l.Contains("kullanılmıyor", StringComparison.OrdinalIgnoreCase));
        Enum.GetValues<UsageCollectionStatus>().Select(UsageScanJobRules.StatusLabel).Should().OnlyHaveUniqueItems();
        UsageScanJobRules.TileLabel(UsageScanTileKind.NotFound, UsageCollectionStatus.Ok, 0).Should().Be("Aranan kaynaklarda bulunamadı");
        UsageScanJobRules.TileLabel(UsageScanTileKind.NotCovered, UsageCollectionStatus.Unreachable, 0).Should().Be("Erişilemedi — bilgi yok");
        UsageScanJobRules.TileLabel(UsageScanTileKind.Found, UsageCollectionStatus.Ok, 1).Should().Be("1 kullanım bulundu");
    }

    [Fact]
    public void Summary_NamesFindingsAndGaps_AndOffersARetryOnlyWhenSomethingIsMissing()
    {
        UsageScanJobRules.Summary(3, 7, 4, 0, 2).Should().Be(
            "3 sunucuda 7 kullanım bulundu; 4 sunucuda aranan kaynaklarda bulunamadı (kullanılmıyor demek değildir); "
            + "2 sunucuya erişilemedi veya okunamadı (bilgi yok) → yalnız bunları yeniden deneyebilirsiniz.");
        UsageScanJobRules.Summary(0, 0, 5, 0, 0).Should().Be(
            "Hiçbir sunucuda kullanım bulunmadı; 5 sunucuda aranan kaynaklarda bulunamadı (kullanılmıyor demek değildir).");
        UsageScanJobRules.Summary(1, 1, 0, 1, 0).Should().EndWith("yeniden deneyebilirsiniz.");
    }

    [Fact]
    public void EstimateRemaining_WaitsForThreeServers_AndUsesTheObservedRate()
    {
        DateTimeOffset start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        UsageScanJobRules.EstimateRemaining(start, start.AddSeconds(10), 2, 10).Should().BeNull();
        UsageScanJobRules.EstimateRemaining(start, start.AddSeconds(30), 3, 10).Should().Be(TimeSpan.FromSeconds(70));
        UsageScanJobRules.EstimateRemaining(start, start.AddSeconds(30), 10, 10).Should().BeNull();
        UsageScanJobRules.EstimateRemaining(null, start, 5, 10).Should().BeNull();
    }

    [Fact]
    public void NextStep_GuidesEveryState()
    {
        foreach (UsageScanJobState state in Enum.GetValues<UsageScanJobState>())
        {
            UsageScanJobRules.NextStep(state, 0, 0, false).Should().StartWith("Sıradaki adım:");
        }

        UsageScanJobRules.NextStep(UsageScanJobState.Completed, 2, 1, true).Should().Contain("kullanım olarak kaydedin").And.Contain("yeniden deneyebilirsiniz");
        UsageScanJobRules.NextStep(UsageScanJobState.Completed, 0, 0, true).Should().Contain("kullanılmıyor demek değildir");
    }

    [Fact]
    public void RunStatement_FitsTheEvidenceColumn_AndAFakeSaysSo()
    {
        var id = Guid.NewGuid();
        string fake = UsageScanJobRules.RunStatement("Fake", id, cancelled: true);
        fake.Should().Contain("SAHTE").And.Contain(id.ToString("N"));
        fake.Length.Should().BeInRange(5, 400);
        UsageScanJobRules.RunStatement("Jea", id, cancelled: false).Should().Contain("JEA").And.Contain("salt okunur").And.NotContain("SAHTE");
    }
}
