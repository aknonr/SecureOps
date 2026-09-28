using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using NSubstitute;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Pages;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the Jira-only submission states: ready, submitting, created (saved key), acknowledged,
/// rejected and uncertain must never be presented as one another.
/// </summary>
public sealed class JiraSubmissionTests
{
    private static readonly OperationalRecordView.Actions _createAllowed = new(true, true, false, null);

    // ------------------------------------------------------------------ phase resolution

    [Fact]
    public void Ready_RequiresServerPreview_WorkflowCreate_AndCapability()
    {
        OperationalRecordResponse record = Record();
        JiraPreviewResponse preview = Preview();

        JiraSubmissionView.PhaseOf(record, preview, _createAllowed, canCreate: true, false, null).Should().Be(JiraSubmissionView.Phase.Ready);
        JiraSubmissionView.PhaseOf(record, null, _createAllowed, canCreate: true, false, null).Should().Be(JiraSubmissionView.Phase.Unavailable);
        JiraSubmissionView.PhaseOf(record, preview, _createAllowed, canCreate: false, false, null).Should().Be(JiraSubmissionView.Phase.Unavailable);
        JiraSubmissionView.PhaseOf(record, preview, _createAllowed with { Create = false }, canCreate: true, false, null)
            .Should().Be(JiraSubmissionView.Phase.Unavailable);
        JiraSubmissionView.PhaseOf(record, preview with { ReviewOnly = true }, _createAllowed, canCreate: true, false, null)
            .Should().Be(JiraSubmissionView.Phase.ReviewOnly);
    }

    [Fact]
    public void ReadOnlyFence_KeepsPreviewVisibleButNotReady()
    {
        OperationalRecordResponse record = Record() with { ReadOnlyIntegrationMode = true };
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(record);

        JiraSubmissionView.PhaseOf(record, Preview(), actions, canCreate: true, false, null)
            .Should().Be(JiraSubmissionView.Phase.Unavailable);
        actions.WriteFenceReason.Should().NotBeNull();
    }

    [Fact]
    public void Submitting_OutranksEverythingWhileTheCommandIsInFlight()
    {
        JiraSubmissionView.PhaseOf(Record(), Preview(), _createAllowed, true, submitting: true, null)
            .Should().Be(JiraSubmissionView.Phase.Submitting);
    }

    [Fact]
    public void Acknowledgement_IsNeverPresentedAsCreated_UntilTheReReadRecordCarriesTheKey()
    {
        var attempt = new JiraSubmissionView.Attempt(Transfer(OperationalRecordWorkflowState.JiraCreated, "SIM-7"), null);

        JiraSubmissionView.PhaseOf(Record(), null, _createAllowed, true, false, attempt)
            .Should().Be(JiraSubmissionView.Phase.Acknowledged);
        JiraSubmissionView.PhaseOf(Saved("SIM-7"), null, _createAllowed, true, false, attempt)
            .Should().Be(JiraSubmissionView.Phase.Created);
    }

    [Fact]
    public void PersistedKey_IsCreated_EvenWithoutAnAttempt_AndWhenDuplicateWasRefused()
    {
        UiProblem duplicate = UiProblemFactory.NetworkFailure() with { Code = OperationalErrorCodes.JiraAlreadyCreated, Stage = "jira-create" };

        JiraSubmissionView.PhaseOf(Saved("SIM-1"), null, _createAllowed, true, false, null).Should().Be(JiraSubmissionView.Phase.Created);
        JiraSubmissionView.PhaseOf(Saved("SIM-1"), null, _createAllowed, true, false, new(null, duplicate))
            .Should().Be(JiraSubmissionView.Phase.Created);
    }

    [Fact]
    public void ServerRefusal_WithoutKeyOrUnknownOutcome_IsRejected()
    {
        UiProblem refused = UiProblemFactory.NetworkFailure() with { Code = OperationalErrorCodes.OperationalRecordChanged, Stage = "source-validation" };

        JiraSubmissionView.PhaseOf(Record(), null, _createAllowed, true, false, new(null, refused))
            .Should().Be(JiraSubmissionView.Phase.Rejected);
    }

    [Theory]
    [InlineData(true, OperationalRecordWorkflowState.JiraCreateFailed)]
    [InlineData(false, OperationalRecordWorkflowState.CreatingJira)]
    public void UnknownOutcomeOnRecord_IsUncertain_AndOffersNoCreateOrRetry(bool flag, OperationalRecordWorkflowState state)
    {
        OperationalRecordResponse record = Record() with { WorkflowState = state, ReconciliationRequired = flag, RetryEligible = true };
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(record);

        JiraSubmissionView.PhaseOf(record, Preview(), actions, true, false, null).Should().Be(JiraSubmissionView.Phase.Uncertain);
        actions.Create.Should().BeFalse();
        actions.Retry.Should().BeFalse();
    }

    [Fact]
    public void UnacknowledgedCommand_StaysUncertain_EvenIfTheReReadLooksUntouched()
    {
        UiProblem uncertain = UiProblemFactory.UncertainPublication(UiProblemFactory.NetworkFailure());
        OperationalRecordResponse untouched = Record() with { WorkflowState = OperationalRecordWorkflowState.Previewed };

        JiraSubmissionView.PhaseOf(untouched, Preview(), _createAllowed, true, false, new(null, uncertain))
            .Should().Be(JiraSubmissionView.Phase.Uncertain);
    }

    [Fact]
    public void SameCommandAlreadyRunning_IsUncertain_NotRejected()
    {
        // A transport resend of a create whose response was lost can meet the first execution.
        UiProblem running = UiProblemFactory.NetworkFailure() with { Code = OperationalErrorCodes.WorkflowAlreadyInProgress, Stage = "command" };
        OperationalRecordResponse requested = Record() with { WorkflowState = OperationalRecordWorkflowState.CreateRequested };

        JiraSubmissionView.PhaseOf(requested, null, OperationalRecordView.ActionsFor(requested), true, false, new(null, running))
            .Should().Be(JiraSubmissionView.Phase.Uncertain);
    }

    [Fact]
    public void UnsupportedTypeGuidance_ComesOnlyFromServerBlockers()
    {
        OperationalRecordResponse record = Record() with { BlockingConditions = ["ApplicationMappingPending", "RequesterMissing"] };

        JiraSubmissionView.UnsupportedTypeGuidance(record, null).Should().ContainSingle()
            .Which.Should().Contain("Uygulama Kurulumu için Jira etiket eşlemesi doğrulanmadı");
        JiraSubmissionView.UnsupportedTypeGuidance(Record(), null).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ panel rendering

    [Fact]
    public async Task ReadyPanel_ShowsSourceOr_Type_Destination_SourceStaysOpen_AndDuplicateProtection()
    {
        string html = await RenderPanelAsync(Record(), Preview(), JiraSubmissionView.Phase.Ready);

        html.Should().Contain("Gönderime hazır")
            .And.Contain("OR-123").And.Contain("kaynak no")
            .And.Contain("Sunucu Talebi")
            .And.Contain("SYNPROJ · Görev").And.Contain("kimlik 10001")
            .And.Contain("Turuncu Hat kaydı açık kalacak.")
            .And.Contain("Kullanılamaz. Bu ekranda aktar-ve-kapat işlemi sunulmaz.")
            .And.Contain("Kaynak kapatma bu dağıtımda kapalıdır")
            .And.Contain("Mükerrer koruma (sunucu aktarım anahtarı)").And.Contain("synthetic-transfer-key")
            .And.Contain("açık onay verildikten sonra");
        html.Should().NotContain("<button").And.NotContain("href=");
    }

    [Fact]
    public async Task SubmittingPanel_IsBusyLiveRegion()
    {
        string html = await RenderPanelAsync(Record(), Preview(), JiraSubmissionView.Phase.Submitting);

        html.Should().Contain("Gönderiliyor").And.Contain("aria-busy=\"true\"").And.Contain("role=\"status\"")
            .And.Contain("işlemi tekrarlamayın");
    }

    [Fact]
    public async Task CreatedPanel_ShowsSavedKey_NoInventedLink_AndDuplicateGuard()
    {
        UiProblem duplicate = UiProblemFactory.NetworkFailure() with { Code = OperationalErrorCodes.JiraAlreadyCreated, Stage = "jira-create" };
        string html = await RenderPanelAsync(Saved("SIM-42") with { SimulationMode = true }, null, JiraSubmissionView.Phase.Created, new(null, duplicate));

        html.Should().Contain("Jira kaydı oluşturuldu · SIM-42")
            .And.Contain("Jira oluşturuldu. Turuncu Hat kaydı açık bırakıldı.")
            .And.Contain("data-jira-key").And.Contain("Sunucu bağlantı adresi sağlamıyor")
            .And.Contain("Mükerrer koruma").And.Contain("mevcut kayıt korundu")
            .And.Contain("sentetiktir ve gerçek bir Jira kaydı değildir");
        html.Should().NotContain("href=").And.NotContain("<a ");
    }

    [Fact]
    public async Task CreatedPanel_AfterLostResponse_SaysTheKeyCameFromTheReRead()
    {
        var attempt = new JiraSubmissionView.Attempt(null, UiProblemFactory.UncertainPublication(UiProblemFactory.NetworkFailure()));
        string html = await RenderPanelAsync(Saved("SIM-3"), null, JiraSubmissionView.Phase.Created, attempt);

        html.Should().Contain("data-resolved-uncertain").And.Contain("kayıtlı durum yeniden okunarak doğrulandı")
            .And.Contain("Yeni gönderim yapmayın");
        (await RenderPanelAsync(Saved("SIM-3"), null, JiraSubmissionView.Phase.Created)).Should().NotContain("data-resolved-uncertain");
    }

    [Fact]
    public async Task AcknowledgedPanel_DoesNotClaimTheIssueExists()
    {
        var attempt = new JiraSubmissionView.Attempt(Transfer(OperationalRecordWorkflowState.JiraCreated, "SIM-9"), null);
        string html = await RenderPanelAsync(Record(), null, JiraSubmissionView.Phase.Acknowledged, attempt);

        html.Should().Contain("İstek yanıtlandı; Jira kaydı doğrulanmadı")
            .And.Contain("oluşturulmuş bir Jira kaydı olarak kabul edilmez")
            .And.Contain("Yanıtta bildirilen anahtar (doğrulanmadı)");
        html.Should().NotContain("Jira kaydı oluşturuldu");
    }

    [Fact]
    public async Task UncertainPanel_DirectsToReconciliation_WithoutAnyAction()
    {
        OperationalRecordResponse record = Record() with { ReconciliationRequired = true, WorkflowState = OperationalRecordWorkflowState.JiraCreateFailed };
        string html = await RenderPanelAsync(record, null, JiraSubmissionView.Phase.Uncertain);

        html.Should().Contain("Sonuç belirsiz; mutabakat gerekli")
            .And.Contain("gönderim ve yeniden deneme bu ekranda kapalıdır")
            .And.Contain("destek referansıyla platform yöneticisine bildirin")
            .And.Contain("Yenileme yeni bir Jira isteği göndermez")
            .And.Contain("Boş arama sonucu tek başına kaydın olmadığını kanıtlamaz")
            .And.Contain("corr-123");
        html.Should().NotContain("<button").And.NotContain("Jira kaydı oluşturuldu");
    }

    [Fact]
    public async Task RejectedPanel_StatesTheRefusal_AndThatNoKeyIsSaved()
    {
        UiProblem refused = UiProblemFactory.NetworkFailure() with
        {
            Code = OperationalErrorCodes.OperationalRecordChanged,
            Title = "Kaynak kayıt değişmiş",
            Explanation = "Jira kaydı oluşturulmadı."
        };
        string html = await RenderPanelAsync(Record(), null, JiraSubmissionView.Phase.Rejected, new(null, refused));

        html.Should().Contain("Gönderim reddedildi").And.Contain("Kaynak kayıt değişmiş")
            .And.Contain("Jira anahtarı yok ve sonuç belirsiz olarak işaretlenmedi");
    }

    [Fact]
    public async Task UnavailablePanel_ExplainsUnsupportedType_AndTransferAndClose()
    {
        OperationalRecordResponse record = Record() with { JiraEligible = false, BlockingConditions = ["RetirementMappingPending"] };
        string html = await RenderPanelAsync(record, null, JiraSubmissionView.Phase.Unavailable, reason: "Bu kayıt Jira aktarımına uygun değil.");

        html.Should().Contain("Gönderime hazır değil").And.Contain("Bu kayıt Jira aktarımına uygun değil.")
            .And.Contain("Desteklenmeyen talep türü").And.Contain("Sunucu Talebi etiketleri yerine kullanılamaz")
            .And.Contain("Aktar ve kapat: kullanılamaz.");
    }

    [Fact]
    public async Task CloseIntentFromServer_IsStatedAccurately_NotAsSourceOpen()
    {
        string html = await RenderPanelAsync(Record() with { SourceCloseEnabled = true }, Preview() with { SourceCloseRequested = true },
            JiraSubmissionView.Phase.Ready);

        html.Should().Contain("Turuncu Hat kaydının kapatılması denenecek").And.NotContain("Turuncu Hat kaydı açık kalacak");
    }

    // ------------------------------------------------------------------ confirmation gate

    [Fact]
    public void ConfirmationDialog_CannotCloseWithoutTheExplicitStatement()
    {
#pragma warning disable BL0005 // Parameters are set directly to exercise the handler without a dialog host.
        var dialog = new JiraCreateDialog { Record = Record(), Preview = Preview() };
#pragma warning restore BL0005
        MethodInfo confirm = typeof(JiraCreateDialog).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // No dialog instance is cascaded: reaching Close would throw, so returning proves the guard.
        confirm.Invoking(m => m.Invoke(dialog, null)).Should().NotThrow();

        string statement = (string)typeof(JiraCreateDialog).GetProperty("AcknowledgementText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        statement.Should().Contain("OR-123").And.Contain("tek bir Jira kaydı").And.Contain("açık kalacağını");
    }

    [Fact]
    public async Task ConfirmationStatement_IsALabelledNativeCheckbox_DescribedByItsGate()
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<SoConfirmStatement>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(SoConfirmStatement.Text)] = "OR-123 için onaylıyorum.",
                [nameof(SoConfirmStatement.DescribedBy)] = "gate-hint"
            }))).ToHtmlString());

        html = WebUtility.HtmlDecode(html);
        html.Should().Contain("<label").And.Contain("type=\"checkbox\"").And.Contain("aria-describedby=\"gate-hint\"")
            .And.Contain("OR-123 için onaylıyorum.").And.NotContain("checked");
    }

    // ------------------------------------------------------------------ page handlers

    [Fact]
    public async Task Create_AcknowledgedWithoutSavedKey_IsNotShownAsCreated_AndFocusMovesToOutcome()
    {
        using var f = new PageFixture();
        f.Records.CreateJiraAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Transfer(OperationalRecordWorkflowState.JiraCreated, "SIM-5"));

        await f.Call("CreateAsync");

        f.Phase().Should().Be(JiraSubmissionView.Phase.Acknowledged);
        f.Get<int>("_focusRequest").Should().Be(1);
        f.Get<JiraPreviewResponse?>("_preview").Should().BeNull();
        await f.Records.Received(1).CreateJiraAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithSavedKeyOnReRead_IsCreated()
    {
        using var f = new PageFixture();
        f.Records.CreateJiraAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Transfer(OperationalRecordWorkflowState.JiraCreated, "SIM-6"));
        f.Records.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Saved("SIM-6"));

        await f.Call("CreateAsync");

        f.Phase().Should().Be(JiraSubmissionView.Phase.Created);
        f.Get<OperationalRecordResponse>("_record").JiraIssueKey.Should().Be("SIM-6");
    }

    [Fact]
    public async Task Create_UnacknowledgedTransport_StaysUncertainUntilExplicitRefresh_WithNoSecondCommand()
    {
        using var f = new PageFixture();
        f.Records.CreateJiraAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JiraTransferResponse>(new SecureOpsApiException(
                UiProblemFactory.UncertainPublication(UiProblemFactory.NetworkFailure()))));
        f.Records.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Record() with { WorkflowState = OperationalRecordWorkflowState.Previewed });

        await f.Call("CreateAsync");

        f.Phase().Should().Be(JiraSubmissionView.Phase.Uncertain);
        await f.Call("CreateAsync"); // No preview is held any more; the handler must refuse.
        await f.Records.Received(1).CreateJiraAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await f.Call("LoadAsync", true);
        f.Get<JiraSubmissionView.Attempt?>("_attempt").Should().BeNull();
    }

    [Fact]
    public async Task Create_CanceledConfirmation_SendsNothing()
    {
        using var f = new PageFixture(confirm: false);

        await f.Call("CreateAsync");

        f.Phase().Should().Be(JiraSubmissionView.Phase.Ready);
        f.Get<bool>("_busy").Should().BeFalse();
        await f.Records.DidNotReceive().CreateJiraAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_WithoutPersistedKey_IsSentOnlyAfterExplicitConfirmation(bool confirm)
    {
        // A safe JiraCreateFailed record is retry-eligible, and the retry route asks Jira to create
        // the issue again; a single click must not dispatch it.
        using var f = new PageFixture(confirm);
        f.Set("_preview", null);
        f.Set("_record", Record() with { WorkflowState = OperationalRecordWorkflowState.JiraCreateFailed, RetryEligible = true });
        f.Records.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Transfer(OperationalRecordWorkflowState.JiraCreateFailed, null));

        await f.Call("RetryAsync");

        await f.Dialogs.Received(1).ShowAsync<JiraRetryDialog>(Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>());
        await f.Records.Received(confirm ? 1 : 0).RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        f.Get<bool>("_busy").Should().BeFalse();
        f.Get<bool>("_submitting").Should().BeFalse();
    }

    [Fact]
    public void RetryDialog_CannotCloseWithoutTheExplicitStatement_AndNamesTheEffect()
    {
#pragma warning disable BL0005 // Parameters are set directly to exercise the handler without a dialog host.
        var dialog = new JiraRetryDialog { Record = Record() with { WorkflowState = OperationalRecordWorkflowState.JiraCreateFailed } };
#pragma warning restore BL0005
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;

        typeof(JiraRetryDialog).GetMethod("Confirm", members)!.Invoking(m => m.Invoke(dialog, null)).Should().NotThrow();
        ((string)typeof(JiraRetryDialog).GetProperty("Statement", members)!.GetValue(dialog)!)
            .Should().Contain("OR-123").And.Contain("tek bir Jira kaydının yeniden deneneceğini").And.Contain("açık kalacağını");
    }

    // ------------------------------------------------------------------ helpers

    private static OperationalRecordResponse Record() => JsonSerializer.Deserialize<OperationalRecordResponse>("""
        {"id":"00000000-0000-0000-0000-000000000123","sourceRecordId":"123","orCode":"OR-123",
         "title":"Synthetic","description":"Synthetic only","classification":0,"jiraEligible":true,
         "workflowState":3,"correlationId":"corr-123","version":1,"reasonCodes":[],"blockingConditions":[]}
        """, ApiResponseReader.JsonOptions)!;

    private static OperationalRecordResponse Saved(string key) => Record() with
    {
        WorkflowState = OperationalRecordWorkflowState.JiraCreated,
        JiraIssueKey = key,
        JiraExists = true
    };

    private static JiraPreviewResponse Preview() => JsonSerializer.Deserialize<JiraPreviewResponse>("""
        {"operationalRecordId":"00000000-0000-0000-0000-000000000123","orCode":"OR-123","projectKey":"SYNPROJ",
         "issueType":"Görev","issueTypeId":"10001","summary":"Synthetic summary","description":"Synthetic body",
         "requesterAccountId":"synthetic.requester","mappingVersion":"synthetic-v1","idempotencyKey":"synthetic-transfer-key",
         "warnings":[],"requestType":0,"sourceCloseRequested":false,"blockingConditions":[],"recordVersion":1}
        """, ApiResponseReader.JsonOptions)!;

    private static JiraTransferResponse Transfer(OperationalRecordWorkflowState state, string? key) =>
        new(Guid.Parse("00000000-0000-0000-0000-000000000123"), "OR-123", state, key, "synthetic-v1", "synthetic-transfer-key", 0, "corr-transfer");

    private static async Task<string> RenderPanelAsync(
        OperationalRecordResponse record,
        JiraPreviewResponse? preview,
        JiraSubmissionView.Phase phase,
        JiraSubmissionView.Attempt? attempt = null,
        string? reason = null)
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<JiraSubmissionPanel>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(JiraSubmissionPanel.Record)] = record,
                [nameof(JiraSubmissionPanel.Preview)] = preview,
                [nameof(JiraSubmissionPanel.Phase)] = phase,
                [nameof(JiraSubmissionPanel.Attempt)] = attempt,
                [nameof(JiraSubmissionPanel.UnavailableReason)] = reason
            }))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    private sealed class PageFixture : IDisposable
    {
        private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly EmptyDetail _component = new();
        private readonly TestRenderer _renderer = new();
        public IOperationalRecordApiClient Records { get; } = Substitute.For<IOperationalRecordApiClient>();
        public IDialogService Dialogs { get; } = Substitute.For<IDialogService>();
        public AccessSnapshot Snapshot { get; } = new(new(Guid.NewGuid(), "Approved", ["Admin"],
            AccessRoleCatalog.GetCapabilities(["Admin"]), null, "Demo", new(30, 12, true, true, "Lax", true, "Local")), null, DateTimeOffset.UtcNow);

        public PageFixture(bool confirm = true)
        {
            IDialogReference reference = Substitute.For<IDialogReference>();
            reference.Result.Returns(Task.FromResult(confirm ? DialogResult.Ok(true) : DialogResult.Cancel()));
            Dialogs.ShowAsync<JiraCreateDialog>(Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>()).Returns(reference);
            Dialogs.ShowAsync<JiraRetryDialog>(Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>()).Returns(reference);
            Records.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Record());
            SetProperty("Records", Records);
            SetProperty("Dialogs", Dialogs);
            SetProperty("AccessProvider", Substitute.For<ICurrentAccessProvider>());
            SetProperty("RecordId", Record().Id);
            Set("_record", Record());
            Set("_preview", Preview());
            Set("_access", Snapshot);
            _renderer.Attach(_component);
        }

        public JiraSubmissionView.Phase Phase()
        {
            OperationalRecordResponse record = Get<OperationalRecordResponse>("_record");
            return JiraSubmissionView.PhaseOf(record, Get<JiraPreviewResponse?>("_preview"), OperationalRecordView.ActionsFor(record),
                Snapshot.Can(Capabilities.OperationalRecordsCreateJira), Get<bool>("_submitting"), Get<JiraSubmissionView.Attempt?>("_attempt"));
        }

        public T Get<T>(string name) => (T)typeof(OperationalRecordDetail).GetField(name, _members)!.GetValue(_component)!;
        public void Set(string name, object? value) => typeof(OperationalRecordDetail).GetField(name, _members)!.SetValue(_component, value);
        public void SetProperty(string name, object? value) => typeof(OperationalRecordDetail).GetProperty(name, _members)!.SetValue(_component, value);
        public Task Call(string name, params object[] args) => _renderer.Dispatcher.InvokeAsync(() =>
            (Task)typeof(OperationalRecordDetail).GetMethod(name, _members)!.Invoke(_component, args)!);
        public void Dispose() { _component.Dispose(); ((IDisposable)_renderer).Dispose(); }
    }

    private sealed class EmptyDetail : OperationalRecordDetail
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) { }
    }

#pragma warning disable BL0006 // Test-only renderer attaches the actual component handlers without UI packages.
    private sealed class TestRenderer() : Renderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public void Attach(IComponent component) => AssignRootComponentId(component);
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Test render failed", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }
#pragma warning restore BL0006
}
