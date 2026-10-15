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
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Pages.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The web usage scan screens (synthetic data, static HTML renderer, canned API): tiles carry icon and text besides colour, the progress
/// bar is a real progressbar, the live region is polite and speaks only at milestones, the end summary separates "bulunamadı" from
/// "bilgi yok" and never says "kullanılmıyor", a closed feature says so honestly, and buttons follow the server's permissions.
/// </summary>
public sealed class ServiceAccountUsageScanJobUiTests
{
    private static readonly Guid _jobId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _account = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset _at = DateTimeOffset.Parse("2026-10-13T09:00:00+03:00");

    static ServiceAccountUsageScanJobUiTests() => UsageScanJobPresentation.PollInterval = TimeSpan.FromHours(1);

    [Fact]
    public void Presentation_EveryTileHasItsOwnIconAndClass()
    {
        string[] tiles = ["Pending", "Connecting", "Found", "NotFound", "Uncertain", "NotCovered"];
        tiles.Select(UsageScanJobPresentation.TileIcon).Should().OnlyHaveUniqueItems();
        tiles.Select(UsageScanJobPresentation.TileClass).Should().OnlyHaveUniqueItems().And.OnlyContain(c => c.StartsWith("sa-wscan-tile--", StringComparison.Ordinal));
        string css = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Pages", "ServiceAccounts", "ServiceAccountUsageScanJob.razor.css"));
        foreach (string tile in tiles)
        {
            css.Should().Contain("." + UsageScanJobPresentation.TileClass(tile) + " ", tile);
        }
    }

    [Fact]
    public void Presentation_AnnouncesMilestonesOnly()
    {
        UsageScanJobView running = View("Running", [Server(1, "syn-app-01", "Done", "Ok", "Found", "1 kullanım bulundu", 1)]);
        UsageScanJobPresentation.Announcement(null, running, 0).Should().Be("Tarama çalışıyor: 1 / 1 sunucu bitti.");
        UsageScanJobPresentation.Announcement(running, running, 0).Should().BeNull("an unchanged poll says nothing");
        UsageScanJobPresentation.Announcement(running, running, 2).Should().Be("2 yeni kullanım bulundu.");
        UsageScanJobView done = running with { State = "Completed", StateLabel = "Tamamlandı", Summary = "1 sunucuda 1 kullanım bulundu." };
        UsageScanJobPresentation.Announcement(running, done, 0).Should().Be("Tarama tamamlandı. 1 sunucuda 1 kullanım bulundu.");
    }

    [Fact]
    public void Presentation_ProgressAndRemainingTime()
    {
        UsageScanJobPresentation.Remaining(45).Should().Be("yaklaşık 45 sn kaldı (tahmin)");
        UsageScanJobPresentation.Remaining(130).Should().Be("yaklaşık 3 dk kaldı (tahmin)");
        UsageScanJobServerView[] servers =
            [Server(1, "a", "Done", "Ok", "NotFound", "Aranan kaynaklarda bulunamadı", 0), Server(2, "b", "Connecting", null, "Connecting", "Bağlanıyor", 0)];
        UsageScanJobView running = View("Running", servers) with { EstimatedSecondsRemaining = 90 };
        UsageScanJobPresentation.ProgressText(running).Should().Be("1 / 2 sunucu bitti · 1 sunucu okunuyor · yaklaşık 2 dk kaldı (tahmin)");
        UsageScanJobPresentation.ProgressText(running with { EstimatedSecondsRemaining = null }).Should().EndWith("kalan süre en az 3 sunucu bitince tahmin edilir");
        UsageScanJobPresentation.Percent(running.Counts).Should().Be(50);
    }

    [Fact]
    public async Task LivePage_ShowsTilesProgressAndFoundItems_AndOffersCancelOnlyWhenAllowed()
    {
        UsageScanJobView running = View("Running",
        [
            Server(1, "syn-app-01", "Done", "Ok", "Found", "2 kullanım bulundu", 2),
            Server(2, "syn-app-02", "Done", "Ok", "NotFound", "Aranan kaynaklarda bulunamadı", 0),
            Server(3, "syn-unreach-01", "Done", "Unreachable", "NotCovered", "Erişilemedi — bilgi yok", 0),
            Server(4, "syn-app-04", "Connecting", null, "Connecting", "Bağlanıyor", 0),
            Server(5, "syn-app-05", "Pending", null, "Pending", "Bekliyor", 0)
        ]) with
        {
            IsFake = true,
            NewItems = [Item(1, "syn-app-01", "SynSvc01"), Item(1, "syn-app-01", "SynTask02")],
            LastSeq = 1,
            Permissions = new UsageScanJobPermissions(true, false)
        };

        string html = await RenderPageAsync(running);

        html.Should().Contain("role=\"progressbar\"").And.Contain("aria-valuenow=\"3\"").And.Contain("aria-valuemax=\"5\"")
            .And.Contain("aria-valuetext=\"3 / 5 sunucu bitti\"");
        Regex.Count(html, "aria-live=\"polite\"").Should().Be(1, "one polite live region for the whole page");
        html.Should().Contain("Tarama çalışıyor: 3 / 5 sunucu bitti.");
        foreach (string text in new[] { "2 kullanım bulundu", "Aranan kaynaklarda bulunamadı", "Erişilemedi — bilgi yok", "Bağlanıyor", "Bekliyor" })
        {
            html.Should().Contain(text);
        }

        Regex.Count(html, "class=\"sa-wscan-tile sa-wscan-tile--").Should().Be(5);
        html.Should().Contain("SynSvc01").And.Contain("SynTask02").And.Contain("Bulunan kullanımlar (2)");
        html.Should().Contain("SAHTE toplayıcı").And.Contain("Taramayı iptal et").And.NotContain("yeniden dene");
        Text(html).Should().NotMatchRegex("(?i)kullanılmıyor(?! demek değildir)");

        string denied = await RenderPageAsync(running with { Permissions = new UsageScanJobPermissions(false, false) });
        denied.Should().NotContain("Taramayı iptal et");
    }

    [Fact]
    public async Task LivePage_FinishedScan_SummarisesHonestly_LinksAccounts_AndOffersRetry()
    {
        UsageScanJobView done = View("Completed",
        [
            Server(1, "syn-app-01", "Done", "Ok", "Found", "1 kullanım bulundu", 1),
            Server(2, "syn-noep-01", "Done", "NoEndpoint", "NotCovered", "Tarama uç noktası yok — bilgi yok", 0)
        ]) with
        {
            StateLabel = "Tamamlandı",
            Summary = "1 sunucuda 1 kullanım bulundu; 1 sunucuya erişilemedi veya okunamadı (bilgi yok) → yalnız bunları yeniden deneyebilirsiniz.",
            NextStep = "Sıradaki adım: hesabın Kullanım taraması bölümünde her bulguyu kullanım olarak kaydedin veya gerekçeyle ayırın.",
            Accounts = [new UsageScanJobAccountView(_account, "SYN\\svc_syn", "Attached", "Sonuç bu hesaba bağlandı; hesabın Kullanım taraması bölümünde.")],
            Counts = new UsageScanJobCounts(2, 0, 0, 2, 1, 0, 0, 1, 1, 1),
            Permissions = new UsageScanJobPermissions(false, true)
        };

        string html = await RenderPageAsync(done);

        html.Should().Contain("Özet").And.Contain("yalnız bunları yeniden deneyebilirsiniz").And.Contain("Bilgi alınamayan 1 sunucuyu yeniden dene");
        html.Should().Contain($"service-accounts/{_account}").And.Contain("Sonuç bu hesaba bağlandı");
        html.Should().Contain("Sıradaki adım:").And.Contain("COM+ kimlikleri");
        html.Should().NotContain("Taramayı iptal et");
    }

    [Fact]
    public async Task LivePage_MissingOrOutOfScope_ShowsAnEmptyState()
    {
        string html = await RenderPageAsync(null);

        html.Should().Contain("Tarama gösterilemiyor").And.NotContain("role=\"progressbar\"");
    }

    [Fact]
    public async Task StartPanel_WhenClosed_SaysWhyAndPointsToTheScript_WithoutAStartButton()
    {
        UsageScanJobAvailabilityView closed = new(false, "collectorNotConfigured",
            "Web taraması bu ortamda kapalı: sunucu tarafı toplayıcı (Worker'daki JEA çalıştırıcısı) henüz kurulu ve yapılandırılmış değil.", "None", false, 20, 500,
            true);

        string html = await RenderPanelAsync(closed, []);

        html.Should().Contain("Kapalı").And.Contain("henüz kurulu ve yapılandırılmış değil").And.Contain("Tarama sonucu yükle");
        html.Should().NotContain("Taramayı başlat");
    }

    [Fact]
    public async Task StartPanel_WhenOpen_ShowsTheFormTheFakeWarningAndTheAccountsScans()
    {
        UsageScanJobAvailabilityView open = new(true, null, string.Empty, "Fake", true, 20, 500, true);
        UsageScanJobSummaryView[] jobs = [new(_jobId, "Running", "Çalışıyor", true, _at, null, 40, 12, 3, 1, true, true)];

        string html = await RenderPanelAsync(open, jobs);

        html.Should().Contain("Taramayı başlat").And.Contain("aria-disabled=\"true\"").And.Contain("En az bir sunucu adı yazın.");
        html.Should().Contain("SAHTE toplayıcı").And.Contain("Planlanan sunucular");
        html.Should().Contain($"service-accounts/usage-scan-jobs/{_jobId}").And.Contain("12 / 40 sunucu · 3 kullanım bulundu · 1 sunucuda bilgi yok");
        html.Should().Contain("Çalışan taramayı aç");
        Text(html).Should().NotMatchRegex("(?i)kullanılmıyor(?! demek değildir)");
    }

    [Fact]
    public async Task StartPanel_WithoutWork_ExplainsTheRuleInsteadOfAForm()
    {
        string html = await RenderPanelAsync(new(true, null, string.Empty, "Jea", false, 20, 500, false), [], canWork: false);

        html.Should().Contain("Çalışma yetkisi ve seçilen her hesapta sorumlu dayanak").And.NotContain("Taramayı başlat");
    }

    [Theory]
    [InlineData("ServiceAccountUsageScanJobsUnavailable", UiProblemKind.NotConfigured)]
    [InlineData("ServiceAccountUsageScanJobsNotInstalled", UiProblemKind.NotConfigured)]
    [InlineData("ServiceAccountUsageScanAlreadyRunning", UiProblemKind.Conflict)]
    [InlineData("ServiceAccountUsageScanJobStateConflict", UiProblemKind.Conflict)]
    public void JobErrorCodes_HaveOperatorWording(string code, UiProblemKind kind)
    {
        UiProblem problem = ServiceAccountProblems.FromCode(code)!;
        problem.Kind.Should().Be(kind);
        problem.Explanation.Should().NotBeNullOrWhiteSpace().And.NotContain("kullanılmıyor");
    }

    [Fact]
    public void Css_StopsMotionForReducedMotion_AndKeepsTheFocusRing()
    {
        string css = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Pages", "ServiceAccounts", "ServiceAccountUsageScanJob.razor.css"));
        Match reduced = Regex.Match(css, @"@media \(prefers-reduced-motion: reduce\)\s*\{(?<body>[\s\S]*)\}\s*$");
        reduced.Success.Should().BeTrue();
        reduced.Groups["body"].Value.Should().Contain("animation: none").And.Contain("transition: none").And.Contain("sa-wscan-tile-icon");
        css.Should().MatchRegex(@"\.sa-page ::deep \.mud-button-root:focus-visible\s*\{[^}]*outline:\s*2px solid");
        string panel = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaWebScanPanel.razor.css"));
        panel.Should().Contain("overflow-wrap: anywhere");
    }

    private static UsageScanJobView View(string state, UsageScanJobServerView[] servers)
    {
        int done = servers.Count(s => s.State == "Done");
        UsageScanJobCounts counts = new(servers.Length, servers.Count(s => s.State == "Pending"), servers.Count(s => s.State == "Connecting"), done,
            servers.Count(s => s.Tile == "Found"), servers.Count(s => s.Tile == "NotFound"), servers.Count(s => s.Tile == "Uncertain"),
            servers.Count(s => s.Tile == "NotCovered"), servers.Sum(s => s.FormerMatches), servers.Count(s => s.Tile is "NotCovered" or "Uncertain"));
        return new UsageScanJobView(_jobId, state, state == "Running" ? "Çalışıyor" : state, "Discovery", null, "Jea", false, _at, _at, null, false, null, null,
            null, null, true, [new UsageScanJobAccountView(_account, "SYN\\svc_syn", null, null)], servers, counts, null, [], 0, false, null,
            "Sıradaki adım: bulunan kullanımlar aşağıdaki listeye düşer.", new UsageScanJobPermissions(false, false));
    }

    private static UsageScanJobServerView Server(int ordinal, string name, string state, string? status, string tile, string text, int matches) =>
        new(ordinal, name, state, status, tile, text, matches, 0, state == "Done" ? 120 : null, status == "Ok" ? "Success" : null);

    private static UsageScanJobItemView Item(int seq, string server, string name) =>
        new(seq, server, "Former", "SYN\\svc_syn", "WindowsService", "Windows servisi", name, "SYN\\svc_syn", "Running", null);

    private static string Text(string html) => Regex.Replace(html, "<[^>]+>", " ");

    private static async Task<string> RenderPageAsync(UsageScanJobView? view) =>
        await RenderAsync<ServiceAccountUsageScanJob>(new CannedApi(view, null, null), new() { ["Id"] = _jobId });

    private static async Task<string> RenderPanelAsync(UsageScanJobAvailabilityView availability, UsageScanJobSummaryView[] jobs, bool canWork = true) =>
        await RenderAsync<SaWebScanPanel>(new CannedApi(null, availability, jobs),
            new() { ["AccountIds"] = (IReadOnlyList<Guid>)[_account], ["CanWork"] = canWork, ["ShowHistory"] = true });

    private static async Task<string> RenderAsync<TComponent>(CannedApi api, Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        CurrentAccessResponse access = new(Guid.NewGuid(), "Approved", ["Synthetic"], [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work], null,
            "test", new SessionPolicyResponse(15, 8, true, true, "Lax", true, "Synthetic"));
        ICurrentAccessProvider provider = Substitute.For<ICurrentAccessProvider>();
        provider.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccessSnapshot(access, null, DateTimeOffset.UnixEpoch));
        HttpClient http = new(api) { BaseAddress = new Uri("http://localhost/") };

        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(provider);
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton(new ServiceAccountApiClient(http, Substitute.For<IApiSessionContext>()));
        registrations.AddSingleton<NavigationManager>(new FakeNavigation());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private sealed class CannedApi(UsageScanJobView? job, UsageScanJobAvailabilityView? availability, UsageScanJobSummaryView[]? jobs) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            object? body = path.EndsWith("/availability", StringComparison.Ordinal) ? availability
                : path.EndsWith("/usage-scan-jobs", StringComparison.Ordinal) && path.Contains("/accounts/", StringComparison.Ordinal) ? jobs
                : job;
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = JsonContent.Create(new { title = "x", status = 404, code = "ServiceAccountNotFound" }, options: ApiResponseReader.JsonOptions)
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType(), options: ApiResponseReader.JsonOptions) });
        }
    }

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("http://localhost/", "http://localhost/service-accounts/usage-scan-jobs/" + _jobId);

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
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
