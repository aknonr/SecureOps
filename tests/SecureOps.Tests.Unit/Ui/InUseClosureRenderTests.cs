using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

public sealed class InUseClosureRenderTests
{
    [Theory]
    [InlineData("Unconfirmed", "Acknowledged", "Kapatma isteği iletildi. Lütfen kaynak sistemde OR durumunu kontrol edin.", true)]
    [InlineData("Unknown", "Unknown", "Kapatma sonucu doğrulanamadı. Tekrar denemeden önce kaynak sistemde OR durumunu kontrol edin.", true)]
    [InlineData("Failed", "Rejected", "İstek reddedildi; işlem başarısız.", false)]
    [InlineData("Running", "Started", "Kaynak adımı çalışıyor", false)]
    public async Task ClosureOutcome_IsExplicitWithoutClaimingSourceClosure(string state, string outcome, string text, bool confirm)
    {
        InUseExecution operation = Operation(state, outcome);
        InUseClosureVerification.CanConfirm(operation).Should().Be(confirm);
        string html = await RenderAsync(operation);
        html.Should().Contain("OR-000123").And.Contain(text).And.NotContain("OR kapandı").And.Contain("aria-live=\"polite\"");
        InUseClosureVerification.CanConfirm(operation with { Evidence = operation.Evidence.Where(e => e.Step != "Attachment").ToArray() }).Should().BeFalse();
    }

    [Fact]
    public async Task ManualAndSourceVerification_NeverBecomeInterchangeable()
    {
        InUseExecution operation = Operation("Unconfirmed", "Acknowledged");
        InUseStepEvidence manual = new("ManualVerification", "ManuallyConfirmed", null, "OperatorAttestedSourceClosed",
            new DateTimeOffset(2026, 9, 22, 3, 15, 0, TimeSpan.Zero), "SecureOps.Api")
        { ConfirmedBy = Guid.NewGuid(), ConfirmedByLabel = "Synthetic <operator>" };
        InUseExecution confirmed = operation with { Evidence = [.. operation.Evidence, manual] };
        string html = await RenderAsync(confirmed);
        html.Should().Contain("Manuel doğrulama").And.Contain("Sistem doğrulaması değildir").And.Contain("2026-09-22 03:15:00 UTC")
            .And.Contain("Synthetic <operator>").And.NotContain("OR kapandı");
        InUseClosureVerification.CanConfirm(confirmed).Should().BeFalse();
        InUseClosureVerification.SourceVerified(confirmed).Should().BeFalse();
        (await RenderAsync(operation with { State = "Completed" })).Should().NotContain("OR kapandı");
        (await RenderAsync(operation with
        {
            State = "Completed",
            Evidence = [.. operation.Evidence,
            new("Closure", "Verified", "123", "AuthoritativeEvidence", DateTimeOffset.UtcNow, "Worker")]
        })).Should().Contain("OR kapandı");
    }

    [Theory]
    [InlineData("Acknowledged", "WASAS onay isteği iletildi")]
    [InlineData("Unknown", "WASAS onay sonucu doğrulanamadı")]
    [InlineData("Verified", "WASAS aktivitesinin tamamlandığı kaynak kanıtıyla doğrulandı")]
    [InlineData("Rejected", "İstek reddedildi")]
    public async Task ActivityOutcome_IsNotOverallClosure(string outcome, string expected)
    {
        InUseExecution operation = Operation(outcome == "Rejected" ? "Failed" : "Unconfirmed", outcome)
            with
        { VerificationMode = "WasasActivityManual" };
        (await RenderAsync(operation)).Should().Contain(expected).And.NotContain("OR kapandı");
        InUseClosureVerification.ActivityVerified(operation).Should().Be(outcome == "Verified");
        InUseClosureVerification.SourceVerified(operation).Should().BeFalse();
    }

    private static InUseExecution Operation(string state, string outcome) => new(Guid.NewGuid(), Guid.NewGuid(), 2, new string('A', 64),
        state, 5, 12, Guid.NewGuid(), "Synthetic initiator", DateTimeOffset.UtcNow,
        [new("Attachment", "Verified", "456", "ExactBytes", DateTimeOffset.UtcNow, "Worker"),
         new("Bpm", outcome, "789", "ResponseCategory", DateTimeOffset.UtcNow, "Worker")])
    { SourceCode = "OR-000123" };

    private static async Task<string> RenderAsync(InUseExecution operation)
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<InUseClosureResult>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(InUseClosureResult.Operation)] = operation }))).ToHtmlString()));
    }
}
