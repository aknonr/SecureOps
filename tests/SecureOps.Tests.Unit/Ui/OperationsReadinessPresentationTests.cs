using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

public sealed class OperationsReadinessPresentationTests
{
    private const BindingFlags _flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly DateTimeOffset _capture = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static OperationsReadiness Snapshot() => new("synthetic", 1, _capture, "synthetic-reference",
        [new("AnnouncementMail:Enabled", "False", "Default"), new("AnnouncementMail:SelfTestEnabled", "False", "Default"),
         new("AnnouncementMail:SendEnabled", "False", "Default"), new("Hangfire:Enabled", "False", "Default"),
         new("InUseCompletion:Enabled", "False", "Default"), new("InUseCompletion:Provider", "Disabled", "Default")],
        new("Disabled", "configuration", [], "NotChecked", 0, null, _capture.AddSeconds(20)),
        [.. Enumerable.Range(1, 6).Select(i => new OperationAssetReadiness($"synthetic-{i}", "Validated", "png", null))],
        "synthetic-assets", "synthetic-archive", "ReadableWriteNotTested")
    { Bundles = [new("synthetic-bundle", "Synthetic", "PresentNotValidated")] };

    [Fact]
    public void DisabledSnapshot_DistinguishesUncheckedWorkerAndPartialAssetArchiveEvidence()
    {
        IReadOnlyList<OperationStatusRow> rows = OperationsReadinessPresentation.Rows(Snapshot());
        rows.Select(r => r.Status).Should().Equal("Kapalı", "Kapalı", "Kontrol edilmedi", "Kısmen doğrulandı", "Kısmen doğrulandı", "Kapalı");
        rows[0].Reason.Should().Contain("İş kuyruğu API tarafında etkin değil");
        rows[2].Reason.Should().Contain("sorgusu yapılmadı");
        rows[3].Reason.Should().Contain("6 dosya doğrulandı").And.Contain("doğrulaması tamamlanmadı");
        rows[4].Reason.Should().Be("Okuma doğrulandı. Yazma kontrolü yapılmadı.");
        rows.Should().OnlyContain(r => r.Next.Length > 0);
    }

    [Theory]
    [InlineData("ConfigurationMissing", "NotChecked", "Yapılandırma eksik", "Kontrol edilmedi")]
    [InlineData("Ready", "Ready", "Kısmen doğrulandı", "Doğrulandı")]
    [InlineData("WrongQueue", "WrongQueue", "Kısmen doğrulandı", "Kontrol başarısız")]
    [InlineData("NoWorker", "NoWorker", "Kısmen doğrulandı", "Kontrol başarısız")]
    [InlineData("Unknown", "Unknown", "Kontrol başarısız", "Kontrol başarısız")]
    public void SourceAndWorker_AreIndependentEvidence(string state, string worker, string sourceLabel, string workerLabel)
    {
        OperationsReadiness report = Snapshot() with { Source = new(state, "queue", [], worker, 1, _capture, _capture), Settings = [] };
        IReadOnlyList<OperationStatusRow> rows = OperationsReadinessPresentation.Rows(report);
        rows[0].Status.Should().Be(sourceLabel);
        rows[2].Status.Should().Be(workerLabel);
        rows.Should().HaveCount(5);
    }

    [Theory]
    [InlineData("False", "True", "True", "Kapalı")]
    [InlineData("True", "False", "False", "Kapalı")]
    [InlineData("True", "True", "False", "Kontrol edilmedi")]
    [InlineData("True", "True", "True", "Kontrol edilmedi")]
    [InlineData("", "", "", "Kontrol edilmedi")]
    public void MailFlags_NeverProveSmtp(string enabled, string self, string send, string expected)
    {
        OperationsReadiness report = Snapshot() with { Settings = [new("AnnouncementMail:Enabled", enabled, "Default"), new("AnnouncementMail:SelfTestEnabled", self, "Default"), new("AnnouncementMail:SendEnabled", send, "Default")] };
        OperationsReadinessPresentation.Rows(report)[1].Status.Should().Be(expected);
    }

    [Fact]
    public void Timestamps_UseResponseInstantsAndExplicitUtc()
    {
        OperationsReadinessPresentation.Time(_capture.ToOffset(TimeSpan.FromHours(3))).Should().Be("01.01.2026 00:00:00 UTC (+00:00)");
        OperationsReadinessPresentation.Time(Snapshot().Source.CheckedAt).Should().Be("01.01.2026 00:00:20 UTC (+00:00)");
    }

    [Fact]
    public async Task InitialRender_DoesNotRequestOrPrefillHealthyStates()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://synthetic.invalid/") };
        await using ServiceProvider services = new ServiceCollection().AddLogging()
            .AddSingleton(new OperationsDiagnosticsApiClient(http, new FakeApiSessionContext()))
            .AddSingleton(Substitute.For<IJSRuntime>()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<OperationsReadinessPanel>()).ToHtmlString());
        html = WebUtility.HtmlDecode(html);
        html.Should().Contain("İş akışı kontrolleri").And.Contain("Henüz kontrol edilmedi").And.Contain("Durumu kontrol et")
            .And.Contain("aria-live=\"polite\"").And.NotContain("Tanılama raporunu indir").And.NotContain("Doğrulandı");
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task RepeatedClick_IsSingleFlight_FailureRetainsSnapshot_DownloadDoesNotRecheck()
    {
        using var handler = new Handler { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = new HttpClient(handler) { BaseAddress = new("https://synthetic.invalid/") };
        using var component = new OperationsReadinessPanel();
        Set(component, "Diagnostics", new OperationsDiagnosticsApiClient(http, new FakeApiSessionContext()), property: true);
        IJSRuntime js = Substitute.For<IJSRuntime>();
        Set(component, "Js", js, property: true);
        var busy = new List<bool>();
        typeof(OperationsReadinessPanel).GetProperty(nameof(OperationsReadinessPanel.BusyChanged))!.SetValue(component, EventCallback.Factory.Create<bool>(busy, busy.Add));
        Task first = Invoke(component, "ReadAsync");
        await Invoke(component, "ReadAsync");
        handler.Count.Should().Be(1);
        handler.Pending.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(Snapshot()) });
        await first;
        busy.Should().Equal(true, false);
        var original = (OperationsReadiness)Get(component, "_result")!;
        handler.Pending = null;
        handler.Status = HttpStatusCode.ServiceUnavailable;
        await Invoke(component, "ReadAsync");
        Get(component, "_result").Should().BeSameAs(original);
        Get(component, "_problem").Should().NotBeNull();
        await Invoke(component, "DownloadAsync");
        handler.Count.Should().Be(2);
        object?[] args = (object?[])js.ReceivedCalls().Single().GetArguments()[2]!;
        args[0].Should().Be("wasas-api-operations.json");
        OperationsReadiness downloaded = JsonSerializer.Deserialize<OperationsReadiness>(Convert.FromBase64String((string)args[2]!))!;
        downloaded.CapturedAt.Should().Be(original.CapturedAt);
        downloaded.Source.CheckedAt.Should().Be(original.Source.CheckedAt);
        handler.Status = HttpStatusCode.Forbidden;
        await Invoke(component, "ReadAsync");
        Get(component, "_result").Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AvailableResult_RendersEvidenceAndStaleFailureWithoutLeakingBodies(bool failed)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://synthetic.invalid/") };
        var component = new OperationsReadinessPanel();
        Set(component, "_result", Snapshot());
        if (failed)
        { Set(component, "_problem", UiProblemFactory.FromResponse(503, new() { CorrelationId = "synthetic-support", Detail = "NEVER_RENDER_RAW" })); }
        await using ServiceProvider services = new ServiceCollection().AddLogging()
            .AddSingleton(new OperationsDiagnosticsApiClient(http, new FakeApiSessionContext()))
            .AddSingleton(Substitute.For<IJSRuntime>()).AddSingleton<IComponentActivator>(new Activator(component)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<OperationsReadinessPanel>()).ToHtmlString());
        html = WebUtility.HtmlDecode(html);
        html.Should().Contain("Son kontrol: 01.01.2026 00:00:20 UTC (+00:00)")
            .And.Contain("Yapılandırma kaydı: 01.01.2026 00:00:00 UTC (+00:00)")
            .And.Contain("Kontrol edilmedi").And.Contain("Kısmen doğrulandı").And.Contain("6 dosya doğrulandı")
            .And.Contain("Tanılama raporunu indir").And.Contain("Teknik ayrıntılar").And.NotContain("NEVER_RENDER_RAW");
        html.Contains("Son başarılı rapor gösteriliyor; yeni kontrol başarısız oldu.", StringComparison.Ordinal).Should().Be(failed);
        if (failed)
        { html.Should().Contain("synthetic-support").And.Contain("Kontrol başarısız"); }
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task PageRefreshBusy_PreventsDiagnosticRequest()
    {
        using var component = new OperationsReadinessPanel();
        typeof(OperationsReadinessPanel).GetProperty(nameof(OperationsReadinessPanel.RefreshBusy))!.SetValue(component, true);
        await Invoke(component, "ReadAsync");
        Get(component, "_busy").Should().Be(false);
    }

    private static object? Get(object target, string name) => target.GetType().GetField(name, _flags)!.GetValue(target);
    private static void Set(object target, string name, object value, bool property = false)
    {
        if (property)
        { target.GetType().GetProperty(name, _flags)!.SetValue(target, value); }
        else
        { target.GetType().GetField(name, _flags)!.SetValue(target, value); }
    }
    private static Task Invoke(object target, string method) => (Task)target.GetType().GetMethod(method, _flags)!.Invoke(target, null)!;
    private sealed class Activator(OperationsReadinessPanel panel) : IComponentActivator
    {
        public IComponent CreateInstance(Type type) => type == typeof(OperationsReadinessPanel)
            ? panel : (IComponent)System.Activator.CreateInstance(type)!;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public TaskCompletionSource<HttpResponseMessage>? Pending { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/diagnostics/operations");
            return Pending?.Task ?? Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = Status == HttpStatusCode.OK
                ? JsonContent.Create(Snapshot()) : JsonContent.Create(new { code = "SyntheticFailure", correlationId = "synthetic-support" })
            });
        }
    }
}
