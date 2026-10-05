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
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The usage-scan tab (ADR-0027) rendered with synthetic data: honest wording for every outcome, gMSA evidence that never
/// claims verification, and commands that only mirror the server-computed permissions (responsible, participant, viewer).
/// </summary>
public sealed class ServiceAccountUsageScanUiTests
{
    private static readonly Guid _account = Guid.Parse("5a1e0000-0000-4000-8000-000000000001");
    private static readonly Guid _request = Guid.Parse("5a1e0000-0000-4000-8000-000000000002");

    [Fact]
    public async Task Responsible_SeesHonestCoverage_GapsAndDecisionCommands()
    {
        string html = await RenderAsync(Detail(new AccountPermissions(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible), [Discovery(), Check()]));

        html.Should().Contain("Kullanım taraması").And.Contain("Tarama neyi göremez?").And.Contain("Sistem sunuculara bağlanmaz, tarama");
        html.Should().Contain("Tarama dosyası yükle ve bu hesaba bağla");
        html.Should().Contain("4 sunucudan 2 tanesi tam tarandı");
        html.Should().Contain("2 sunucu için sonuç eksik: SYN-APP03 (Kısmi tarandı), SYN-APP04 (Erişilemedi)").And.Contain("yeniden tarayın");
        html.Should().Contain("Karar ver").And.Contain("Karar ver: SYN-APP01 SynPool");
        html.Should().Contain("Kullanım kaydı oluşturuldu", "a decided item shows its decision instead of a command");
        html.Should().Contain("gMSA: eski hesap hâlâ var").And.Contain("bu tarama doğrulama değildir");
        html.Should().Contain("Beklenen gMSA ile çalışan bileşenler (1)").And.Contain("SYN\\gmsa_synapp$");
        Words(html).Should().NotContain("silinebilir").And.NotContain("doğrulandı");
        html.Should().NotContain("Kullanılmıyor").And.NotContain("kullanılmıyor.");
    }

    [Fact]
    public async Task Participant_UploadsOnlyThroughItsOwnRequest_AndCannotDecide()
    {
        AccountPermissions participant = new(false, false, false, false, false, true, ServiceAccountAccessBasis.Participant, [_request]);
        string html = await RenderAsync(Detail(participant, [Discovery()]));

        html.Should().Contain("Tarama dosyası yükle ve bu hesaba bağla");
        html.Should().NotContain("Karar ver").And.Contain("Karar bekliyor (hesaptan sorumlu ekip)");
    }

    [Fact]
    public async Task Viewer_SeesEvidenceButNoCommand()
    {
        string html = await RenderAsync(Detail(new AccountPermissions(false, false, false, false, false, false), [Discovery()]));

        html.Should().Contain("SYN-APP01").And.NotContain("Tarama dosyası yükle").And.NotContain("Karar ver");
    }

    [Fact]
    public async Task NoScanYet_AndSchemaNotInstalled_AreDistinctHonestStates()
    {
        AccountPermissions work = new(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible);

        string empty = await RenderAsync(Detail(work, []));
        empty.Should().Contain("Bu hesaba bağlı tarama yok").And.Contain("bu, hesabın kullanılmadığı anlamına gelmez");

        string missing = await RenderAsync(Detail(work, null));
        missing.Should().Contain("veritabanı güncellemesi 030 uygulanmamış").And.NotContain("Tarama dosyası yükle").And.NotContain("Bu hesaba bağlı tarama yok");
    }

    // Review 2026-10-05: with no match the component section said "not found in the scanned sources" even when no server
    // was fully scanned; it must say what was actually covered.
    [Fact]
    public async Task NoMatch_SaysOnlyWhatWasFullyScanned()
    {
        AccountPermissions work = new(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible);

        string nothingScanned = Words(await RenderAsync(Detail(work, [Empty(
            Server("SYN-APP01", "Unreachable", "Erişilemedi", 0, "NotCovered", "Bilgi yok: sunucu taranamadı"),
            Server("SYN-APP02", "Partial", "Kısmi tarandı", 0, "Uncertain", "Belirsiz: kısmi tarama, okunamayan kaynakta olabilir"))])));
        nothingScanned.Should().NotContain("Taranan kaynaklarda bu hesapla çalışan bileşen bulunmadı")
            .And.Contain("Hiçbir sunucu tam taranmadı").And.Contain("bilinmiyor");

        string mixed = Words(await RenderAsync(Detail(work, [Empty(
            Server("SYN-APP01", "Success", "Tam tarandı", 0, "NotFound", "Taranan kaynaklarda bulunmadı (kullanılmıyor demek değildir)"),
            Server("SYN-APP02", "Unreachable", "Erişilemedi", 0, "NotCovered", "Bilgi yok: sunucu taranamadı"))])));
        mixed.Should().Contain("Tam taranan 1 sunucuda bu hesapla çalışan bileşen bulunmadı").And.Contain("1 sunucu için sonuç eksik")
            .And.Contain("kullanılmadığını göstermez");

        string complete = Words(await RenderAsync(Detail(work, [Empty(
            Server("SYN-APP01", "Success", "Tam tarandı", 0, "NotFound", "Taranan kaynaklarda bulunmadı (kullanılmıyor demek değildir)"))])));
        complete.Should().Contain("Taranan kaynaklarda bu hesapla çalışan bileşen bulunmadı").And.Contain("kullanılmadığını göstermez");
    }

    [Theory]
    [InlineData("scanSecretField", "parola veya gizli değer")]
    [InlineData("scanSecretValue", "saklanmadı")]
    [InlineData("scanServers", "tam bir kez")]
    [InlineData("accountNotInScan", "bu hesabı aramamış")]
    [InlineData("accountAmbiguousInScan", "farklı domain")]
    [InlineData("runStatement", "hangi yetkiyle")]
    [InlineData("alreadyDecided", "karar zaten verilmiş")]
    [InlineData("scanTablesMissing", "030")]
    public void UploadRejections_AreExplainedInTurkish(string field, string expected)
    {
        UiProblem problem = UiProblemFactory.FromResponse(400, new ProblemDetailsPayload { Code = "ServiceAccountUsageScanRejected", Fields = [field] });

        problem.Title.Should().Be("Tarama dosyası kabul edilmedi");
        ServiceAccountProblems.FieldMessage(problem).Should().Contain(expected);
    }

    [Fact]
    public void DetailPage_WiresTheTab_AndTheGmsaPanelCallsTheScanEvidenceOnly()
    {
        string root = Root();
        File.ReadAllText(Path.Combine(root, "src", "SecureOps.Ui", "Pages", "ServiceAccounts", "ServiceAccountDetail.razor"))
            .Should().Contain("<SaUsageScanPanel").And.Contain("OnUpload=\"UploadScanAsync\"");
        File.ReadAllText(Path.Combine(root, "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaHandoverPanel.razor"))
            .Should().Contain("Tarama kanıttır; dönüşümü işlem doğrulamasında doğrulayıcı onaylar.");
        File.ReadAllText(Path.Combine(root, "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaUsageScanPanel.razor"))
            .Should().Contain("else if (Detail.Permissions.Work)").And.Contain("@if (!Detail.Permissions.Work)").And.NotContain("HttpMethod.Delete");
    }

    [Fact]
    public void AlreadyDecided_ReloadsAsAConflict_AndTheDecisionBoxClosesForADecidedItem()
    {
        // Windows 2026-10-05: a second decision is 409 ServiceAccountUsageScanAlreadyDecided; the page reloads (conflict) and the
        // stale decision box must not stay open for an item that now has a decision.
        UiProblem problem = UiProblemFactory.FromResponse(409, new ProblemDetailsPayload { Code = "ServiceAccountUsageScanAlreadyDecided", Fields = ["alreadyDecided"] });
        (problem.Kind, problem.Title).Should().Be((UiProblemKind.Conflict, "Karar zaten verilmiş"));
        ServiceAccountProblems.FieldMessage(problem).Should().Contain("karar zaten verilmiş");
        File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaUsageScanPanel.razor"))
            .Should().Contain("former.Any(i => i.Id == item.Id && i.Decision is null)");
    }

    [Fact]
    public void ScanPanel_GivesItsButtonsAVisibleKeyboardFocusRing()
    {
        // Measured in the live app: MudBlazor text buttons ("Karar ver") drew no focus indicator; keyboard focus must be visible.
        string css = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaUsageScanPanel.razor.css"));
        css.Should().MatchRegex(@"\.so-panel ::deep \.mud-button-root:focus-visible\s*\{[^}]*outline:\s*2px solid");
    }

    private static AccountDetail Detail(AccountPermissions permissions, IReadOnlyList<UsageScanView>? scans)
    {
        AccountSummaryView summary = new(_account, "svc_synapp", "SYN", null, "Provisional", null, null, null, null, "Active", null, null, null, 1, null, [],
            null, "AAAAAAAAAAA=");
        RequestView request = new(_request, _account, "svc_synapp", "GmsaHandover", "gMSA ile devir", "Open", null, new SaRef(Guid.NewGuid(), "SYN GMSA"),
            null, null, null, null, null, null, null, null, null, false, true, [], null, null, "AAAAAAAAAAA=");
        return new AccountDetail(summary, [], [request], [], [], [], [], [], [], [], [], [], permissions, [], null, scans, scans?.Count ?? 0);
    }

    private static UsageScanView Discovery()
    {
        UsageScanServerView[] servers =
        [
            Server("SYN-APP01", "Success", "Tam tarandı", 2, "Found", "Bu sunucuda çalışıyor"),
            Server("SYN-APP02", "Success", "Tam tarandı", 0, "NotFound", "Taranan kaynaklarda bulunmadı (kullanılmıyor demek değildir)"),
            Server("SYN-APP03", "Partial", "Kısmi tarandı", 0, "Uncertain", "Belirsiz: kısmi tarama, okunamayan kaynakta olabilir"),
            Server("SYN-APP04", "Unreachable", "Erişilemedi", 0, "NotCovered", "Bilgi yok: sunucu taranamadı")
        ];
        UsageScanItemView[] items =
        [
            new(Guid.NewGuid(), "SYN-APP01", "Former", "IisAppPool", "IIS uygulama havuzu", "SynPool", "SYN\\svc_synapp", null, null, "IisAppPool", null, null, null, null),
            new(Guid.NewGuid(), "SYN-APP01", "Former", "ScheduledTask", "Zamanlanmış görev", "\\SynNightly", "SYN\\svc_synapp", "Ready", "LogonType=Password",
                "ScheduledTask", "UsageRecorded", Guid.NewGuid(), null, DateTimeOffset.UnixEpoch)
        ];
        return new UsageScanView(Guid.NewGuid(), Guid.NewGuid(), "Discovery", "SYN\\svc_synapp", null, "scan.json", new string('a', 64), "Combined",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "Sentetik beyan", "Sentetik kullanıcı", DateTimeOffset.UnixEpoch, null,
            DateTimeOffset.UnixEpoch, new UsageScanCoverageView(4, 2, 1, 0, 1, 0, 1, 1, 1, 1), null, servers, items);
    }

    /// <summary>A discovery scan without any match, over the given servers.</summary>
    private static UsageScanView Empty(params UsageScanServerView[] servers) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Discovery", "SYN\\svc_synapp", null, "empty.json", new string('c', 64), "Combined", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "Sentetik beyan", "Sentetik kullanıcı", DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch,
            new UsageScanCoverageView(servers.Length, servers.Count(s => s.Result == "Success"), servers.Count(s => s.Result == "Partial"),
                servers.Count(s => s.Result == "Failed"), servers.Count(s => s.Result == "Unreachable"), servers.Count(s => s.Result == "NoResult"), 0,
                servers.Count(s => s.Outcome == "NotFound"), servers.Count(s => s.Outcome == "Uncertain"), servers.Count(s => s.Outcome == "NotCovered")),
            null, servers, []);

    private static UsageScanView Check()
    {
        UsageScanServerView[] servers = [Server("SYN-APP01", "Success", "Tam tarandı", 1, "Found", "Bu sunucuda çalışıyor") with
            { GmsaState = "StillFormer", GmsaStateLabel = "Eski hesap hâlâ yapılandırılı" }];
        UsageScanItemView[] items =
        [
            new(Guid.NewGuid(), "SYN-APP01", "Former", "ScheduledTask", "Zamanlanmış görev", "\\SynOther", "SYN\\svc_synapp", null, null, "ScheduledTask", null,
                null, null, null),
            new(Guid.NewGuid(), "SYN-APP01", "Expected", "WindowsService", "Windows servisi", "SynSvc", "SYN\\gmsa_synapp$", "Running", null, "WindowsService",
                null, null, null, null)
        ];
        return new UsageScanView(Guid.NewGuid(), Guid.NewGuid(), "GmsaCheck", "SYN\\svc_synapp", "SYN\\gmsa_synapp$", "gmsa.json", new string('b', 64), "Combined",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "Sentetik beyan", "Sentetik kullanıcı", DateTimeOffset.UnixEpoch, null,
            DateTimeOffset.UnixEpoch, new UsageScanCoverageView(1, 1, 0, 0, 0, 0, 1, 0, 0, 0),
            new UsageScanGmsaView("SYN\\gmsa_synapp$", "StillFormer", "Dönüşüm tamamlanmamış: eski hesap hâlâ en az bir bileşende", 1, 0, 0, 0), servers, items);
    }

    private static UsageScanServerView Server(string name, string result, string resultLabel, int matches, string outcome, string label) =>
        new(name, result, resultLabel, result is "Success" or "Partial" ? "Success" : null, result == "Partial" ? "Failed" : result == "Success" ? "Success" : null,
            result is "Success" or "Partial" ? "Success" : null, result is "Success" or "Partial" ? DateTimeOffset.UnixEpoch : null, null, matches, outcome, label,
            null, null);

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
            new Dictionary<string, object?> { ["Detail"] = detail, ["Busy"] = false, ["Revision"] = 0 }))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("http://localhost/", "http://localhost/service-accounts/" + _account);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private static string Words(string html) => Regex.Replace(html, "<[^>]+>", " ");

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
