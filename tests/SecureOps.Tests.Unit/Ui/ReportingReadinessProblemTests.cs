using FluentAssertions;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the difference between reporting that is switched off and reporting that is broken.
/// </summary>
/// <remarks>
/// Both arrive as 503. Before these mappings existed, both fell through to the generic
/// "Servis şu anda yanıt veremiyor" — so an administrator opening the Yönetim Panosu in an
/// environment where SQL reporting had simply never been enabled was sent to investigate an outage
/// that did not exist, and offered a retry that could never succeed.
/// </remarks>
public sealed class ReportingReadinessProblemTests
{
    [Fact]
    public void PersistenceNotConfigured_ReadsAsAReadinessStateNotAnOutage()
    {
        UiProblem problem = Problem(OperationalErrorCodes.ReportingPersistenceNotConfigured);

        problem.Kind.Should().Be(UiProblemKind.NotConfigured);
        problem.Title.Should().Be("Yönetim raporlaması henüz etkin değil");
        problem.Explanation.Should().Be(
            "Bu ekran kalıcı SQL raporlama verisi etkinleştirildiğinde gerçek kullanım ve operasyon "
            + "metriklerini gösterecektir.");
    }

    [Fact]
    public void PersistenceNotConfigured_NeverShowsTheGenericServiceOutageWording()
    {
        UiProblem problem = Problem(OperationalErrorCodes.ReportingPersistenceNotConfigured);

        problem.Title.Should().NotContain("yanıt ver");
        problem.Explanation.Should().NotContain("yanıt ver");
    }

    [Fact]
    public void PersistenceNotConfigured_OffersNoRetry()
    {
        // Retrying a feature that was never switched on cannot succeed, and offering the button
        // implies it might.
        Problem(OperationalErrorCodes.ReportingPersistenceNotConfigured).Retryable.Should().BeFalse();
    }

    [Fact]
    public void ReportingUnavailable_IsARetryableServiceProblemAndDistinctFromNotConfigured()
    {
        UiProblem problem = Problem(OperationalErrorCodes.ReportingUnavailable);

        problem.Kind.Should().Be(UiProblemKind.UpstreamUnavailable);
        problem.Retryable.Should().BeTrue();
        problem.Kind.Should().NotBe(UiProblemKind.NotConfigured);
    }

    [Fact]
    public void Forbidden_StaysAnOrdinaryAuthorizationState()
    {
        UiProblem problem = UiProblemFactory.FromResponse(403, null);

        problem.Kind.Should().Be(UiProblemKind.Forbidden);
        problem.Retryable.Should().BeFalse();
    }

    [Fact]
    public void ValidationFailure_IsAboutTheSelectedRangeRatherThanTheService()
    {
        UiProblem problem = Problem(OperationalErrorCodes.ReportingValidationFailed, statusCode: 400);

        problem.Kind.Should().Be(UiProblemKind.Validation);
        problem.Title.Should().Be("Rapor aralığı kabul edilmedi");
    }

    [Fact]
    public void UnclassifiedFailure_SaysOnlyWhatIsCertainAndKeepsTheSupportReference()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            418,
            new ProblemDetailsPayload { Code = "SomethingNewAndUnmapped", CorrelationId = "corr-9" });

        problem.Title.Should().Be("İşlem tamamlanamadı.");
        problem.CorrelationId.Should().Be("corr-9");
    }

    private static UiProblem Problem(string code, int statusCode = 503) =>
        UiProblemFactory.FromResponse(statusCode, new ProblemDetailsPayload { Code = code });
}
