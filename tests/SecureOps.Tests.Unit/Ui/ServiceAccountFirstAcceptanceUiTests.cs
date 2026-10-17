using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
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
/// Findings of the first TEST acceptance (2026-10-13), synthetic data only: the list re-reads when a shortcut link changes the
/// query string while the list is already open, the work-summary cards say what they count and open a list with the same number,
/// the directory name search keeps "no match" apart from "could not search", and the usage-scan wording does not suggest the
/// system scans web servers.
/// </summary>
public sealed class ServiceAccountFirstAcceptanceUiTests
{
    private const int _allInScope = 399;
    private const int _proposed = 321;

    [Fact]
    public async Task ShortcutLink_WhileTheListIsOpen_ReReadsTheListAndTheListShowsTheCardNumber()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        fixture.Calls.Should().ContainSingle(c => c.StartsWith("/accounts?", StringComparison.Ordinal), "the first load reads the list once");
        fixture.Html().Should().Contain($"{_allInScope} hesap").And.Contain("kapsamınızdaki tüm servis hesaplarıdır");

        await fixture.NavigateAsync("proposed", false);

        fixture.Calls.Last(c => c.StartsWith("/accounts?", StringComparison.Ordinal)).Should().Contain("status=proposed").And.Contain("page=1");
        string html = fixture.Html();
        html.Should().Contain($"{_proposed} hesap").And.NotContain($"{_allInScope} hesap · sayfa");
        html.Should().Contain("Özet kartıyla aynı sayıdır");
        Regex.Match(html, @"Sahiplik kararı bekleyenler \((\d+)\)").Groups[1].Value.Should().Be(_proposed.ToString(), "the shortcut shows the card's number");
    }

    [Fact]
    public async Task ShortcutLinks_EachRefetchWithTheirOwnFilter_AndClearTheOthers()
    {
        await using Fixture fixture = await Fixture.StartAsync();

        await fixture.NavigateAsync("open", true);
        string open = fixture.Calls.Last(c => c.StartsWith("/accounts?", StringComparison.Ordinal));
        open.Should().Contain("status=open").And.Contain("myTeam=true");

        await fixture.NavigateAsync("overdue", true);
        string overdue = fixture.Calls.Last(c => c.StartsWith("/accounts?", StringComparison.Ordinal));
        overdue.Should().Contain("status=overdue").And.Contain("myTeam=true").And.NotContain("status=open");

        await fixture.NavigateAsync("proposed", false);
        string proposed = fixture.Calls.Last(c => c.StartsWith("/accounts?", StringComparison.Ordinal));
        proposed.Should().Contain("status=proposed").And.NotContain("myTeam=true", "a shortcut means exactly its own filter");

        await fixture.NavigateAsync(null, false);
        string plain = fixture.Calls.Last(c => c.StartsWith("/accounts?", StringComparison.Ordinal));
        plain.Should().NotContain("status=").And.NotContain("myTeam=true");
        fixture.Html().Should().Contain($"{_allInScope} hesap");
    }

    [Fact]
    public async Task PersonFilterInTheLink_IsSentToTheListAndShownAsARemovableChip_AndTheOthersStayCleared()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        string person = "5a1e0000-0000-4000-8000-0000000000b1";

        await fixture.NavigateAsync(null, false, person);

        fixture.Calls.Last(c => c.StartsWith("/accounts?", StringComparison.Ordinal)).Should().Contain("personId=" + person);
        string html = fixture.Html();
        html.Should().Contain("Etkin süzgeçler").And.Contain("Süzgeci kaldır: Kişi (sahip ya da açık işin takipçisi)")
            .And.Contain("ad gösterilemiyor", "a name is never invented when only the id is known").And.Contain("kişi süzgeci hesabın sahibi ya da açık bir işin takipçisi");

        await fixture.NavigateAsync(null, false);
        fixture.Calls.Last(c => c.StartsWith("/accounts?", StringComparison.Ordinal)).Should().NotContain("personId=");
        fixture.Html().Should().NotContain("Etkin süzgeçler");
    }

    [Fact]
    public async Task InvalidPersonInTheLink_IsIgnored_NotSentToTheServer()
    {
        await using Fixture fixture = await Fixture.StartAsync();

        await fixture.NavigateAsync(null, false, "not-a-guid");

        fixture.Calls.Where(c => c.StartsWith("/accounts?", StringComparison.Ordinal)).Should().OnlyContain(c => !c.Contains("personId=", StringComparison.Ordinal));
    }

    [Fact]
    public void ListPage_SendsPersonToListAndExport_AndLabelsItWithTheServerMeaning()
    {
        string page = File.ReadAllText(UiFile("Pages", "ServiceAccounts", "ServiceAccountList.razor"));

        page.Should().Contain("\"personId=\" + person", "ListQuery feeds both the list and the Excel export");
        page.Should().Contain("/accounts/export?\" + string.Join('&', ListQuery(1))");
        page.Should().Contain("Kişi (sahip ya da açık işin takipçisi)").And.Contain("Süzgeçleri temizle");
        page.Should().NotContain("PasswordLastSet", "the list DTO carries no password date; none is shown");
    }

    [Fact]
    public async Task SameQueryRendered_AgainDoesNotRefetch()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        int before = fixture.Calls.Count(c => c.StartsWith("/accounts?", StringComparison.Ordinal));

        await fixture.NavigateAsync(null, false);

        fixture.Calls.Count(c => c.StartsWith("/accounts?", StringComparison.Ordinal)).Should().Be(before);
    }

    [Fact]
    public async Task Cards_SayWhatTheyCount_AndTheTotalIsNotMistakenForAFilteredCount()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        string html = fixture.Html();

        html.Should().Contain("Kapsamınızdaki farklı hesap; tüm hesap sayısı değil");
        html.Should().Contain("kapsamınızdaki hesap sayısı değildir").And.Contain("sahiplik önerisi");
        html.Should().Contain("Açık talep sayısı (hesap değil)");
        html.Should().Contain("Sahiplik kararı bekleyenler (" + _proposed + ")");
    }

    [Fact]
    public async Task ListSearchField_NamesWhatItSearches_AndFiltersAreAppliedByListele()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        string html = fixture.Html();

        html.Should().Contain("Hesap adı").And.NotContain("Hesap adı veya SID", "the server matches the account name only");
        html.Should().Contain("SID ve dizin aranmaz");
        html.Should().Contain("uygulanmaz").And.Contain("Listele");
    }

    [Fact]
    public async Task DirectoryNameSearch_KeepsNoMatchApartFromCouldNotSearch()
    {
        string none = await RenderSearchAsync(new Dictionary<string, object?> { ["Result"] = new DirectoryNameSearchResponse([], false, 3, 10) }, "Ayşe");
        none.Should().Contain("için dizinde eşleşme bulunamadı").And.Contain("kişinin dizinde hiç olmadığını göstermez");

        UiProblem problem = UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = "ServiceAccountDirectoryUnavailable" });
        string failed = await RenderSearchAsync(new Dictionary<string, object?> { ["Problem"] = problem }, "Ayşe");
        failed.Should().Contain("Arama tamamlanmadı").And.Contain("Sonuç bilinmiyor").And.Contain("ayarlı olmayabilir");
        failed.Should().NotContain("eşleşme bulunamadı", "a failed search is not a 'no match'");

        string busy = await RenderSearchAsync(new Dictionary<string, object?> { ["Busy"] = true }, "Ayşe");
        busy.Should().Contain("Dizinde aranıyor").And.NotContain("eşleşme bulunamadı");

        string beforeFirstSearch = await RenderSearchAsync(new Dictionary<string, object?>(), null);
        beforeFirstSearch.Should().NotContain("eşleşme bulunamadı").And.NotContain("Arama tamamlanmadı").And.Contain("en az 3 harf");
    }

    [Fact]
    public void DirectoryNameSearch_ExplainsItIsAboutPeople_NotTheAccountListFilter()
    {
        string panel = File.ReadAllText(UiFile("Shared", "Components", "ServiceAccounts", "SaDirectoryNameSearch.razor"));

        panel.Should().Contain("kişiyi</strong> kurumsal dizinde ada göre bulur").And.Contain("yalnız kayıtlı servis hesaplarını süzer, dizine bakmaz");
    }

    [Fact]
    public void UsageScanTexts_SayTheSystemDoesNotScan_AndThatRemoteScanningIsSeparateAndUnapproved()
    {
        string panel = File.ReadAllText(UiFile("Shared", "Components", "ServiceAccounts", "SaUsageScanPanel.razor"));
        string batch = File.ReadAllText(UiFile("Shared", "Components", "ServiceAccounts", "SaUsageScanBatchForm.razor"));

        panel.Should().Contain("Sistem sunuculara bağlanmaz").And.Contain("salt okunur JEA ile uzaktan tarama yapması ayrı bir yetenektir")
            .And.Contain("Bilgi Güvenliği onayı olmadığı için");
        batch.Should().Contain("Sistem sunuculara bağlanıp tarama yapmaz").And.Contain("Bilgi Güvenliği onayı olmadığı için şu an yoktur");
    }

    [Fact]
    public void ListGuide_NoLongerPromisesSidSearch_AndDescribesTheShortcuts()
    {
        ServiceAccountGuides.List.Steps.Single(s => s.Title == "Bul").Text.Should().Contain("SID aranmaz").And.Contain("özet düğmeleri");
    }

    [Fact]
    public void ImportRows_NameTheExcelRow_AndWhyAConflictOrInvalidRowNeedsAttention()
    {
        string rows = File.ReadAllText(UiFile("Shared", "Components", "ServiceAccounts", "SaImportRows.razor"));

        rows.Should().Contain("Excel satırı @context.RowNumber").And.Contain("Hata (@context.Sheet, satır @context.RowNumber): @error");
        rows.Should().Contain("sistemdeki kayıtla çelişiyor").And.Contain("Satır doğrulamadan geçmedi ve aktarılmaz");
        rows.Should().Contain("Sunucu bu satır için ayrıntılı bir hata metni göndermedi", "a missing reason is said, not hidden");
    }

    private static async Task<string> RenderSearchAsync(Dictionary<string, object?> parameters, string? query)
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new Navigation("http://localhost/service-accounts"));
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        parameters["SearchedQuery"] = query;
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<SaDirectoryNameSearch>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private static string UiFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return Path.Combine([directory!.FullName, "src", "SecureOps.Ui", .. parts]);
    }

    /// <summary>List page whose query-string parameters are supplied as component parameters (no router in the static renderer).</summary>
    public sealed class QueryList : ServiceAccountList
    {
        [Parameter] public string? Status { get; set; }

        [Parameter] public bool Team { get; set; }

        [Parameter] public string? Person { get; set; }

        public override Task SetParametersAsync(ParameterView parameters)
        {
            parameters.TryGetValue(nameof(Status), out string? status);
            parameters.TryGetValue(nameof(Team), out bool team);
            parameters.TryGetValue(nameof(Person), out string? person);
            (StatusQuery, MyTeamQuery, PersonQuery) = (status, team, person);
            return base.SetParametersAsync(parameters);
        }
    }

    private sealed class HostHandle
    {
        public ListHost? Host { get; set; }
    }

    private sealed class ListHost : ComponentBase
    {
        [Inject] public HostHandle Handle { get; set; } = default!;

        public string? Status { get; set; }

        public bool Team { get; set; }

        public string? Person { get; set; }

        protected override void OnInitialized() => Handle.Host = this;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<QueryList>(0);
            builder.AddAttribute(1, nameof(QueryList.Status), Status);
            builder.AddAttribute(2, nameof(QueryList.Team), Team);
            builder.AddAttribute(3, nameof(QueryList.Person), Person);
            builder.CloseComponent();
        }

        public void Navigate(string? status, bool team, string? person = null)
        {
            (Status, Team, Person) = (status, team, person);
            StateHasChanged();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly Func<string> _root;
        private readonly HostHandle _handle;
        private readonly Api _api;

        private Fixture(ServiceProvider services, HtmlRenderer renderer, Func<string> root, HostHandle handle, Api api)
            => (_services, _renderer, _root, _handle, _api) = (services, renderer, root, handle, api);

        public IReadOnlyList<string> Calls => _api.Calls;

        public static async Task<Fixture> StartAsync()
        {
            CurrentAccessResponse access = new(Guid.NewGuid(), "Approved", ["Synthetic"], [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work, ServiceAccountCapabilities.Report],
                null, "test", new SessionPolicyResponse(15, 8, true, true, "Lax", true, "Synthetic"));
            ICurrentAccessProvider provider = Substitute.For<ICurrentAccessProvider>();
            provider.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccessSnapshot(access, null, DateTimeOffset.UnixEpoch));
            Api api = new();
            HostHandle handle = new();

            ServiceCollection registrations = new();
            registrations.AddLogging();
            registrations.AddMudServices();
            registrations.AddSingleton(provider);
            registrations.AddSingleton(handle);
            registrations.AddSingleton(Substitute.For<IJSRuntime>());
            registrations.AddSingleton<NavigationManager>(new Navigation("http://localhost/service-accounts"));
            registrations.AddSingleton(new ServiceAccountApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost/") }, new FakeApiSessionContext()));
            ServiceProvider services = registrations.BuildServiceProvider();
            HtmlRenderer renderer = new(services, services.GetRequiredService<ILoggerFactory>());
            HtmlRootComponent root;
            root = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<ListHost>());
            return new Fixture(services, renderer, () => root.ToHtmlString(), handle, api);
        }

        public string Html() => WebUtility.HtmlDecode(Regex.Replace(_renderer.Dispatcher.InvokeAsync(() => _root()).GetAwaiter().GetResult(), " b-[a-z0-9]{10}", string.Empty));

        /// <summary>Simulates a shortcut link: the same page gets a new query string.</summary>
        public async Task NavigateAsync(string? status, bool team, string? person = null)
        {
            int before = _api.Calls.Count;
            await _renderer.Dispatcher.InvokeAsync(() => _handle.Host!.Navigate(status, team, person));
            // The re-read (if any) runs after the parameter update; give it a moment to finish, then require quiet.
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(20);
                if (_api.Calls.Count > before && i > 3)
                {
                    break;
                }
            }

            await Task.Delay(50);
        }

        public async ValueTask DisposeAsync() { await _renderer.DisposeAsync(); await _services.DisposeAsync(); }
    }

    /// <summary>Synthetic API: 399 accounts in scope, 321 with a proposed ownership, a handful open for the caller's team.</summary>
    private sealed class Api : HttpMessageHandler
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_calls)
                {
                    return [.. _calls];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery.Replace("/api/v1/service-accounts", string.Empty, StringComparison.Ordinal);
            lock (_calls)
            {
                _calls.Add(path);
            }

            object body = path switch
            {
                "/me" => new ServiceAccountMe(true, [ServiceAccountCapabilities.View], "All", [], [], true),
                "/work-summary" => new ServiceAccountWorkSummary(new DateOnly(2026, 10, 13), true, 7, 1, 0, 0, 0, true, 2, 0, _proposed, 0, []),
                "/organizations" => Array.Empty<OrganizationView>(),
                "/teams" => Array.Empty<TeamView>(),
                _ when path.StartsWith("/accounts?", StringComparison.Ordinal) => Accounts(path),
                _ => throw new InvalidOperationException("Unexpected call " + path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: ApiResponseReader.JsonOptions) });
        }

        private static AccountPage Accounts(string path)
        {
            int total = path.Contains("status=proposed", StringComparison.Ordinal) ? _proposed : path.Contains("myTeam=true", StringComparison.Ordinal) ? 7 : _allInScope;
            AccountListItem item = new(Guid.Parse("5a1e0000-0000-4000-8000-0000000000a1"), "SYN_SVC_ONE", "SYN", "Resolved", null, null, null, null, false, 0, null,
                "Açık işi yok", null, null, "v1");
            return new AccountPage([item], total, 1, 25);
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation(string uri) => Initialize("http://localhost/", uri);

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
