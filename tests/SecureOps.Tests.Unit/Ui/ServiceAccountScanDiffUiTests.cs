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
/// The scan comparison UI (G-33, synthetic data) only shows the server's answer: honest wording, paging of component changes, a
/// distinct "no older scan" state, and a re-scan server list in the collector's format. The client no longer computes a diff.
/// </summary>
public sealed class ServiceAccountScanDiffUiTests
{
    private static readonly DateTimeOffset _at = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task View_ShowsServerAnswer_WithCountsPagerAndNoUsageVerdict()
    {
        UsageScanDiffView diff = Diff(
            [new("SYN-APP02", "Found", "NotCovered", "InformationLost", "Güncel bilgi yok", "Önceki tarama: çalışıyor; bu taramada sonuç eksik.")],
            new UsageScanComponentDiffPage([new("SYN-APP01", "ScheduledTask", "Zamanlanmış görev", "\\SynTask30", "NotFoundNow", "Bu taramada bulunmadı",
                "Bu taramada, sunucu tam tarandığı hâlde bulunmadı. Bu, bileşenin kullanılmadığını göstermez.", "SYN\\svc", null)], 30, 2, 25,
                new UsageScanComponentDiffCounts(10, 15, 5, 0)));

        string html = await RenderAsync<SaScanDiffView>(new() { ["Diff"] = diff });

        string text = Regex.Replace(html, @"\s+", " ");
        text.Should().Contain("old.json").And.Contain("1 sunucuda sonuç aynı").And.Contain("10 yeni görüldü, 15 tam taramada bulunmadı, 5 durumu bilinmiyor");
        html.Should().Contain("Güncel bilgi yok").And.Contain("SYN-APP02").And.Contain("Bileşen farkları (30)");
        html.Should().Contain("Bileşen farkları: 26–30 / 30 (sayfa 2 / 2)").And.Contain("Önceki").And.Contain("Sonraki");
        html.Should().Contain("kullanılmadığını göstermez");
        Regex.Replace(html, "<[^>]+>", " ").Should().NotContain("silinebilir").And.NotContain("doğrulandı");
    }

    [Fact]
    public async Task View_SaysWhenThereIsNoOlderScan()
    {
        UsageScanDiffView none = new(Side("new.json"), null, [], 0, new UsageScanComponentDiffPage([], 0, 1, 25, new UsageScanComponentDiffCounts(0, 0, 0, 0)), null, null, null);

        (await RenderAsync<SaScanDiffView>(new() { ["Diff"] = none })).Should().Contain("aynı türde bir tarama yok");
    }

    [Fact]
    public async Task View_NoChange_IsNotAUsageVerdict()
    {
        string html = await RenderAsync<SaScanDiffView>(new() { ["Diff"] = Diff([], new UsageScanComponentDiffPage([], 0, 1, 25, new UsageScanComponentDiffCounts(0, 0, 0, 0))) });

        html.Should().Contain("farkı yok").And.Contain("kullanıldığını veya kullanılmadığını göstermez");
    }

    [Fact]
    public async Task Loader_AsksBeforeCalling_AndExplainsThatPagingDoesNotChangeTheAnswer()
    {
        int calls = 0;
        Func<Guid, int, Task<UsageScanDiffView?>> load = (_, _) => { calls++; return Task.FromResult<UsageScanDiffView?>(null); };

        string html = await RenderAsync<SaScanDiffLoader>(new() { ["LinkId"] = Guid.NewGuid(), ["Load"] = load });

        html.Should().Contain("Önceki taramayla karşılaştır").And.Contain("tüm eşleşmeler üzerinden");
        calls.Should().Be(0, "nothing is loaded until the person asks");
    }

    [Fact]
    public void RescanList_HoldsOnlyServersWithoutAUsableResult_InTheCollectorFormat()
    {
        UsageScanView scan = Scan(("SYN-APP02", "NotCovered"), ("SYN-APP01", "Found"), ("SYN-APP03", "Uncertain"), ("SYN-APP04", "NotFound"));

        ServiceAccountRescanList.Servers(scan).Should().Equal("SYN-APP02", "SYN-APP03");
        string[] lines = ServiceAccountRescanList.File(scan).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Where(l => !l.StartsWith('#')).Should().Equal("SYN-APP02", "SYN-APP03");
        lines.Should().OnlyContain(l => l.StartsWith('#') || Regex.IsMatch(l, "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$"));
    }

    [Fact]
    public void PanelAndPage_UseTheServerComparison_NotAClientDiff()
    {
        string components = Path.Combine(Root(), "src", "SecureOps.Ui");
        File.Exists(Path.Combine(components, "Services", "ServiceAccounts", "ServiceAccountScanDiff.cs")).Should().BeFalse("the client-side diff is gone (G-33)");
        File.ReadAllText(Path.Combine(components, "Shared", "Components", "ServiceAccounts", "SaUsageScanPanel.razor")).Should().Contain("<SaScanDiffLoader");
        File.ReadAllText(Path.Combine(components, "Pages", "ServiceAccounts", "ServiceAccountDetail.razor.cs")).Should().Contain("/diff?page=");
        File.ReadAllText(Path.Combine(components, "Shared", "Components", "ServiceAccounts", "SaScanDiffView.razor.css"))
            .Should().MatchRegex(@"\.sa-diff ::deep \.mud-button-root:focus-visible\s*\{[^}]*outline:\s*2px solid");
    }

    private static UsageScanDiffView Diff(IReadOnlyList<UsageScanServerDiffView> servers, UsageScanComponentDiffPage components) =>
        new(Side("new.json"), Side("old.json"), servers, 1, components, null, null, null);

    private static UsageScanDiffSide Side(string file) => new(Guid.NewGuid(), Guid.NewGuid(), "Discovery", file, _at, _at);

    private static UsageScanView Scan(params (string Name, string Outcome)[] servers) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Discovery", "SYN\\svc_synapp", null, "scan.json", new string('a', 64), "Combined", _at, _at, _at, "Sentetik beyan",
            "Sentetik kullanıcı", _at, null, _at, new UsageScanCoverageView(servers.Length, 0, 0, 0, 0, 0, 0, 0, 0, 0), null,
            [.. servers.Select(s => new UsageScanServerView(s.Name, "Success", "Tam tarandı", null, null, null, null, null, 0, s.Outcome, s.Outcome, null, null))], []);

    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
