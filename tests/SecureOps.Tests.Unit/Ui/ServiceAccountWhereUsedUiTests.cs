using System.Net;
using System.Net.Http.Json;
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
/// "Hangi sunucularda?" screen (synthetic data, static HTML renderer, canned API): every source keeps its own badge, a conflict is
/// shown as a conflict, an unreadable source is named instead of hidden, empty and error states are distinct, and no text ever says
/// an account is "kullanılmıyor" - only that it was "bulunamadı" in what was scanned.
/// </summary>
public sealed class ServiceAccountWhereUsedUiTests
{
    private static readonly Guid _account = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset _t1 = DateTimeOffset.Parse("2026-10-01T09:00:00+03:00");
    private static readonly DateTimeOffset _t2 = DateTimeOffset.Parse("2026-10-10T09:00:00+03:00");

    [Fact]
    public async Task Renders_OneRowPerServer_EverySourceAsItsOwnBadge_AndTheConflictExplicitly()
    {
        string html = await RenderAsync(Full(), active: true);
        string text = Text(html);

        text.Should().Contain("Hangi sunucularda?");
        text.Should().Contain("2 sunucu · 2 sunucuda görüldü · 1 çelişki · son tarama 10.10.2026");
        text.Should().Contain("syn-app01.corp.example").And.Contain("SYN-DB01");
        text.Should().Contain("Aynı sunucu olarak birleştirildi: SYN-APP01");
        text.Should().Contain("Tarama-Betik · bulundu · 10.10.2026");
        text.Should().Contain("Tarama-Web · tam taramada bulunamadı · 10.10.2026");
        text.Should().Contain("Excel · bulundu · 01.10.2026");
        text.Should().Contain("Elle · bulundu");
        text.Should().Contain("SPN · SPN tanımlı");
        text.Should().Contain("Kaynaklar çelişiyor").And.Contain("Çelişki: Elle kayıt sunucuda buldu");
        text.Should().Contain("En son görülme 10.10.2026 · son bakış 10.10.2026 · 3 kaynak");
        html.Should().Contain("sa-wu-item--conflict").And.Contain("sa-wu-item--found");
        text.Should().Contain("Kanıt ayrıntısı").And.Contain("Windows servisi: SynSvc");
        text.Should().Contain("Toplam 30 bileşen; ilk 1 listelendi.");
    }

    [Fact]
    public async Task NeverSaysAccountIsUnused_OnlyThatItWasNotFoundInWhatWasScanned()
    {
        string text = Text(await RenderAsync(Full(), active: true)) + Text(await RenderAsync(Empty(), active: true));

        text.Replace("kullanılmıyor demek değildir", string.Empty).Should().NotContainEquivalentOf("kullanılmıyor");
        text.Should().NotContain("Kullanılmıyor");
        text.Should().Contain("kullanılmadığı anlamına gelmez");
        text.Should().Contain("yalnız taranan kaynaklar için geçerlidir");
    }

    [Fact]
    public async Task UnreadableSource_IsNamedInAWarning_AndTheOtherSourcesStillShow()
    {
        WhereUsedView view = Full() with
        {
            Sources =
            [
                new("Scan", "Available", "1 tarama okundu."),
                new("Finding", "Empty", "Sunucu adı olan Excel bulgusu yok."),
                new("Manual", "Available", "1 sunucu adlı elle kayıt okundu."),
                new("Spn", "Unknown", "SPN bilinmiyor: dizine şu an ulaşılamadı. Diğer kaynaklar etkilenmez.")
            ]
        };

        string text = Text(await RenderAsync(view, active: true));

        text.Should().Contain("Bazı kaynaklar tam okunamadı; liste eksik olabilir: SPN: bilinmiyor");
        text.Should().Contain("SPN: Bilinmiyor").And.Contain("Excel bulgusu: Kayıt yok").And.Contain("Tarama: Okundu");
        text.Should().Contain("SPN bilinmiyor: dizine şu an ulaşılamadı");
        text.Should().Contain("syn-app01.corp.example", "the other sources are not hidden");
    }

    [Theory]
    [InlineData("Denied", "Yetki yok")]
    [InlineData("NotConfigured", "Yapılandırılmamış")]
    [InlineData("NotInstalled", "Kurulu değil")]
    [InlineData("Partial", "Kısmen okundu")]
    public void SourceStates_AreDistinct_AndIncompleteOnesAreFlagged(string state, string label)
    {
        WhereUsedPresentation.StateLabel(state).Should().Be(label);
        WhereUsedPresentation.IsIncomplete(new WhereUsedSourceStatus("Spn", state, "x")).Should().BeTrue();
        WhereUsedPresentation.StateLabel("Empty").Should().Be("Kayıt yok").And.NotBe(WhereUsedPresentation.StateLabel("Unknown"));
        WhereUsedPresentation.IsIncomplete(new WhereUsedSourceStatus("Spn", "Empty", "x")).Should().BeFalse("nothing recorded is an answer");
        WhereUsedPresentation.IsIncomplete(new WhereUsedSourceStatus("Spn", "Available", "x")).Should().BeFalse();
    }

    [Fact]
    public async Task EmptyAccount_SaysNoServerInfo_NotUnused_AndErrorsAreSeparate()
    {
        string empty = Text(await RenderAsync(Empty(), active: true));
        empty.Should().Contain("Bu hesap için sunucu bilgisi yok").And.Contain("hiçbir yerde kullanılmadığı anlamına gelmez");
        empty.Should().Contain("Tarama: Kayıt yok");

        foreach ((HttpStatusCode status, string code) in new[]
        {
            (HttpStatusCode.NotFound, "ServiceAccountNotFound"), (HttpStatusCode.Forbidden, "ServiceAccountAccessDenied"),
            (HttpStatusCode.ServiceUnavailable, "ServiceAccountPersistenceUnavailable")
        })
        {
            string failed = await RenderAsync(null, active: true, status, code);
            Text(failed).Should().NotContain("Bu hesap için sunucu bilgisi yok", $"{status} is an error, not an empty list");
            failed.Should().NotContain("sa-wu-list").And.NotContain("sa-wu-headline");
        }
    }

    [Fact]
    public async Task BeforeTheTabIsOpened_NothingIsRead_AndTheStateIsSaid()
    {
        CannedApi api = new(Full());
        string text = Text(await RenderAsync(api, active: false));

        text.Should().Contain("Bu sekme açıldığında okunur.");
        api.Requests.Should().BeEmpty("the directory is asked only when the tab opens");
        CannedApi opened = new(Full());
        await RenderAsync(opened, active: true);
        opened.Requests.Should().ContainSingle().Which.Should().Be($"/api/v1/service-accounts/accounts/{_account}/where-used?page=1&foundOnly=false");
    }

    [Fact]
    public async Task Filters_ArePlainLabelledControls_AndThePagerAppearsOnlyWhenNeeded()
    {
        string html = await RenderAsync(Full(), active: true);
        html.Should().Contain("<label for=\"sa-wu-source\"").And.Contain("<select id=\"sa-wu-source\"").And.Contain("type=\"checkbox\"");
        html.Should().NotContain("sa-pager", "two servers fit on one page");
        string paged = await RenderAsync(Full() with { Total = 120, PageSize = 50 }, active: true);
        paged.Should().Contain("sa-pager");
        Text(paged).Should().Contain("Sunucular: 1–50 / 120 sunucu", "the range comes from the server total");
    }

    [Fact]
    public void Presentation_StatusesAndEvidenceCarryTextAndIconBesidesTone()
    {
        string[] statuses = ["Conflict", "Found", "DefinedOnly", "NotFoundInScanned", "NoInformation"];
        statuses.Select(WhereUsedPresentation.StatusIcon).Distinct().Should().HaveCountGreaterThan(3);
        string[] outcomes = ["Found", "NotFound", "NoInformation", "Defined"];
        outcomes.Select(WhereUsedPresentation.EvidenceIcon).Should().OnlyHaveUniqueItems();
        outcomes.Select(WhereUsedLabels.Outcome).Should().OnlyHaveUniqueItems().And.OnlyContain(s => !s.Contains("kullanılmıyor", StringComparison.OrdinalIgnoreCase));
        WhereUsedPresentation.SourceFilters.Select(f => f.Value).Should().Equal(null, "Scan", "Finding", "Manual", "Spn");
        WhereUsedLabels.Source("Scan", "Jea").Should().Be("Tarama-Web");
        WhereUsedLabels.Source("Scan", "Combined").Should().Be("Tarama-Betik");
        WhereUsedLabels.Tool("Jea").Should().Be("Web taraması (JEA)");
        WhereUsedLabels.Tool("Combined").Should().Be("Betik çıktısı (yükleme)");
        WhereUsedPresentation.Dates(new WhereUsedServer("syn", [], "DefinedOnly", "x", false, false, null, null, 1, null, [])).Should()
            .Be("Hiçbir kaynakta görülmedi · 1 kaynak");
    }

    [Fact]
    public void ScanPanel_ShowsTheScanToolBadge_AndDetailPlacesThePanelInTheUsageTab()
    {
        string panel = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaUsageScanPanel.razor"));
        panel.Should().Contain("WhereUsedLabels.Tool(scan.Tool)");
        string page = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Pages", "ServiceAccounts", "ServiceAccountDetail.razor"));
        page.Should().Contain("<SaWhereUsedPanel @key=\"Id\" AccountId=\"Id\" Active=\"@(_tab == 1)\" />");
    }

    [Fact]
    public void Css_WrapsLongNames_KeepsTheFocusRing_AndUsesThemeTokensOnly()
    {
        string css = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaWhereUsedPanel.razor.css"));
        css.Should().Contain("overflow-wrap: anywhere");
        css.Should().MatchRegex(@"\.sa-wu-details summary:focus-visible[\s\S]*outline:\s*2px solid");
        css.Should().NotMatchRegex(@"#[0-9a-fA-F]{3,8}\b", "colours come from theme tokens so light and dark both work");
        css.Should().NotContain("rgb(").And.NotContain("overflow-x");
    }

    [Fact]
    public void Contract_HasNoReverseDirection()
    {
        string controller = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Api", "Controllers", "ServiceAccounts", "ServiceAccountsController.cs"));
        Regex.Matches(controller, @"\[HttpGet\(""[^""]*where-used[^""]*""\)\]").Select(m => m.Value).Should().Equal("[HttpGet(\"accounts/{id:guid}/where-used\")]");
        File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Shared", "Contracts", "ServiceAccounts", "ServiceAccountWhereUsedContracts.cs"))
            .Should().NotContain("ByServer").And.NotContain("AccountsOnServer");
    }

    private static WhereUsedView Full()
    {
        WhereUsedEvidence scanFound = new("Scan", "Found", _t2, "Combined", "Success", "Betik çıktısı (yükleme): hesap bu sunucuda 30 bileşende çalışıyor.",
            [new("WindowsService", "Windows servisi", "SynSvc")], 30);
        WhereUsedEvidence scanNone = new("Scan", "NotFound", _t2, "Jea", "Success", "Web taraması (JEA): tam taramada bulunmadı (kullanılmıyor demek değildir).", [], 0);
        WhereUsedEvidence finding = new("Finding", "Found", _t1, null, null, "Excel bulgusu: bu sunucuda eşleşme var.", [new("WindowsService", "WindowsService", "SynSvc")], 1);
        WhereUsedEvidence manual = new("Manual", "Found", _t1, null, null, "Elle kayıt: bir kişi bu sunucuda kullanım kaydetti.", [new("ScheduledTask", "Zamanlanmış görev", "SynTask")], 1);
        WhereUsedEvidence spn = new("Spn", "Defined", null, null, null, "Active Directory'de bu ana makine için SPN tanımlı.", [new("SpnClass", "SPN sınıfı", "HTTP")], 1);
        WhereUsedServer found = new("syn-app01.corp.example", ["SYN-APP01"], "Found", "Kullanımda görüldü", true, false, _t2, _t2, 3, null, [scanFound, finding, spn]);
        WhereUsedServer conflict = new("SYN-DB01", [], "Conflict", "Kaynaklar çelişiyor", true, true, _t2, _t2, 3,
            "Çelişki: Elle kayıt sunucuda buldu; Tarama-Web tam taramada bulamadı. Hangisinin güncel olduğuna tarihlerden bakın; bu, hesabın kullanılmadığını göstermez.",
            [scanNone, manual, spn]);
        return new WhereUsedView(_account, [conflict, found], 2, 1, 50,
            new WhereUsedSummary(2, 2, 1, 0, 0, 0, _t2, 2, 0, 0, 0, false),
            [
                new("Scan", "Available", "2 tarama okundu."),
                new("Finding", "Available", "1 sunucu adlı Excel bulgusu okundu."),
                new("Manual", "Available", "1 sunucu adlı elle kayıt okundu."),
                new("Spn", "Available", "2 ana makine için SPN tanımlı; tanım kullanım kanıtı değildir.")
            ]);
    }

    private static WhereUsedView Empty() => new(_account, [], 0, 1, 50, new WhereUsedSummary(0, 0, 0, 0, 0, 0, null, 0, 0, 0, 0, false),
        [
            new("Scan", "Empty", "Bu hesaba bağlı tarama yok."),
            new("Finding", "Empty", "Sunucu adı olan Excel bulgusu yok."),
            new("Manual", "Empty", "Sunucu adı olan elle kullanım kaydı yok."),
            new("Spn", "NotConfigured", "SPN bilgisi bilinmiyor: bu ortamda dizin yapılandırılmamış.")
        ]);

    private static string Text(string html) => string.Join(' ', Regex.Replace(html, "<[^>]+>", " ").Replace((char)160, (char)32)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Task<string> RenderAsync(WhereUsedView? view, bool active, HttpStatusCode failure = HttpStatusCode.OK, string code = "") =>
        RenderAsync(new CannedApi(view, failure, code), active);

    private static async Task<string> RenderAsync(CannedApi api, bool active)
    {
        HttpClient http = new(api) { BaseAddress = new Uri("http://localhost/") };
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton(new ServiceAccountApiClient(http, Substitute.For<IApiSessionContext>()));
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<SaWhereUsedPanel>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["AccountId"] = _account, ["Active"] = active }))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private sealed class CannedApi(WhereUsedView? view, HttpStatusCode failure = HttpStatusCode.OK, string code = "") : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(view is not null && failure == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(view, options: ApiResponseReader.JsonOptions) }
                : new HttpResponseMessage(failure == HttpStatusCode.OK ? HttpStatusCode.NotFound : failure)
                {
                    Content = JsonContent.Create(new { title = "x", status = (int)failure, code }, options: ApiResponseReader.JsonOptions)
                });
        }
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
