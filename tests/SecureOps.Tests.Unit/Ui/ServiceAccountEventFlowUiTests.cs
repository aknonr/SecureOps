using System.Net;
using System.Text.Json;
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
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// OLAY-3 "Bir gelişme mi oldu?" UI (synthetic data only): the draft sends only what the event table allows and never a verification
/// field; the flow shows what will and will not be recorded, keeps one idempotency key per opening across retries, separates an
/// unknown outcome from a refusal, hides itself when migration 036 is missing, and the close hints come from the server's list.
/// </summary>
public sealed class ServiceAccountEventFlowUiTests
{
    private static readonly Guid _account = Guid.Parse("5a1e0000-0000-4000-8000-0000000000e1");
    private static readonly Guid _passwordRequest = Guid.Parse("5a1e0000-0000-4000-8000-0000000000e2");
    private static readonly Guid _reviewRequest = Guid.Parse("5a1e0000-0000-4000-8000-0000000000e3");
    private static readonly Guid _action = Guid.Parse("5a1e0000-0000-4000-8000-0000000000e4");
    private static readonly AccountPermissions _responsible = new(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible);
    private static readonly AccountPermissions _participant = new(false, false, false, false, false, true, ServiceAccountAccessBasis.Participant, [_passwordRequest]);
    private static readonly AccountPermissions _viewer = new(false, false, false, false, false, false);
    private static readonly AccountPermissions _verifierOnly = new(false, false, false, true, false, false);

    [Fact]
    public void PasswordChanged_SendsOnlyAllowedBlocks_AndNeverAVerificationField()
    {
        SaEventDraftModel model = new()
        {
            Kind = "PasswordChanged",
            OccurredOn = new DateTime(2026, 10, 1),
            RequestId = _passwordRequest,
            PerformerTeamId = Guid.NewGuid(),
            OwnerTeamId = Guid.NewGuid(),
            OwnerReason = "yazıldı ama bu olay almaz",
            CreateRequest = true
        };
        model.References.Add(new SaEventDraftModel.ReferenceRow { Type = "OCO", Number = " OCO-00001 " });
        model.References.Add(new SaEventDraftModel.ReferenceRow { Type = "OR", Number = "  " });

        EventDraft draft = model.Build(Detail(_responsible));

        draft.EventKind.Should().Be("PasswordChanged");
        draft.DatePrecision.Should().Be("DateOnly");
        draft.OccurredOn.Should().Be(new DateOnly(2026, 10, 1));
        draft.AccountVersion.Should().Be("AAAAAAAAAAE=");
        draft.RequestId.Should().Be(_passwordRequest);
        draft.Ownership.Should().BeNull("a password change carries no ownership proposal");
        draft.CreateRequest.Should().BeNull();
        draft.CloseRequest.Should().BeNull("closing is off by default");
        draft.Communication.Should().BeNull("the mail is optional for this event and was not asked for");
        draft.References.Should().ContainSingle().Which.Should().Be(new SaExternalRef("OCO", "OCO-00001"));
        draft.IdempotencyKey.Should().BeNull("the preview draft carries no key");

        string json = JsonSerializer.Serialize(draft, ApiResponseReader.JsonOptions);
        json.Should().NotContainAny("\"result\"", "\"recordKind\"", "\"actionType\"", "verified", "Verified");
    }

    [Fact]
    public void FixedMailDirection_IsNotSent_AndAChosenOneIs()
    {
        SaEventDraftModel reply = new() { Kind = "OwnerReplied", DateUnknown = true, Summary = " kısa özet " };
        EventDraft replied = reply.Build(Detail(_responsible));
        replied.Communication.Should().NotBeNull("a reply always records the mail");
        replied.Communication!.Direction.Should().BeNull("the event table fixes it");
        replied.Communication.Kind.Should().BeNull();
        replied.Communication.Summary.Should().Be("kısa özet");
        replied.OccurredOn.Should().BeNull("an unknown date is never invented");
        replied.DatePrecision.Should().Be("Unknown");
        replied.Action.Should().BeNull("a reply writes no action");

        SaEventDraftModel moved = new() { Kind = "ServiceMoved", OccurredOn = DateTime.Today, Direction = "Outgoing", RequestId = _reviewRequest, CreateRequest = true };
        EventDraft movedDraft = moved.Build(Detail(_responsible));
        movedDraft.Communication!.Direction.Should().Be("Outgoing");
        movedDraft.Communication.Kind.Should().Be("Information");
        movedDraft.RequestId.Should().BeNull("a moved service links no request");
        movedDraft.CreateRequest.Should().NotBeNull("the person asked for the review request");
    }

    [Fact]
    public void Participant_NeverSendsCloseOrNewRequest()
    {
        SaEventDraftModel model = new() { Kind = "PasswordChanged", OccurredOn = DateTime.Today, RequestId = _passwordRequest, CloseRequest = true };

        EventDraft draft = model.Build(Detail(_participant));

        draft.CloseRequest.Should().BeNull("closing needs the responsible basis");
        draft.RequestId.Should().Be(_passwordRequest);
    }

    [Fact]
    public void Incomplete_SaysWhatIsMissing_WithoutDecidingRules()
    {
        new SaEventDraftModel().Incomplete(_responsible).Should().Contain("ne olduğunu");
        new SaEventDraftModel { Kind = "PasswordChanged" }.Incomplete(_responsible).Should().Contain("Tarih bilinmiyor");
        new SaEventDraftModel { Kind = "PasswordChanged", DateUnknown = true }.Incomplete(_participant).Should().Contain("talep seçin");
        new SaEventDraftModel { Kind = "OwnerChanged", DateUnknown = true }.Incomplete(_responsible).Should().Contain("sahip");
        new SaEventDraftModel { Kind = "OwnerChanged", DateUnknown = true, OwnerTeamId = Guid.NewGuid() }.Incomplete(_responsible).Should().Contain("gerekçe");
        new SaEventDraftModel { Kind = "PasswordChanged", DateUnknown = true }.Incomplete(_responsible).Should().BeNull();
    }

    [Fact]
    public void RequestsAndCards_FollowTheServerTableAndPermissions()
    {
        ServiceAccountEventText.EventForRequest("PasswordChange").Should().Be("PasswordChanged");
        ServiceAccountEventText.EventForRequest("Deletion").Should().Be("AccountDeleted");
        ServiceAccountEventText.EventForRequest("GmsaHandover").Should().Be("GmsaConverted");
        ServiceAccountEventText.EventForRequest("Review").Should().Be("OwnerReplied");

        AccountDetail detail = Detail(_responsible);
        ServiceAccountEventText.LinkableRequests(detail, "PasswordChanged").Select(r => r.Id).Should().Equal(_passwordRequest);
        ServiceAccountEventText.LinkableRequests(detail, "OwnerReplied").Should().HaveCount(2, "a reply links any open request");
        ServiceAccountEventText.LinkableRequests(detail, "ServiceMoved").Should().BeEmpty();
        ServiceAccountEventText.LinkableRequests(Detail(_participant), "OwnerReplied").Select(r => r.Id).Should().Equal(_passwordRequest);

        ServiceAccountEventText.CardsFor(_responsible).Should().HaveCount(7);
        ServiceAccountEventText.CardsFor(_participant).Select(c => c.Kind).Should().BeEquivalentTo("OwnerReplied", "PasswordChanged", "AccountDeleted", "GmsaConverted");
        ServiceAccountEventText.WhyNoFlow(_viewer).Should().Contain("sorumlu ekibe iletin");
        ServiceAccountEventText.WhyNoFlow(_participant).Should().BeNull();
    }

    [Fact]
    public void Wording_SaysReportedNotVerified_AndCoversEveryServerCode()
    {
        ServiceAccountEventText.Cards.Where(c => c.Kind is "PasswordChanged" or "AccountDeleted" or "GmsaConverted")
            .Should().OnlyContain(c => c.Records.Contains("Bildirildi — doğrulanmadı"));
        foreach (string code in new[] { "Verification", "AccountClosure", "RequestClose", "OwnershipDecision", "PlanChecklist" })
        {
            ServiceAccountEventText.NotRecorded(code).Should().NotBe(code);
        }

        foreach (string code in new[] { "OrMissingForDeletion", "DateUnknown", "MailDuplicate", "ReplyDateEarlier", "ReplyDateUnknown" })
        {
            ServiceAccountEventText.Warning(code).Should().NotBe(code);
        }

        foreach (string code in new[] { "Verify", "CloseRequest", "OpenPlan", "UsageTab", "OwnershipDecision" })
        {
            ServiceAccountEventText.FollowUp(code).Should().NotBe(code);
        }

        ServiceAccountEventText.FieldValue(new EventField("result", "Performed"), Detail(_responsible), []).Should().Be("Bildirildi — doğrulanmadı");
        ServiceAccountEventText.FieldValue(new EventField("actualOn", null), Detail(_responsible), []).Should().Be("Tarih bilinmiyor");
    }

    [Fact]
    public void EventProblems_MapToHonestTurkishStates()
    {
        UiProblem stale = UiProblemFactory.FromResponse(409, new ProblemDetailsPayload { Code = "ServiceAccountEventStale" });
        stale.Kind.Should().Be(UiProblemKind.Conflict);
        stale.Explanation.Should().Contain("hiçbir kayıt yapılmadı");
        UiProblemFactory.FromResponse(422, new ProblemDetailsPayload { Code = "ServiceAccountEventCloseNotAllowed" }).Explanation.Should().Contain("Hiçbir kayıt yapılmadı");
        UiProblemFactory.FromResponse(409, new ProblemDetailsPayload { Code = "ServiceAccountEventIdempotencyKeyReused" }).Title.Should().Contain("başka içerikle");
        UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = "ServiceAccountEventsNotInstalled" }).Explanation.Should().Contain("036");
        ServiceAccountProblems.FieldText("requestTypeMismatch").Should().Contain("eşleşmiyor");
        ServiceAccountProblems.FieldText("blockNotAllowed:ownership").Should().Contain("bu bilgiyi almıyor");
    }

    [Fact]
    public void OnlyANoAnswerIsAnUnknownOutcome()
    {
        new SaCallResult<EventCommitResult>(null, UiProblemFactory.NetworkFailure(), null, true).OutcomeUnknown.Should().BeTrue();
        new SaCallResult<EventCommitResult>(null, UiProblemFactory.TimedOut(), null, true).OutcomeUnknown.Should().BeTrue();
        new SaCallResult<EventCommitResult>(null, null, null, true).OutcomeUnknown.Should().BeTrue("a superseded call may have reached the server");
        new SaCallResult<EventCommitResult>(null, UiProblemFactory.FromResponse(409, new ProblemDetailsPayload { Code = "ServiceAccountEventStale" }), null, true)
            .OutcomeUnknown.Should().BeFalse("the server answered: nothing was written");
        new SaCallResult<EventCommitResult>(null, null, null, false).OutcomeUnknown.Should().BeFalse("nothing was sent");
    }

    [Fact]
    public async Task NotInstalled_HidesTheFlowWithAShortHonestText()
    {
        await using Harness<SaEventFlow> harness = await Harness<SaEventFlow>.StartAsync(FlowParameters(Detail(_responsible), available: false, new FakeEvents()));
        string html = harness.Html();

        html.Should().Contain("036").And.Contain("kapalı");
        html.Should().NotContain("Bir gelişme mi oldu?</span></button>");
    }

    [Fact]
    public async Task Viewer_SeesWhyInsteadOfAButton_ResponsibleSeesTheButton()
    {
        await using (Harness<SaEventFlow> viewer = await Harness<SaEventFlow>.StartAsync(FlowParameters(Detail(_viewer), true, new FakeEvents())))
        {
            viewer.Html().Should().Contain("kayıt yetkiniz yok").And.NotContain("Bir gelişme mi oldu?</span></button>");
        }

        await using Harness<SaEventFlow> responsible = await Harness<SaEventFlow>.StartAsync(FlowParameters(Detail(_responsible), true, new FakeEvents()));
        responsible.Html().Should().Contain("Bir gelişme mi oldu?</span></button>");
    }

    [Fact]
    public async Task StepOne_IsARadioGroupWithWhatEachEventRecords_AndAStepIndicator()
    {
        await using Harness<SaEventFlow> harness = await Harness<SaEventFlow>.StartAsync(FlowParameters(Detail(_responsible), true, new FakeEvents()));
        await harness.RunAsync(flow => flow.Open(null));
        string html = harness.Html();

        Regex.Matches(html, "type=\"radio\" name=\"sa-event-kind\"").Count.Should().Be(7);
        html.Should().Contain("Ürün ne kaydeder:").And.Contain("aria-current=\"step\"").And.Contain("Adım 1 / 3 · Ne oldu?");
        html.Should().Contain("aria-disabled=\"true\"").And.Contain("Önce ne olduğunu seçin.");
    }

    [Fact]
    public async Task PreSelectedStart_OpensDetailsWithTheRequestAndTheReportedLabel()
    {
        Dictionary<string, object?> parameters = FlowParameters(Detail(_responsible), true, new FakeEvents());
        parameters["Start"] = new SaEventStart("PasswordChanged", _passwordRequest);
        parameters["StartSignal"] = 1;
        await using Harness<SaEventFlow> harness = await Harness<SaEventFlow>.StartAsync(parameters);
        string html = harness.Html();

        html.Should().Contain("Adım 2 / 3 · Ayrıntılar: Parola değişti (kişi bildirdi)");
        html.Should().Contain("Bildirildi — doğrulanmadı").And.Contain("Yanıt mailini yapıştır").And.Contain("saklanmaz");
        harness.Component.Draft.RequestId.Should().Be(_passwordRequest);
    }

    [Fact]
    public async Task Preview_ListsWhatIsAndIsNotRecorded_AndADeniedItemKeepsSaveDisabled()
    {
        FakeEvents events = new() { NextPreview = Preview(canCommit: false) };
        await using Harness<SaEventFlow> harness = await OpenAtDetailsAsync(events);
        await harness.RunAsync(flow => flow.PreviewAsync());
        string html = harness.Html();

        html.Should().Contain("Şunlar kaydedilecek").And.Contain("Kaydedilmeyecek");
        html.Should().Contain("Doğrulama yapılmaz").And.Contain("Hesap kapatılmaz").And.Contain("Talep kapatılmaz");
        html.Should().Contain("Bildirildi — doğrulanmadı").And.Contain("Yapılamaz: yalnız ekibinize atanmış talep");
        html.Should().Contain("Tümü ya da hiçbiri kaydedilir");
        Regex.IsMatch(html, "aria-disabled=\"true\"[^>]*aria-describedby=\"sa-event-why-3\"").Should().BeTrue("Kaydet stays focusable but disabled with its reason");

        await harness.RunAsync(flow => flow.CommitAsync());
        events.Commits.Should().BeEmpty("a denied item is never sent");
    }

    [Fact]
    public async Task UnknownOutcome_RetriesWithTheSameKeyAndDigest_ThenShowsWhatRemains()
    {
        FakeEvents events = new() { NextPreview = Preview(canCommit: true) };
        events.CommitAnswers.Enqueue(new SaCallResult<EventCommitResult>(null, UiProblemFactory.TimedOut(), null, true));
        events.CommitAnswers.Enqueue(new SaCallResult<EventCommitResult>(new EventCommitResult(Detail(_responsible), Guid.NewGuid(),
            [new EventCreatedRecord("Action", "Action", Guid.NewGuid(), "Create")], false), null, null, true));
        await using Harness<SaEventFlow> harness = await OpenAtDetailsAsync(events);
        await harness.RunAsync(flow => flow.PreviewAsync());

        await harness.RunAsync(flow => flow.CommitAsync());
        harness.Html().Should().Contain("Sonuç bilinmiyor").And.Contain("Yeniden göndermek güvenlidir").And.Contain("Hesabı yenile");

        await harness.RunAsync(flow => flow.CommitAsync());
        events.Commits.Should().HaveCount(2);
        events.Commits[0].IdempotencyKey.Should().NotBeNull().And.Be(events.Commits[1].IdempotencyKey);
        events.Commits[0].PreviewDigest.Should().Be("digest-1").And.Be(events.Commits[1].PreviewDigest);
        string html = harness.Html();
        html.Should().Contain("Kaydedildi").And.Contain("İşlem bildirimi oluşturulur · Bildirildi — doğrulanmadı");
        html.Should().Contain("Hâlâ yapılacaklar (yapılmadı)").And.Contain("doğrulama yetkisi olan kişi");
    }

    [Fact]
    public async Task EachOpening_GetsANewKey()
    {
        FakeEvents events = new() { NextPreview = Preview(canCommit: true) };
        events.CommitAnswers.Enqueue(new SaCallResult<EventCommitResult>(null, UiProblemFactory.NetworkFailure(), null, true));
        events.CommitAnswers.Enqueue(new SaCallResult<EventCommitResult>(null, UiProblemFactory.NetworkFailure(), null, true));
        await using Harness<SaEventFlow> harness = await OpenAtDetailsAsync(events);
        await harness.RunAsync(flow => flow.PreviewAsync());
        await harness.RunAsync(flow => flow.CommitAsync());

        await harness.RunAsync(flow => flow.Open(new SaEventStart("PasswordChanged", null)));
        harness.Component.Draft.DateUnknown = true;
        await harness.RunAsync(flow => flow.PreviewAsync());
        await harness.RunAsync(flow => flow.CommitAsync());

        events.Commits.Select(c => c.IdempotencyKey).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Stale_AsksToRefreshThePreview_AndCloseRefusal_ListsTheServersGaps()
    {
        FakeEvents events = new() { NextPreview = Preview(canCommit: true) };
        events.CommitAnswers.Enqueue(new SaCallResult<EventCommitResult>(null,
            UiProblemFactory.FromResponse(409, new ProblemDetailsPayload { Code = "ServiceAccountEventStale" }), null, true));
        string current = JsonSerializer.Serialize(new CloseReadiness(false, "Open", "Completed",
            [new CloseMissing("NoMatchingPerformedAction", "ReportAction", ExpectedActionType: "PasswordChange")]), ApiResponseReader.JsonOptions);
        events.CommitAnswers.Enqueue(new SaCallResult<EventCommitResult>(null,
            UiProblemFactory.FromResponse(422, new ProblemDetailsPayload { Code = "ServiceAccountEventCloseNotAllowed" }), current, true));
        await using Harness<SaEventFlow> harness = await OpenAtDetailsAsync(events);
        await harness.RunAsync(flow => flow.PreviewAsync());

        await harness.RunAsync(flow => flow.CommitAsync());
        harness.Html().Should().Contain("Hesap değişti; önizlemeyi yenileyin.").And.Contain("Önizlemeyi yenile").And.NotContain("Sonuç bilinmiyor");

        await harness.RunAsync(flow => flow.CommitAsync());
        harness.Html().Should().Contain("Talep bu kayıtlarla kapatılamıyor").And.Contain("gerçekleşmiş bir \"Parola değişimi\" bildirimi gerekir")
            .And.Contain("Talebi kapatmadan önizlemeyi yenile");
    }

    [Fact]
    public async Task MailHints_ClearThePastedText_AndTheDraftNeverCarriesIt()
    {
        const string pasted = "SENTETIK-MAIL-ISARETI parola değişti OCO-00042 01.10.2026";
        FakeEvents events = new()
        {
            NextHints = new MailHints("olay-1", [new MailHintEvent("PasswordChanged", "Matched:test")], [new MailHintReference("OCO", "OCO-00042", "r")],
                [new MailHintDate(new DateOnly(2026, 10, 1), "AssumedYear", "d")], [])
        };
        await using Harness<SaEventFlow> harness = await OpenAtDetailsAsync(events);
        harness.Component.MailText = pasted;

        await harness.RunAsync(flow => flow.HintAsync());

        events.HintTexts.Should().ContainSingle().Which.Should().Be(pasted);
        harness.Component.MailText.Should().BeNull("the pasted text leaves the form once the hints arrive");
        string html = harness.Html();
        html.Should().Contain("OCO-00042").And.Contain("yıl yazmıyordu").And.Contain("hiçbiri kendiliğinden seçilmedi").And.NotContain("SENTETIK-MAIL-ISARETI");
        harness.Component.Draft.References.Should().BeEmpty("a hint is only applied when the person picks it");

        harness.Component.Draft.AddReference("OCO", "OCO-00042");
        await harness.RunAsync(flow => flow.PreviewAsync());
        JsonSerializer.Serialize(events.Previews.Single(), ApiResponseReader.JsonOptions).Should().NotContain("SENTETIK-MAIL-ISARETI");
    }

    [Fact]
    public void NextStep_ReportOpensTheOverdueRequestsEvent_OnlyForThatStep()
    {
        AccountDetail detail = Detail(_responsible) with
        {
            Requests = [Request(_passwordRequest, "PasswordChange", overdue: true), Request(_reviewRequest, "Review", overdue: false)]
        };
        IReadOnlyList<SaNextStep> steps = ServiceAccountNextStep.Compute(detail);

        ServiceAccountNextStep.EventFor(steps.Single(s => s.Code == "requests-overdue"), detail).Should().Be(new SaEventStart("PasswordChanged", _passwordRequest));
        steps.Where(s => s.Code != "requests-overdue").Should().OnlyContain(s => ServiceAccountNextStep.EventFor(s, detail) == null);
    }

    [Fact]
    public async Task NextStepCard_ShowsBildir_OnlyWhenTheFlowIsInstalled()
    {
        AccountDetail detail = Detail(_responsible) with { Requests = [Request(_passwordRequest, "PasswordChange", overdue: true)] };
        Dictionary<string, object?> parameters = new() { ["Detail"] = detail, ["EventAvailable"] = true };
        await using (Harness<SaNextStepCard> on = await Harness<SaNextStepCard>.StartAsync(parameters))
        {
            on.Html().Should().Contain(">Bildir</span>").And.Contain("Plan bitişi geçen işleri güncelleyin: gelişmeyi bildir");
        }

        parameters["EventAvailable"] = false;
        await using Harness<SaNextStepCard> off = await Harness<SaNextStepCard>.StartAsync(parameters);
        off.Html().Should().NotContain(">Bildir</span>");
    }

    [Fact]
    public async Task RequestsPanel_CloseButtonIsAriaDisabledWithTheServersReasons()
    {
        AccountDetail detail = Detail(_responsible);
        CloseReadiness readiness = new(false, "Open", "Completed",
            [new CloseMissing("NoMatchingPerformedAction", "ReportAction", ExpectedActionType: "PasswordChange"), new CloseMissing("OwnershipNotConfirmed", "ConfirmOwnership")]);
        Dictionary<string, object?> parameters = new()
        {
            ["Detail"] = detail,
            ["Readiness"] = new Dictionary<Guid, CloseReadiness> { [_passwordRequest] = readiness },
            ["EventAvailable"] = true
        };
        await using Harness<SaRequestsPanel> harness = await Harness<SaRequestsPanel>.StartAsync(parameters);
        harness.Html().Should().Contain("Henüz tamamlandı olarak kapatılamaz (2 eksik)").And.Contain("Gelişme bildir");

        await harness.RunAsync(panel => panel.Select(detail.Requests.First(r => r.Id == _passwordRequest)));
        string html = harness.Html();

        html.Should().Contain("Talep bu sonuçla şu an kapatılamaz").And.Contain("gerçekleşmiş bir \"Parola değişimi\" bildirimi gerekir")
            .And.Contain("Sahiplik sekmesi");
        Regex.IsMatch(html, "aria-disabled=\"true\"[^>]*aria-describedby=\"sa-close-why-list\"").Should().BeTrue();
    }

    [Fact]
    public async Task RequestsPanel_WithoutAnswer_SaysTheServerDecides()
    {
        AccountDetail detail = Detail(_responsible);
        await using Harness<SaRequestsPanel> harness = await Harness<SaRequestsPanel>.StartAsync(new() { ["Detail"] = detail });
        await harness.RunAsync(panel => panel.Select(detail.Requests[0]));

        harness.Html().Should().Contain("Kapatma koşulları okunamadı").And.Contain("kararı sunucu verir").And.NotContain("Gelişme bildir");
    }

    [Fact]
    public async Task ActionsPanel_SaysWhatTheClosureCandidateLacks_AndWhoVerifies()
    {
        AccountDetail detail = Detail(_participant) with { Actions = [Action()] };
        CloseReadiness readiness = new(false, "Open", "Completed",
            [new CloseMissing("VerifiedClosureRequired", "VerifyAction", ["NotVerified", "EvidenceRequired"], RelatedActionId: _action)]);
        await using Harness<SaActionsPanel> harness = await Harness<SaActionsPanel>.StartAsync(new()
        {
            ["Detail"] = detail,
            ["Readiness"] = new Dictionary<Guid, CloseReadiness> { [_passwordRequest] = readiness }
        });
        string html = harness.Html();

        html.Should().Contain("Talebin kapanması için bu kayıtta eksik: İşlem doğrulanmadı.").And.Contain("kanıt notu veya kanıt dosyası");
        html.Should().Contain("Doğrulamayı doğrulama yetkisi olan kişi yapar.");
    }

    [Fact]
    public void Guide_RoleNotes_FollowTheServerPermissions()
    {
        ServiceAccountGuides.DetailRole(_responsible, true).Should().Contain("Bir gelişme mi oldu?").And.Contain("doğrulama ayrıdır").And.Contain("kanıt gerekir");
        ServiceAccountGuides.DetailRole(_responsible with { Verify = false }, false).Should().Contain("henüz açık değil").And.NotContain("kanıt gerekir");
        ServiceAccountGuides.DetailRole(_participant, true).Should().Contain("Size atanmış talep");
        ServiceAccountGuides.DetailRole(_verifierOnly, true).Should().Contain("bildirim kaydeder, doğrulamaz");
        ServiceAccountGuides.DetailRole(_viewer, true).Should().Contain("sorumlu ekibe iletin");
    }

    [Fact]
    public void Css_FlowHasFocusRingsAndNoAnimation()
    {
        string css = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaEventFlow.razor.css"));
        css.Should().MatchRegex(@"\.sa-event-heading:focus-visible\s*\{[^}]*outline:\s*2px solid");
        css.Should().MatchRegex(@"\.sa-event-card input:focus-visible\s*\{[^}]*outline:\s*2px solid");
        css.Should().NotMatchRegex(@"animation\s*:|transition\s*:|@keyframes", "the flow has no motion to reduce");
        css.Should().NotMatchRegex(@"#[0-9a-fA-F]{3,6}\b", "colours come from theme tokens (light and dark)");
    }

    private static async Task<Harness<SaEventFlow>> OpenAtDetailsAsync(FakeEvents events)
    {
        Harness<SaEventFlow> harness = await Harness<SaEventFlow>.StartAsync(FlowParameters(Detail(_responsible), true, events));
        await harness.RunAsync(flow => flow.Open(new SaEventStart("PasswordChanged", _passwordRequest)));
        harness.Component.Draft.OccurredOn = new DateTime(2026, 10, 1);
        return harness;
    }

    private static EventPreview Preview(bool canCommit) => new("digest-1",
        [
            new EventPreviewItem("Action", "Create", null, "İşlem bildirimi — Bildirildi, doğrulanmadı", [new EventField("result", "Performed"), new EventField("actualOn", "2026-10-01")], true),
            new EventPreviewItem("RequestClose", "Update", _passwordRequest, "Talep kapatılır (Tamamlandı)", [], canCommit, canCommit ? null : "NotParticipantRequest")
        ],
        ["Verification", "AccountClosure", "RequestClose"], [], null, [new EventFollowUp("Verify"), new EventFollowUp("CloseRequest")], canCommit);

    private static Dictionary<string, object?> FlowParameters(AccountDetail detail, bool available, FakeEvents events) => new()
    {
        ["Detail"] = detail,
        ["Available"] = available,
        ["Preview"] = (Func<EventDraft, Task<SaCallResult<EventPreview>>>)events.PreviewAsync,
        ["Commit"] = (Func<EventDraft, Task<SaCallResult<EventCommitResult>>>)events.CommitAsync,
        ["Hints"] = (Func<string, Task<SaCallResult<MailHints>>>)events.HintsAsync
    };

    private static AccountDetail Detail(AccountPermissions permissions)
    {
        AccountSummaryView summary = new(_account, "svc_synevent", "SYN", null, "Confirmed", null, new SaRef(Guid.NewGuid(), "SYN Ekip"), null, null, "Active", null, null, null,
            2, null, [], null, "AAAAAAAAAAE=");
        return new AccountDetail(summary, [], [Request(_passwordRequest, "PasswordChange", false), Request(_reviewRequest, "Review", false)], [], [], [], [], [], [], [], [], [],
            permissions, [], null, [], 0);
    }

    private static RequestView Request(Guid id, string type, bool overdue) =>
        new(id, _account, "svc_synevent", type, type == "PasswordChange" ? "Parola değişikliği" : "İnceleme", "Open", null, new SaRef(Guid.NewGuid(), "SYN Hedef"), null, null,
            null, overdue ? new DateOnly(2026, 9, 1) : null, null, null, null, null, null, overdue, false, [], null, null, "AAAAAAAAAAI=");

    private static ActionView Action() =>
        new(_action, _account, _passwordRequest, "Deletion", "Silme", "Performed", "Gerçekleşti", "Closure", new DateOnly(2026, 10, 1), null, "Day", null, null, null, null,
            null, null, false, false, null, null, [], null, "AAAAAAAAAAM=");

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Synthetic event endpoints: records every draft and answers from queues.</summary>
    private sealed class FakeEvents
    {
        public EventPreview? NextPreview { get; init; }

        public MailHints? NextHints { get; init; }

        public Queue<SaCallResult<EventCommitResult>> CommitAnswers { get; } = new();

        public List<EventDraft> Previews { get; } = [];

        public List<EventDraft> Commits { get; } = [];

        public List<string> HintTexts { get; } = [];

        public Task<SaCallResult<EventPreview>> PreviewAsync(EventDraft draft)
        {
            Previews.Add(draft);
            return Task.FromResult(new SaCallResult<EventPreview>(NextPreview, NextPreview is null ? UiProblemFactory.NetworkFailure() : null, null, true));
        }

        public Task<SaCallResult<EventCommitResult>> CommitAsync(EventDraft draft)
        {
            Commits.Add(draft);
            return Task.FromResult(CommitAnswers.Count > 0 ? CommitAnswers.Dequeue() : new SaCallResult<EventCommitResult>(null, null, null, false));
        }

        public Task<SaCallResult<MailHints>> HintsAsync(string text)
        {
            HintTexts.Add(text);
            return Task.FromResult(new SaCallResult<MailHints>(NextHints, null, null, true));
        }
    }

    /// <summary>Renders one component inside a host that keeps a reference to it, so a test can drive it and read the result.</summary>
    private sealed class Harness<TComponent> : IAsyncDisposable where TComponent : IComponent
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly HtmlRootComponent _root;
        private readonly HostState _state;

        private Harness(ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root, HostState state)
            => (_services, _renderer, _root, _state) = (services, renderer, root, state);

        public TComponent Component => (TComponent)_state.Component!;

        public static async Task<Harness<TComponent>> StartAsync(Dictionary<string, object?> parameters)
        {
            HostState state = new() { Type = typeof(TComponent), Parameters = parameters };
            ServiceCollection registrations = new();
            registrations.AddLogging();
            registrations.AddMudServices();
            registrations.AddSingleton(Substitute.For<IJSRuntime>());
            registrations.AddSingleton(state);
            registrations.AddSingleton<NavigationManager>(new Navigation());
            registrations.AddSingleton(new ServiceAccountApiClient(new HttpClient(new NoNetwork()) { BaseAddress = new Uri("http://localhost/") }, new FakeApiSessionContext()));
            ServiceProvider services = registrations.BuildServiceProvider();
            HtmlRenderer renderer = new(services, services.GetRequiredService<ILoggerFactory>());
            HtmlRootComponent root = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<Host>());
            return new Harness<TComponent>(services, renderer, root, state);
        }

        /// <summary>Runs an interaction on the renderer's thread, then re-renders (as a UI event would).</summary>
        public async Task RunAsync(Func<TComponent, Task> action) =>
            await _renderer.Dispatcher.InvokeAsync(async () =>
            {
                await action(Component);
                _state.Host!.Refresh();
            });

        public Task RunAsync(Action<TComponent> action) => RunAsync(component =>
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
        public Type Type { get; init; } = default!;

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
            builder.OpenComponent(0, State.Type);
            builder.AddMultipleAttributes(1, State.Parameters.Where(p => p.Value is not null).Select(p => new KeyValuePair<string, object>(p.Key, p.Value!)));
            builder.AddComponentReferenceCapture(2, component => State.Component = component);
            builder.CloseComponent();
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/service-accounts/" + _account);

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
