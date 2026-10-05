using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Scan comparison (UI only, synthetic data): "not found" follows only a fully scanned server and never means "not used";
/// what the newer scan did not cover is unknown, not gone.
/// </summary>
public sealed class ServiceAccountScanDiffTests
{
    private static readonly Guid _account = Guid.Parse("5a1e0000-0000-4000-8000-000000000021");

    [Fact]
    public void FoundThenNotFound_IsNeverNotUsed_AndLostInformationIsNotGone()
    {
        UsageScanView older = Scan([Srv("SYN-APP01", "Found"), Srv("SYN-APP02", "Found"), Srv("SYN-APP03", "NotCovered")],
            [Item("SYN-APP01", "SynPool"), Item("SYN-APP02", "SynTask")]);
        UsageScanView newer = Scan([Srv("SYN-APP01", "NotFound"), Srv("SYN-APP02", "Uncertain"), Srv("SYN-APP03", "Found")],
            [Item("SYN-APP03", "SynSvc")]);

        SaScanDiff diff = ServiceAccountScanDiff.Compute(newer, older);

        diff.Servers.Single(s => s.Server == "SYN-APP01").Change.Should().Be(SaServerChange.NoLongerFound);
        diff.Servers.Single(s => s.Server == "SYN-APP01").Text.Should().Contain("kullanılmadığını göstermez");
        diff.Servers.Single(s => s.Server == "SYN-APP02").Change.Should().Be(SaServerChange.InformationLost);
        diff.Servers.Single(s => s.Server == "SYN-APP03").Change.Should().Be(SaServerChange.InformationArrived);
        diff.Components.Single(c => c.Server == "SYN-APP01").Change.Should().Be(SaComponentChange.NotFoundNow);
        diff.Components.Single(c => c.Server == "SYN-APP02").Change.Should().Be(SaComponentChange.UnknownNow, "the server was not fully scanned now");
        SaComponentDiff added = diff.Components.Single(c => c.Server == "SYN-APP03");
        added.Change.Should().Be(SaComponentChange.Added);
        added.Text.Should().Contain("yeni olmayabilir", "the server was not scanned before, so the component may have been there");
    }

    [Fact]
    public void NewAndMissingPlanEntries_AreLabelledByPlanNotByUse()
    {
        UsageScanView older = Scan([Srv("SYN-APP01", "Found"), Srv("SYN-APP02", "NotFound")], [Item("SYN-APP01", "SynPool")]);
        UsageScanView newer = Scan([Srv("SYN-APP01", "Found"), Srv("SYN-APP09", "NotFound")], [Item("SYN-APP01", "SynPool")]);

        SaScanDiff diff = ServiceAccountScanDiff.Compute(newer, older);

        diff.Servers.Single(s => s.Server == "SYN-APP02").Change.Should().Be(SaServerChange.NotPlannedNow);
        diff.Servers.Single(s => s.Server == "SYN-APP09").Change.Should().Be(SaServerChange.NewlyPlanned);
        diff.Servers.Single(s => s.Server == "SYN-APP01").Change.Should().Be(SaServerChange.Unchanged);
        diff.Components.Should().BeEmpty();
    }

    [Fact]
    public void SameResult_HasNoChanges_AndSaysItIsNotAUsageVerdict()
    {
        UsageScanView scan = Scan([Srv("SYN-APP01", "Found")], [Item("SYN-APP01", "SynPool")]);

        ServiceAccountScanDiff.Compute(scan, scan).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void ChangedIdentityOfTheSameComponent_IsReported()
    {
        UsageScanView older = Scan([Srv("SYN-APP01", "Found")], [Item("SYN-APP01", "SynPool", "SYN\\svc_old")]);
        UsageScanView newer = Scan([Srv("SYN-APP01", "Found")], [Item("SYN-APP01", "SynPool", "SYN\\svc_new")]);

        ServiceAccountScanDiff.Compute(newer, older).Components.Should().ContainSingle(c => c.Change == SaComponentChange.IdentityChanged);
    }

    [Fact]
    public void PreviousOf_PicksTheNextOlderScanOfTheSamePurpose()
    {
        UsageScanView newestCheck = Scan([], [], purpose: "GmsaCheck");
        UsageScanView discovery = Scan([], []);
        UsageScanView olderCheck = Scan([], [], purpose: "GmsaCheck");
        UsageScanView[] list = [newestCheck, discovery, olderCheck];

        ServiceAccountScanDiff.PreviousOf(list, 0).Should().BeSameAs(olderCheck);
        ServiceAccountScanDiff.PreviousOf(list, 1).Should().BeNull();
        ServiceAccountScanDiff.PreviousOf(list, 2).Should().BeNull();
    }

    [Fact]
    public void RescanFile_ListsOnlyServersWithoutAUsableResult_InTheCollectorFormat()
    {
        UsageScanView scan = Scan([Srv("SYN-APP02", "NotCovered"), Srv("SYN-APP01", "Found"), Srv("SYN-APP03", "Uncertain"), Srv("SYN-APP04", "NotFound")], []);

        ServiceAccountScanDiff.RescanServers(scan).Should().Equal("SYN-APP02", "SYN-APP03");
        string[] lines = ServiceAccountScanDiff.RescanFile(scan).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Where(l => !l.StartsWith('#')).Should().Equal("SYN-APP02", "SYN-APP03");
        lines.Should().OnlyContain(l => l.StartsWith('#') || Regex.IsMatch(l, "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$"));
    }

    [Fact]
    public async Task Panel_ShowsTheDiffForNewestScan_AndTheRescanDownloadWithoutOverclaiming()
    {
        UsageScanView older = Scan([Srv("SYN-APP01", "Found"), Srv("SYN-APP02", "NotCovered")], [Item("SYN-APP01", "SynPool")]);
        UsageScanView newer = Scan([Srv("SYN-APP01", "NotFound"), Srv("SYN-APP02", "NotCovered")], []);
        AccountSummaryView summary = new(_account, "svc_synapp", "SYN", null, "Confirmed", null, null, null, null, "Active", null, null, null, 0, null, [], null, "AAAAAAAAAAA=");
        AccountDetail detail = new(summary, [], [], [], [], [], [], [], [], [], [], [], new AccountPermissions(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible),
            [], null, [newer, older], 2);

        string html = await RenderAsync(detail);

        html.Should().Contain("Önceki taramaya göre fark (1 sunucu, 1 bileşen)").And.Contain("Bu taramada bulunmadı").And.Contain("Yeniden taranacak sunucu listesini indir");
        Regex.Replace(html, "<[^>]+>", " ").Should().NotContain("kullanılmıyor.").And.NotContain("silinebilir").And.NotContain("doğrulandı");
    }

    private static UsageScanServerView Srv(string name, string outcome) =>
        new(name, outcome is "Found" or "NotFound" ? "Success" : outcome == "Uncertain" ? "Partial" : "Unreachable",
            outcome is "Found" or "NotFound" ? "Tam tarandı" : outcome == "Uncertain" ? "Kısmi tarandı" : "Erişilemedi", null, null, null, null, null,
            outcome == "Found" ? 1 : 0, outcome, outcome switch
            {
                "Found" => "Bu sunucuda çalışıyor",
                "NotFound" => "Taranan kaynaklarda bulunmadı (kullanılmıyor demek değildir)",
                "Uncertain" => "Belirsiz",
                _ => "Bilgi yok"
            }, null, null);

    private static UsageScanItemView Item(string server, string component, string identity = "SYN\\svc_synapp") =>
        new(Guid.NewGuid(), server, "Former", "IisAppPool", "IIS uygulama havuzu", component, identity, null, null, "IisAppPool", null, null, null, null);

    private static UsageScanView Scan(UsageScanServerView[] servers, UsageScanItemView[] items, string purpose = "Discovery") =>
        new(Guid.NewGuid(), Guid.NewGuid(), purpose, "SYN\\svc_synapp", null, "scan.json", new string('a', 64), "Combined", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, "Sentetik beyan", "Sentetik kullanıcı", DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch,
            new UsageScanCoverageView(servers.Length, 0, 0, 0, 0, 0, 0, 0, 0, 0), null, servers, items);

    private static async Task<string> RenderAsync(AccountDetail detail)
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new SyntheticNavigation());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<SaUsageScanPanel>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                ["Detail"] = detail,
                ["Busy"] = false,
                ["Revision"] = 0,
                ["OnSaveFile"] = EventCallback.Factory.Create<ServiceAccountFile>(new object(), _ => { })
            }))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("http://localhost/", "http://localhost/service-accounts/" + _account);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }
}
