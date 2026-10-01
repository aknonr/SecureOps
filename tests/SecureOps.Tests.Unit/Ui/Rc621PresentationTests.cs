using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Announcements;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Ui.Auth;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

public sealed class Rc621PresentationTests
{
    [Theory]
    [InlineData("Ready")]
    [InlineData("Disabled")]
    public async Task FailedStatusRefresh_DiscardsPreviousReadiness_NotOperatorInput(string state)
    {
        using var handler = new SourceStatusHandler(state);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://synthetic.invalid/") };
        using var component = new AnnouncementSourceReview();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(AnnouncementSourceReview).GetProperty("Api", flags)!.SetValue(component, new AnnouncementApiClient(http, new FakeApiSessionContext()));
        typeof(AnnouncementSourceReview).GetProperty(nameof(AnnouncementSourceReview.OcoReference))!.SetValue(component, "OCO-preserved");
        MethodInfo read = typeof(AnnouncementSourceReview).GetMethod("ReadAsync", flags)!;
        await (Task)read.Invoke(component, [false])!;
        ((AnnouncementSourceReadiness)typeof(AnnouncementSourceReview).GetField("_readiness", flags)!.GetValue(component)!).State.Should().Be(state);
        handler.Fail = true;
        await (Task)read.Invoke(component, [false])!;
        typeof(AnnouncementSourceReview).GetField("_readiness", flags)!.GetValue(component).Should().BeNull();
        ((UiProblem)typeof(AnnouncementSourceReview).GetField("_problem", flags)!.GetValue(component)!).Code.Should().Be("AnnouncementSourceUnavailable");
        component.OcoReference.Should().Be("OCO-preserved");
    }

    private sealed class SourceStatusHandler(string state) : HttpMessageHandler
    {
        public bool Fail { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Fail
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { code = "AnnouncementSourceUnavailable", correlationId = "synthetic-refresh" }) }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = request.RequestUri!.AbsolutePath.EndsWith("readiness", StringComparison.Ordinal)
                    ? JsonContent.Create(new AnnouncementSourceReadiness(state, "queue", [], state, 0, null, DateTimeOffset.UtcNow))
                    : JsonContent.Create(Array.Empty<MaintenanceProfileChoice>())
                });
    }

    [Fact]
    public void EveryImplementedCapability_HasTurkishNameAndExplanation()
    {
        foreach (FieldInfo field in typeof(Capabilities).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string code = (string)field.GetRawConstantValue()!;
            CapabilityDescriptor description = AccessLabels.Describe(code);
            description.Label.Should().NotBe(code);
            description.Description.Should().NotContain("tanımlı değil");
        }
        AccessLabels.RoleDescription("Admin").Should().NotContain("tüm yetkilere");
    }

    [Theory]
    [InlineData("AnnouncementSourceDisabled", "Kaynak toplama kapalı")]
    [InlineData("AnnouncementSourceJobHostUnavailable", "Kaynak iş kuyruğu hazır değil")]
    [InlineData("AnnouncementSourceProfileUnavailable", "Bakım profili hazır değil")]
    [InlineData("AnnouncementSourceUnavailable", "Kaynak durumu okunamadı")]
    public void SourceFailures_AreNotConflatedWithDisabled(string code, string title)
    {
        UiProblem problem = UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = code, CorrelationId = "synthetic-ref" });
        problem.Title.Should().Be(title);
        problem.CorrelationId.Should().Be("synthetic-ref");
    }

    [Fact]
    public void NewTimes_UseTurkeyDisplayOffset_ExistingInstantsAndSecondsArePreserved()
    {
        var time = new AnnouncementTime();
        string offset = new AnnouncementOptions().DefaultDisplayOffset;
        time.Load("", offset);
        time.Offset.Should().Be("+03:00");
        time.Load("2026-09-18T08:00:37-03:00", offset);
        time.Value.Should().Be("2026-09-18T08:00:37-03:00");
        time.ConvertOffset("+03:00").Should().BeTrue();
        time.Value.Should().Be("2026-09-18T14:00:37+03:00");
        time.Load("2026-09-19T00:30:12+00:00", offset);
        time.Value.Should().Be("2026-09-19T00:30:12+00:00");
    }
}
