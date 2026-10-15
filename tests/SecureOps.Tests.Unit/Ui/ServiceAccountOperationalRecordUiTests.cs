using System.Net;
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
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;
using SecureOps.Ui.Shared.Components;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The plan's OR panel (ADR-0030, PROPOSED; OR-2), synthetic data, static HTML renderer: it shows only what the server answered,
/// keeps "Belirsiz" visible and never as success, says "bilinmiyor" for every unproven tracking value, requires a fresh preview of
/// exactly the text that is sent, sends with the preview's key and retries an unknown answer with the same key.
/// </summary>
public sealed class ServiceAccountOperationalRecordUiTests
{
    private static readonly DateTimeOffset _at = DateTimeOffset.Parse("2026-10-13T09:00:00+03:00");
    private static readonly Guid _plan = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _account = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Presentation_SaysUnknown_ForEveryUnprovenValue_AndKeepsAProvenZero()
    {
        OperationalRecordPresentation.Known(null).Should().Be("bilinmiyor");
        OperationalRecordPresentation.Known(" ").Should().Be("bilinmiyor");
        OperationalRecordPresentation.Known("9301").Should().Be("9301");
        OperationalRecordPresentation.Count(false, null).Should().Be("bilinmiyor");
        OperationalRecordPresentation.Count(false, 0).Should().Be("bilinmiyor", "an unknown part is never shown as zero");
        OperationalRecordPresentation.Count(true, 0).Should().Be("0");
        OperationalRecordPresentation.VerificationLabel("NotVerified").Should().Contain("\"yok\" demek değildir");
        OperationalRecordPresentation.VerificationLabel(null).Should().BeNull();
    }

    [Theory]
    [InlineData("Created", SoStatusBadge.BadgeTone.Positive)]
    [InlineData("Linked", SoStatusBadge.BadgeTone.Positive)]
    [InlineData("OutcomeUnknown", SoStatusBadge.BadgeTone.Caution)]
    [InlineData("Sending", SoStatusBadge.BadgeTone.Caution)]
    [InlineData("PartiallyCreated", SoStatusBadge.BadgeTone.Caution)]
    [InlineData("NotSent", SoStatusBadge.BadgeTone.Neutral)]
    [InlineData("ConfirmedNotCreated", SoStatusBadge.BadgeTone.Neutral)]
    public void Presentation_NeverShowsAnUnknownOutcomeAsSuccess(string state, SoStatusBadge.BadgeTone tone)
    {
        OperationalRecordPresentation.StateTone(state).Should().Be(tone);
        OperationalRecordPresentation.StateIcon(state).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Presentation_RequesterAndProvider_AreTextNotColourOnly()
    {
        OperationalRecordPresentation.RequesterBadge(new OperationalRecordRequesterView("Matched", "x", "920001", true)).Label.Should().Be("Eşleşti");
        OperationalRecordPresentation.RequesterBadge(new OperationalRecordRequesterView("Matched", "x", "920001", false)).Label.Should().Contain("OR açılamaz");
        OperationalRecordPresentation.RequesterBadge(new OperationalRecordRequesterView("Ambiguous", "x", null, false)).Tone.Should().Be(SoStatusBadge.BadgeTone.Critical);
        OperationalRecordPresentation.ProviderBadge(View(synthetic: true)).Label.Should().Contain("Sentetik");
        OperationalRecordPresentation.ProviderBadge(View(provider: "Disabled", available: false)).Label.Should().Be("OR açma kapalı");
        OperationalRecordPresentation.PlanReference(Submission("OutcomeUnknown")).Should().Be($"WASAS plan {_plan:D} / hesap {_account:D}");
    }

    [Fact]
    public void Problems_ExplainTheOrCodes_AndSayNothingWasSent()
    {
        ServiceAccountProblems.FieldText("orPreviewStale").Should().Contain("Hiçbir şey gönderilmedi");
        ServiceAccountProblems.FieldText("requesterNotMatched").Should().Contain("OR açılmadı");
        ServiceAccountProblems.FieldText("submissionExists").Should().Contain("ikinci OR açılmadı");
        UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = "ServiceAccountOperationalRecordsNotInstalled" }).Explanation.Should().Contain("035");
        UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = "ServiceAccountOperationalRecordCreationDisabled" }).Title.Should().Contain("kapalı");
    }

    [Fact]
    public async Task Panel_Disabled_ShowsWhy_AndOffersNoButton()
    {
        string html = await RenderAsync(new Api { Panel = View(provider: "Disabled", available: false, reason: "creationDisabled") });

        html.Should().Contain("OR açma kapalı").And.Contain("sunucu yapılandırması varsayılanı").And.NotContain("OR aç (önce önizleme)");
    }

    [Fact]
    public async Task Panel_Synthetic_SaysSo_AndOffersTheButton_OnlyWhereTheServerAllows()
    {
        string html = await RenderAsync(new Api { Panel = View(synthetic: true) });

        html.Should().Contain("Sentetik sağlayıcı").And.Contain("gerçek değildir").And.Contain("OR aç (önce önizleme)").And.Contain("Henüz OR açılmadı");
        (await RenderAsync(new Api { Panel = View(synthetic: true, canOpen: false, blocked: "noPermission") })).Should().NotContain("OR aç (önce önizleme)");
    }

    [Fact]
    public async Task Panel_OutcomeUnknown_IsShownAsUnknown_WithTheLineToSearch_AndReconciliation()
    {
        string html = await RenderAsync(new Api { Panel = View(current: Submission("OutcomeUnknown"), canOpen: false, canLink: true, canNotCreated: true) });

        html.Should().Contain("Belirsiz — OR açılmış olabilir").And.Contain("Ürün yeniden göndermez").And.Contain($"WASAS plan {_plan:D}")
            .And.Contain("Mutabakat: OR Turuncu Hat'ta açıldı mı?").And.Contain("OR açılmadığını teyit et").And.NotContain("OR aç (önce önizleme)");
        html.Should().NotContain("OR açıldı<", "an unknown outcome is never labelled as created");
    }

    [Fact]
    public async Task Panel_LoadFailure_IsAProblem_NotAnEmptyList()
    {
        string html = await RenderAsync(new Api
        {
            PanelProblem = UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = "ServiceAccountOperationalRecordsNotInstalled" })
        });

        html.Should().Contain("OR açma bu ortamda kurulu değil").And.NotContain("Henüz OR açılmadı");
    }

    [Fact]
    public async Task Flow_NeedsAFreshPreviewOfExactlyTheSentText_ThenSendsWithThePreviewsKey()
    {
        Api api = new() { Panel = View(synthetic: true) };
        await using Harness harness = await Harness.StartAsync(api);

        await harness.RunAsync(c => c.OpenAsync(_account));
        harness.Component.CurrentPreview!.BlockedReason.Should().Be("environmentRequired");
        harness.Component.CanConfirm.Should().BeFalse("no environment chosen yet");
        harness.Html().Should().Contain("Ortamı seçin.").And.Contain("Bildiren (düğmeye basan kişi)").And.Contain("Eşleşti");

        await harness.RunAsync(c => c.Environment = "Test");
        harness.Component.Dirty.Should().BeTrue();
        harness.Component.CanConfirm.Should().BeFalse("the environment change was not previewed");
        await harness.RunAsync(c => c.RefreshPreviewAsync());
        harness.Component.CanConfirm.Should().BeTrue();
        Guid key = harness.Component.Key;
        key.Should().NotBe(Guid.Empty);

        await harness.RunAsync(c => c.Description += " ek");
        harness.Component.CanConfirm.Should().BeFalse("edited text must be previewed again");
        harness.Html().Should().Contain("Önizlemeyi güncelle\"yi seçin");
        await harness.RunAsync(c => c.RefreshPreviewAsync());
        harness.Component.Key.Should().NotBe(key, "a new sendable preview is a new decision");
        key = harness.Component.Key;

        await harness.RunAsync(c => c.ConfirmAsync());

        OperationalRecordSubmitRequest sent = api.Submits.Should().ContainSingle().Subject;
        (sent.Fingerprint, sent.IdempotencyKey, sent.Environment, sent.Description).Should().Be((api.LastPreview!.Fingerprint, key, "Test", api.LastPreview.Description));
        api.Loads.Should().Be(2, "the panel is read again after a successful send");
    }

    [Fact]
    public async Task Flow_AnUnknownAnswer_IsRetriedWithTheSameKey_NeverANewOne()
    {
        Api api = new() { Panel = View(synthetic: true), SubmitUnknown = true };
        await using Harness harness = await Harness.StartAsync(api);
        await harness.RunAsync(c => c.OpenAsync(_account));
        await harness.RunAsync(c => c.Environment = "Test");
        await harness.RunAsync(c => c.RefreshPreviewAsync());

        await harness.RunAsync(c => c.ConfirmAsync());
        harness.Html().Should().Contain("Cevap alınamadı: OR açılmış olabilir").And.Contain("ikinci OR açılmaz").And.Contain("Aynı anahtarla yeniden dene");
        await harness.RunAsync(c => c.RetrySameKeyAsync());

        api.Submits.Should().HaveCount(2);
        api.Submits.Select(s => s.IdempotencyKey).Distinct().Should().ContainSingle("the retry reuses the confirmation's key");
    }

    [Fact]
    public async Task Flow_RequesterNotMatched_CannotBeConfirmed()
    {
        Api api = new() { Panel = View(synthetic: true), Requester = new OperationalRecordRequesterView("NotMatched", "Turuncu Hat'ta sizinle birebir eşleşen kullanıcı yok. OR açılamaz.", null, false) };
        await using Harness harness = await Harness.StartAsync(api);
        await harness.RunAsync(c => c.OpenAsync(_account));
        await harness.RunAsync(c => c.Environment = "Test");
        await harness.RunAsync(c => c.RefreshPreviewAsync());

        harness.Component.CanConfirm.Should().BeFalse();
        await harness.RunAsync(c => c.ConfirmAsync());
        api.Submits.Should().BeEmpty();
        harness.Html().Should().Contain("Eşleşmedi — OR açılamaz").And.Contain("birebir eşleşen kullanıcı yok");
    }

    [Fact]
    public async Task Tracking_ShowsBilinmiyor_ForUnprovenParts_AndRawCodesAsRaw()
    {
        Api api = new() { Panel = View(current: Submission("Created", "OR-123456"), canOpen: false) };
        await using Harness harness = await Harness.StartAsync(api);

        await harness.RunAsync(c => c.TrackAsync(_account));

        string html = harness.Html();
        html.Should().Contain("Turuncu Hat durumu (salt okunur, kaydedilmez)").And.Contain("Aktivite sayısı").And.Contain("ham değerleridir")
            .And.Contain("bilinmiyor").And.NotContain("onaylandı<");
        Regex.Matches(html, "bilinmiyor").Count.Should().BeGreaterThanOrEqualTo(5, "every null tracking value reads as unknown");
    }

    [Fact]
    public async Task Reconcile_ChecksTheNumber_ThenLinksWithTheReason()
    {
        Api api = new() { Panel = View(current: Submission("OutcomeUnknown"), canOpen: false, canLink: true, canNotCreated: true) };
        await using Harness harness = await Harness.StartAsync(api);

        await harness.RunAsync(c => c.Number = "OR-000001");
        await harness.RunAsync(c => c.CheckNumberAsync(_account));
        harness.Html().Should().Contain("Turuncu Hat'ta bulundu").And.Contain("Hayır — doğru OR olduğundan emin olun");
        await harness.RunAsync(c => c.Reason = "Sentetik: plan referansıyla bulundu");
        await harness.RunAsync(c => c.LinkAsync(_account, api.Panel!.Accounts[0].Current!.Id));

        api.Links.Should().ContainSingle().Which.Should().Be(new OperationalRecordLinkRequest(api.Panel.Accounts[0].Current!.Id, "OR-000001",
            "Sentetik: plan referansıyla bulundu"));
    }

    private static PlanOperationalRecordsView View(string provider = "Fake", bool synthetic = false, bool available = true, string? reason = null,
        OperationalRecordSubmissionView? current = null, bool canOpen = true, string? blocked = null, bool canLink = false, bool canNotCreated = false) =>
        new(_plan, "Approved", available, provider, synthetic, reason,
            [new OperationalRecordAccountView(_account, "svc-syn-01", "gmsaSyn01", current, [], canOpen && available, blocked ?? (available ? null : reason),
                blocked is null ? null : OperationalRecordPresentation.BlockedText(blocked), canLink, canNotCreated)]);

    private static OperationalRecordSubmissionView Submission(string state, string? number = null) =>
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), _account, state, state switch
        {
            "OutcomeUnknown" => "Belirsiz — OR açılmış olabilir",
            "Created" => "OR açıldı",
            _ => state
        }, state == "OutcomeUnknown" ? "Timeout" : null, state == "OutcomeUnknown" ? "Turuncu Hat zamanında cevap vermedi" : null, "Fake", true, number,
            number is null ? null : "900001", null, "Test", "gmsa service account talebi(gmsaSyn01)",
            $"Sentetik gövde\n\nWASAS plan {_plan:D} / hesap {_account:D}", "Sentetik Kullanıcı", _at, _at, state is "OutcomeUnknown" or "Sending",
            [new OperationalRecordEventView("Started", null, "Sending", null, null, null, "Sentetik Kullanıcı", _at)]);

    private static async Task<string> RenderAsync(Api api)
    {
        await using Harness harness = await Harness.StartAsync(api);
        return harness.Html();
    }

    /// <summary>Canned server answers: the preview follows the request like the real server (template, normalization, fingerprint).</summary>
    private sealed class Api
    {
        public PlanOperationalRecordsView? Panel { get; set; }

        public UiProblem? PanelProblem { get; init; }

        public OperationalRecordRequesterView Requester { get; init; } = new("Matched", "Turuncu Hat kullanıcınız birebir eşleşti.", "920001", true);

        public bool SubmitUnknown { get; init; }

        public int Loads { get; private set; }

        public OperationalRecordPreview? LastPreview { get; private set; }

        public List<OperationalRecordSubmitRequest> Submits { get; } = [];

        public List<OperationalRecordLinkRequest> Links { get; } = [];

        public Task<SaCallResult<PlanOperationalRecordsView>> LoadAsync()
        {
            Loads++;
            return Task.FromResult(new SaCallResult<PlanOperationalRecordsView>(PanelProblem is null ? Panel : null, PanelProblem, null, true));
        }

        public Task<SaCallResult<OperationalRecordPreview>> PreviewAsync(Guid accountId, OperationalRecordPreviewRequest request)
        {
            string title = request.ShortDescription?.Trim() ?? "gmsa service account talebi(gmsaSyn01)";
            string body = request.Description?.Trim() ?? "Talep: gMSA oluşturulması.\ngMSA adı: gmsaSyn01";
            string reference = $"WASAS plan {_plan:D} / hesap {accountId:D}";
            bool matched = Requester.Status == "Matched" && Requester.HasHomeLocation;
            string? blocked = !matched ? "requesterNotMatched" : request.Environment is null ? "environmentRequired" : null;
            LastPreview = new OperationalRecordPreview(_plan, accountId, "svc-syn-01", "gmsaSyn01", 1, ["SYN-APP01"], title, body, reference, body + "\n\n" + reference,
                200, 4000, request.Environment, ["Test", "Prod"], Requester, [new("Kayıt türü", "OR (operasyonel talep)", "Sabit")], "Fake", true, blocked is null, blocked,
                blocked == "environmentRequired" ? "Ortamı seçin." : blocked is null ? null : Requester.Label,
                blocked is null ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(title + body + request.Environment))).ToLowerInvariant() : null);
            return Task.FromResult(new SaCallResult<OperationalRecordPreview>(LastPreview, null, null, true));
        }

        public Task<SaCallResult<OperationalRecordSubmitResult>> SubmitAsync(Guid accountId, OperationalRecordSubmitRequest request)
        {
            Submits.Add(request);
            return Task.FromResult(SubmitUnknown
                ? new SaCallResult<OperationalRecordSubmitResult>(null, UiProblemFactory.NetworkFailure(), null, true)
                : new SaCallResult<OperationalRecordSubmitResult>(new OperationalRecordSubmitResult(Submission("Created", "OR-123456"), false), null, null, true));
        }

        public Task<SaCallResult<OperationalRecordCandidate>> CandidateAsync(Guid accountId, OperationalRecordCandidateRequest request) =>
            Task.FromResult(new SaCallResult<OperationalRecordCandidate>(new OperationalRecordCandidate(request.Number!, "Found", "Turuncu Hat'ta bulundu", null,
                "Synthetic OR record", false, null, "Fake", true, true), null, null, true));

        public Task<SaCallResult<PlanOperationalRecordsView>> LinkAsync(Guid accountId, OperationalRecordLinkRequest request)
        {
            Links.Add(request);
            return Task.FromResult(new SaCallResult<PlanOperationalRecordsView>(Panel, null, null, true));
        }

        public Task<SaCallResult<PlanOperationalRecordsView>> NotCreatedAsync(Guid accountId, OperationalRecordNotCreatedRequest request) =>
            Task.FromResult(new SaCallResult<PlanOperationalRecordsView>(Panel, null, null, true));

        public Task<SaCallResult<OperationalRecordTrackingView>> TrackingAsync(Guid accountId) =>
            Task.FromResult(new SaCallResult<OperationalRecordTrackingView>(new OperationalRecordTrackingView("OR-123456", "Found", "Turuncu Hat'ta bulundu", null, null,
                null, null, false, null, null, null, false, null, null, "Fake", true, _at), null, null, true));

        public Dictionary<string, object?> Parameters() => new()
        {
            [nameof(SaPlanOperationalRecords.PlanId)] = _plan,
            [nameof(SaPlanOperationalRecords.PlanStatus)] = "Approved",
            [nameof(SaPlanOperationalRecords.Load)] = (Func<Task<SaCallResult<PlanOperationalRecordsView>>>)LoadAsync,
            [nameof(SaPlanOperationalRecords.Preview)] = (Func<Guid, OperationalRecordPreviewRequest, Task<SaCallResult<OperationalRecordPreview>>>)PreviewAsync,
            [nameof(SaPlanOperationalRecords.Submit)] = (Func<Guid, OperationalRecordSubmitRequest, Task<SaCallResult<OperationalRecordSubmitResult>>>)SubmitAsync,
            [nameof(SaPlanOperationalRecords.Candidate)] = (Func<Guid, OperationalRecordCandidateRequest, Task<SaCallResult<OperationalRecordCandidate>>>)CandidateAsync,
            [nameof(SaPlanOperationalRecords.Link)] = (Func<Guid, OperationalRecordLinkRequest, Task<SaCallResult<PlanOperationalRecordsView>>>)LinkAsync,
            [nameof(SaPlanOperationalRecords.NotCreated)] = (Func<Guid, OperationalRecordNotCreatedRequest, Task<SaCallResult<PlanOperationalRecordsView>>>)NotCreatedAsync,
            [nameof(SaPlanOperationalRecords.Tracking)] = (Func<Guid, Task<SaCallResult<OperationalRecordTrackingView>>>)TrackingAsync
        };
    }

    /// <summary>Renders the panel inside a host that keeps a reference to it, so a test can drive it and read the result.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly HtmlRootComponent _root;
        private readonly HostState _state;

        private Harness(ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root, HostState state)
            => (_services, _renderer, _root, _state) = (services, renderer, root, state);

        public SaPlanOperationalRecords Component => (SaPlanOperationalRecords)_state.Component!;

        public static async Task<Harness> StartAsync(Api api)
        {
            HostState state = new() { Parameters = api.Parameters() };
            ServiceCollection registrations = new();
            registrations.AddLogging();
            registrations.AddMudServices();
            registrations.AddSingleton(Substitute.For<IJSRuntime>());
            registrations.AddSingleton(state);
            ServiceProvider services = registrations.BuildServiceProvider();
            HtmlRenderer renderer = new(services, services.GetRequiredService<ILoggerFactory>());
            HtmlRootComponent root = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<Host>());
            return new Harness(services, renderer, root, state);
        }

        public async Task RunAsync(Func<SaPlanOperationalRecords, Task> action) =>
            await _renderer.Dispatcher.InvokeAsync(async () =>
            {
                await action(Component);
                _state.Host!.Refresh();
            });

        public Task RunAsync(Action<SaPlanOperationalRecords> action) => RunAsync(component =>
        {
            action(component);
            return Task.CompletedTask;
        });

        public string Html() => WebUtility.HtmlDecode(Regex.Replace(_renderer.Dispatcher.InvokeAsync(() => _root.ToHtmlString()).GetAwaiter().GetResult(), " b-[a-z0-9]{10}", string.Empty));

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class HostState
    {
        public Dictionary<string, object?> Parameters { get; init; } = [];

        public object? Component { get; set; }

        public Host? Host { get; set; }
    }

    private sealed class Host : ComponentBase
    {
        [Inject] public HostState State { get; set; } = default!;

        protected override void OnInitialized() => State.Host = this;

        public void Refresh() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SaPlanOperationalRecords>(0);
            builder.AddMultipleAttributes(1, State.Parameters.Where(p => p.Value is not null).Select(p => new KeyValuePair<string, object>(p.Key, p.Value!)));
            builder.AddComponentReferenceCapture(2, component => State.Component = component);
            builder.CloseComponent();
        }
    }
}
