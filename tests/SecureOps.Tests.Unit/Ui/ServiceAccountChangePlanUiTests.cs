using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// The gMSA conversion plan screens (synthetic data, static HTML renderer): the step flow reflects only what the server returned,
/// the approval form appears only when the server says the caller may approve, "not found" never reads as "unused", and the
/// checklist says plainly that nothing can be marked yet and that "done" is a statement, not a verification.
/// </summary>
public sealed class ServiceAccountChangePlanUiTests
{
    private static readonly DateTimeOffset _at = DateTimeOffset.Parse("2026-10-07T09:00:00+03:00");
    private static readonly Guid _planId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string _sha = new('a', 64);

    [Theory]
    [InlineData("Draft", "Done", "Current", "Upcoming", "Upcoming", "Upcoming")]
    [InlineData("Previewed", "Done", "Done", "Current", "Upcoming", "Upcoming")]
    [InlineData("Approved", "Done", "Done", "Done", "NotAvailable", "Upcoming")]
    [InlineData("Completed", "Done", "Done", "Done", "Done", "Current")]
    public void Steps_FollowTheServerStatus(string status, params string[] expected)
    {
        ChangePlanView plan = Plan(status, preview: status != "Draft", approval: status is "Approved" or "Completed");

        ChangePlanPresentation.Steps(plan).Select(s => s.State.ToString()).Should().Equal(expected);
        ChangePlanPresentation.Steps(plan).Select(s => s.Key).Should().Equal("plan", "preview", "approval", "checklist", "verification");
    }

    [Fact]
    public void Steps_OfACancelledPlan_KeepWhatWasDone_AndStopTheRest()
    {
        ChangePlanPresentation.Steps(Plan("Cancelled", preview: true, approval: false)).Select(s => s.State.ToString())
            .Should().Equal("Done", "Done", "Stopped", "Stopped", "Stopped");
        ChangePlanPresentation.Steps(Plan("Cancelled", preview: false, approval: false)).Select(s => s.State.ToString())
            .Should().Equal("Done", "Stopped", "Stopped", "Stopped", "Stopped");
    }

    [Fact]
    public void Steps_NeverClaimAVerification_WhenThePlanIsClosed()
    {
        string text = string.Join(' ', ChangePlanPresentation.Steps(Plan("Completed", true, true)).Select(s => s.Missing));

        text.Should().Contain("Kapanış doğrulama sayılmaz").And.NotContain("doğrulandı");
    }

    [Fact]
    public void Flags_AllHaveText_Icon_AndNoneSaysUnused()
    {
        foreach (string flag in ChangePlanPresentation.Flags)
        {
            ChangePlanPresentation.FlagShortLabel(flag).Should().NotBeNullOrWhiteSpace().And.NotContain("kullanılmıyor");
            ChangePlanPresentation.FlagIcon(flag).Should().NotBeNullOrWhiteSpace();
            ChangePlanPresentation.FlagExplanation(flag).Should().NotBeNullOrWhiteSpace();
        }

        ChangePlanPresentation.FlagShortLabel("NothingFound").Should().Be("Taramada bulunamadı");
        ChangePlanPresentation.FlagExplanation("NothingFound").Should().Contain("\"Kullanılmıyor\" demek değildir");
        ChangePlanPresentation.FlagExplanation("NoScan").Should().Contain("Kullanılmadığı anlamına gelmez");
    }

    [Fact]
    public void Counts_ReadEveryFlagFromTheSummary()
    {
        ChangePlanFlagCounts counts = new(1, 2, 3, 4, 5, 6, 7);

        ChangePlanPresentation.Flags.Select(f => ChangePlanPresentation.Count(counts, f)).Should().Equal(1, 2, 3, 4, 5, 7, 6);
    }

    [Fact]
    public void ErrorBodies_ParseTheServersCurrentExtension()
    {
        string planJson = JsonSerializer.Serialize(Plan("Previewed", true, false), ApiResponseReader.JsonOptions);
        ChangePlanPresentation.CurrentPlan(planJson)!.Id.Should().Be(_planId);
        ChangePlanPresentation.CurrentPlan("not json").Should().BeNull();
        ChangePlanPresentation.CurrentPlan(null).Should().BeNull();

        string resultsJson = JsonSerializer.Serialize(new[] { new ChangePlanAccountResult(Guid.NewGuid(), null, "Unavailable", "Eklenemedi") }, ApiResponseReader.JsonOptions);
        ChangePlanPresentation.AccountResults(resultsJson).Should().ContainSingle().Which.AccountName.Should().BeNull();
    }

    [Theory]
    [InlineData("ServiceAccountChangePlanAccountsRefused", UiProblemKind.Validation)]
    [InlineData("ServiceAccountChangePlanStateConflict", UiProblemKind.Conflict)]
    [InlineData("ServiceAccountChangePlansNotInstalled", UiProblemKind.NotConfigured)]
    public void PlanErrorCodes_HaveOperatorWording(string code, UiProblemKind kind)
    {
        UiProblem? problem = ServiceAccountProblems.FromCode(code);

        problem.Should().NotBeNull();
        problem!.Kind.Should().Be(kind);
        problem.Explanation.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("previewStale", "yeniden onaylayın")]
    [InlineData("approverIsPlanner", "kendi planını onaylayamaz")]
    [InlineData("approverChangedPlan", "değiştiren kişi onaylayamaz")]
    [InlineData("planClosed", "kapanmış")]
    public void PlanFields_HaveOperatorWording(string field, string fragment) =>
        ServiceAccountProblems.FieldText(field).Should().Contain(fragment);

    [Fact]
    public async Task Preview_ShowsCountersAndRows_WithoutCallingNotFoundUnused()
    {
        ChangePlanPreviewView preview = new(2, _sha, 7, 3, new ChangePlanFlagCounts(1, 0, 0, 0, 1, 0, 1), "Synthetic Planner", _at);
        ChangePlanItemPage items = new(2,
        [
            Item("Ok", "SYN-APP01", "AppPool", "SynPool", "SYN\\svc", "SYN\\gmsa$", "Taramada bulundu"),
            Item("ManualOnly", "SYN-WEB01", "IisSiteConnectAs", "SynSite", "SYN\\svc", "SYN\\gmsa$", "Elle: IIS connect as"),
            Item("NothingFound", null, null, null, null, "SYN\\gmsa$", "Son tarama bu hesabı taranan sunucularda bulmadı (kullanılmıyor anlamına gelmez)")
        ], 120, 1, 50);

        string html = await RenderAsync<SaPlanPreview>(new() { ["Preview"] = preview, ["Items"] = items });
        string text = Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ");

        text.Should().Contain("Önizleme sürümü").And.Contain("aaaaaaaaaaaa…").And.Contain("Taramada bulunamadı").And.Contain("Elle yapılacak");
        text.Should().Contain("Bu, hesabın kullanılmadığı anlamına gelmez");
        text.Should().Contain("Bilgi yok", "a row with no server says so instead of leaving the cell blank");
        html.Should().Contain("Önizleme satırları: 1–50 / 120 satır (sayfa 1 / 3)");
        Regex.IsMatch(text, "(?<!değil|gelmez|anlamına)\\s+kullanılmıyor", RegexOptions.IgnoreCase).Should().BeFalse("no row or counter may state a verdict of non-use");
    }

    [Fact]
    public async Task Checklist_MarksManualOnlyRows_AndStatesNothingCanBeMarkedYet()
    {
        ChangePlanItemPage items = new(2,
        [
            Item("ManualOnly", "SYN-WEB01", "IisSiteConnectAs", "SynSite", "SYN\\svc", "SYN\\gmsa$", "Elle: IIS connect as"),
            Item("Ok", "SYN-APP01", "AppPool", "SynPool", "SYN\\svc", "SYN\\gmsa$", "Taramada bulundu")
        ], 2, 1, 50);

        string text = Regex.Replace(Regex.Replace(await RenderAsync<SaPlanChecklist>(new() { ["Items"] = items }), "<[^>]+>", " "), @"\s+", " ");

        text.Should().Contain("Elle yapılacak").And.Contain("SynSite").And.Contain("SynPool");
        text.Should().Contain("Yapıldı işareti kişinin beyanıdır, doğrulama değildir").And.Contain("burada hiçbir şey kaydedilmez");
        text.Should().NotContain("Yapıldı olarak işaretle");
    }

    [Fact]
    public async Task Pager_KeepsButtonsFocusable_AtTheEdgesAndWhileBusy()
    {
        string first = await RenderAsync<SaPager>(new() { ["Label"] = "Planlar", ["Page"] = 1, ["PageSize"] = 25, ["Total"] = 60, ["Unit"] = "plan" });
        string last = await RenderAsync<SaPager>(new() { ["Label"] = "Planlar", ["Page"] = 3, ["PageSize"] = 25, ["Total"] = 60, ["Busy"] = true });

        first.Should().Contain("Planlar: 1–25 / 60 plan (sayfa 1 / 3)").And.Contain("aria-disabled=\"true\"");
        Regex.Matches(first, "aria-disabled=\"true\"").Count.Should().Be(1, "only Önceki is unavailable on page 1");
        Regex.Matches(last, "aria-disabled=\"true\"").Count.Should().Be(2, "both are inert while busy");
        foreach (string html in new[] { first, last })
        {
            Regex.IsMatch(html, @"<button[^>]*\sdisabled", RegexOptions.IgnoreCase).Should().BeFalse("a disabled button drops keyboard focus to the page");
        }
    }

    [Fact]
    public async Task Pager_IsHidden_WhenEverythingFitsOnOnePage()
    {
        string html = await RenderAsync<SaPager>(new() { ["Label"] = "Planlar", ["Page"] = 1, ["PageSize"] = 25, ["Total"] = 3 });

        html.Should().NotContain("Sonraki");
    }

    [Fact]
    public async Task Editor_ShowsPerAccountAnswers_KeepsTheDraft_AndHasNoPasswordField()
    {
        var refused = Guid.NewGuid();
        List<PlanAccountDraft> rows = [new() { AccountId = refused, AccountName = "svc-syn-01", Domain = "SYN", TargetGmsaName = "gmsa-syn-01" }];

        string html = await RenderAsync<SaPlanAccountsEditor>(new()
        {
            ["Rows"] = rows,
            ["Results"] = (IReadOnlyList<ChangePlanAccountResult>)[new(refused, "svc-syn-01", "InOpenPlan", "Başka bir açık planda")],
            ["OnSubmit"] = EventCallback.Empty
        });

        html.Should().Contain("svc-syn-01").And.Contain("Başka bir açık planda").And.Contain("gmsa-syn-01");
        html.ToLowerInvariant().Should().NotContain("type=\"password\"").And.NotContain("parola alanı");
    }

    [Fact]
    public async Task Page_ShowsTheApprovalForm_OnlyWhenTheServerAllowsApproval()
    {
        ChangePlanView allowed = Plan("Previewed", true, false, permissions: new(false, false, true, null, true));
        ChangePlanView planner = Plan("Previewed", true, false, permissions: new(true, true, false, "approverIsPlanner", true));
        ChangePlanView nobody = Plan("Previewed", true, false, permissions: new(false, false, false, null, false));

        string allowedHtml = await RenderPageAsync(allowed);
        string plannerHtml = await RenderPageAsync(planner);
        string nobodyHtml = await RenderPageAsync(nobody);

        allowedHtml.Should().Contain("OCO numarası").And.Contain("Önizlemeyi onayla").And.Contain("sürüm 2 (özet aaaaaaaaaaaa");
        plannerHtml.Should().NotContain("Önizlemeyi onayla").And.Contain("kendi planını onaylayamaz");
        nobodyHtml.Should().NotContain("Önizlemeyi onayla").And.Contain("onaylama yetkiniz görünmüyor");
    }

    [Fact]
    public async Task Page_ShowsAllFiveSteps_AndTheCurrentOneIsMarked()
    {
        string html = await RenderPageAsync(Plan("Previewed", true, false));

        foreach (string name in new[] { "1. Plan", "2. Önizleme", "3. Onay", "4. Kontrol listesi", "5. Doğrulama" })
        {
            html.Should().Contain(name);
        }

        Regex.Matches(html, "aria-current=\"step\"").Count.Should().Be(1);
        html.Should().Contain("Planı hazırlamayan bir doğrulama yetkilisi bu önizlemeyi onaylamalı");
    }

    [Fact]
    public async Task Page_OfAnApprovedPlan_ShowsTheApprovalAndTheChecklist_NotAForm()
    {
        string html = await RenderPageAsync(Plan("Approved", true, true));
        string text = Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ");

        text.Should().Contain("OCO-1001").And.Contain("Yapıldı işareti kişinin beyanıdır, doğrulama değildir");
        text.Should().Contain("ITSM'de sorgulanmadı");
        html.Should().NotContain("Önizlemeyi onayla");
    }

    [Fact]
    public async Task Page_OfAMissingPlan_SaysNoneOrOutOfScope_AndShowsNoCommands()
    {
        string html = await RenderPageAsync(null);

        html.Should().Contain("Kapsamınız dışındaki bir plan, olmayan plandan ayırt edilemez");
        html.Should().NotContain("Önizleme oluştur").And.NotContain("Planı iptal et");
    }

    [Fact]
    public void Sources_HaveNoPasswordField_AndKeepTheFocusRing()
    {
        string ui = Path.Combine(Root(), "src", "SecureOps.Ui");
        string[] files =
        [
            Path.Combine("Pages", "ServiceAccounts", "ServiceAccountPlans.razor"),
            Path.Combine("Pages", "ServiceAccounts", "ServiceAccountPlanDetail.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaPlanAccountsEditor.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaPlanApproveForm.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaPlanChecklist.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaPlanPreview.razor")
        ];
        foreach (string file in files)
        {
            string source = File.ReadAllText(Path.Combine(ui, file));
            source.Should().NotContainEquivalentOf("InputType.Password").And.NotContainEquivalentOf("type=\"password\"", file);
        }

        foreach (string css in new[]
        {
            Path.Combine("Pages", "ServiceAccounts", "ServiceAccountPlans.razor.css"),
            Path.Combine("Pages", "ServiceAccounts", "ServiceAccountPlanDetail.razor.css"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaPager.razor.css")
        })
        {
            File.ReadAllText(Path.Combine(ui, css)).Should().MatchRegex(@":focus-visible\s*\{[^}]*outline:\s*2px solid", css);
        }
    }

    [Fact]
    public void EveryModulePager_UsesTheSharedFocusPreservingComponent()
    {
        string components = Path.Combine(Root(), "src", "SecureOps.Ui");
        foreach (string file in new[]
        {
            Path.Combine("Pages", "ServiceAccounts", "ServiceAccountList.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaImportRows.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaScanDiffView.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaUsageScanPanel.razor"),
            Path.Combine("Shared", "Components", "ServiceAccounts", "SaPlanPreview.razor"),
            Path.Combine("Pages", "ServiceAccounts", "ServiceAccountPlans.razor")
        })
        {
            string source = File.ReadAllText(Path.Combine(components, file));
            source.Should().Contain("<SaPager", file);
            source.Should().NotMatchRegex(@"Disabled=""@\(Busy \|\| [^""]*(Page|_pageNumber|current)[^""]*\)""[^>]*>\s*Önceki", file);
        }
    }

    private static ChangePlanView Plan(string status, bool preview, bool approval, ChangePlanPermissions? permissions = null) => new(
        _planId, "GmsaConversion", "Synthetic gMSA plan", status, status, "Synthetic Planner", _at, _at, "v1",
        [new ChangePlanAccountView(Guid.NewGuid(), "svc-syn-01", "SYN", "gmsa-syn-01", null, _at)],
        preview ? new ChangePlanPreviewView(2, _sha, 7, 2, new ChangePlanFlagCounts(1, 0, 0, 0, 1, 0), "Synthetic Planner", _at) : null,
        approval ? new ChangePlanApprovalView(2, _sha, "OCO-1001", _at, _at.AddHours(2), "Synthetic reason", "Synthetic Approver", _at) : null,
        [new ChangePlanEventView("Created", null, "Draft", null, null, "Synthetic Planner", _at)],
        permissions ?? new ChangePlanPermissions(false, false, false, null, false));

    private static ChangePlanItemView Item(string flag, string? server, string? type, string? name, string? current, string target, string label) =>
        new(Guid.NewGuid(), Guid.Parse("22222222-2222-2222-2222-222222222222"), "svc-syn-01", server, type, name, current, target, flag, label, server is null ? null : _at);

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

    /// <summary>Renders the real page against a canned API: the plan (or 404) and one page of rows.</summary>
    private static async Task<string> RenderPageAsync(ChangePlanView? plan)
    {
        CurrentAccessResponse access = new(Guid.NewGuid(), "Approved", ["Synthetic"],
            [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work, ServiceAccountCapabilities.Verify], null, "test",
            new SessionPolicyResponse(15, 8, true, true, "Lax", true, "Synthetic"));
        AccessSnapshot snapshot = new(access, null, DateTimeOffset.UnixEpoch);
        ICurrentAccessProvider provider = Substitute.For<ICurrentAccessProvider>();
        provider.GetAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        IApiSessionContext session = Substitute.For<IApiSessionContext>();
        HttpClient http = new(new CannedApi(plan)) { BaseAddress = new Uri("http://localhost/") };

        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(provider);
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton(new ServiceAccountApiClient(http, session));
        registrations.AddSingleton<NavigationManager>(new FakeNavigation());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<ServiceAccountPlanDetail>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Id"] = _planId }))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private sealed class CannedApi(ChangePlanView? plan) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (plan is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = JsonContent.Create(new { title = "x", status = 404, code = "ServiceAccountNotFound" }, options: ApiResponseReader.JsonOptions)
                });
            }

            object body = path.EndsWith("/items", StringComparison.Ordinal)
                ? new ChangePlanItemPage(2, [Item("ManualOnly", "SYN-WEB01", "IisSiteConnectAs", "SynSite", "SYN\\svc", "SYN\\gmsa$", "Elle: IIS connect as")], 1, 1, 50)
                : plan;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType(), options: ApiResponseReader.JsonOptions) });
        }
    }

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("http://localhost/", "http://localhost/service-accounts/plans/" + _planId);

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
