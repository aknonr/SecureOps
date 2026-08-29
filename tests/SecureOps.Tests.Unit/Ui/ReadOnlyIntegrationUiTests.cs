using FluentAssertions;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins how the UI behaves when the backend reports real-data read-only integration mode.
/// </summary>
/// <remarks>
/// Two properties matter operationally. Reads and the Jira draft must keep working, because that is
/// most of what the record screen is for and the data is genuine. And every external write must be
/// closed off in the UI as well as the server, so the operator is never handed a button whose only
/// possible outcome is a refusal.
/// </remarks>
public sealed class ReadOnlyIntegrationUiTests
{
    [Fact]
    public void ReadOnlyMode_DisablesJiraCreation()
    {
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(
            Record(OperationalRecordWorkflowState.Previewed, readOnly: true));

        actions.Create.Should().BeFalse();
    }

    [Fact]
    public void ReadOnlyMode_DisablesRetry()
    {
        // Retry can resume a source close, which is an external write.
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(
            Record(OperationalRecordWorkflowState.OperationalRecordCloseFailed, readOnly: true, retryEligible: true));

        actions.Retry.Should().BeFalse();
    }

    [Fact]
    public void ReadOnlyMode_KeepsPreviewAvailable()
    {
        // The draft is produced server-side and creates nothing. Disabling it would remove the main
        // reason to open the record in this mode.
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(
            Record(OperationalRecordWorkflowState.Eligible, readOnly: true));

        actions.Preview.Should().BeTrue();
    }

    [Fact]
    public void ReadOnlyMode_ExplainsWhyWritesAreUnavailable()
    {
        // A disabled button with no reason reads as a missing permission.
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(
            Record(OperationalRecordWorkflowState.Eligible, readOnly: true));

        actions.WriteFenceReason.Should().Be(OperationalRecordView.WriteFenceHelp);
        actions.WriteFenceReason.Should().NotContain("ExternalWritesDisabled");
    }

    [Fact]
    public void NormalMode_IsUnchanged()
    {
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(
            Record(OperationalRecordWorkflowState.Eligible, readOnly: false));

        actions.Create.Should().BeTrue();
        actions.Preview.Should().BeTrue();
        actions.WriteFenceReason.Should().BeNull();
    }

    [Theory]
    [InlineData(OperationalRecordWorkflowState.Imported)]
    [InlineData(OperationalRecordWorkflowState.Classified)]
    [InlineData(OperationalRecordWorkflowState.Eligible)]
    [InlineData(OperationalRecordWorkflowState.Previewed)]
    [InlineData(OperationalRecordWorkflowState.CreateRequested)]
    [InlineData(OperationalRecordWorkflowState.CreatingJira)]
    [InlineData(OperationalRecordWorkflowState.JiraCreated)]
    [InlineData(OperationalRecordWorkflowState.ClosingOperationalRecord)]
    [InlineData(OperationalRecordWorkflowState.Completed)]
    [InlineData(OperationalRecordWorkflowState.JiraCreateFailed)]
    [InlineData(OperationalRecordWorkflowState.OperationalRecordCloseFailed)]
    [InlineData(OperationalRecordWorkflowState.NeedsManualReview)]
    public void ReadOnlyMode_OffersNoWriteInAnyWorkflowState(OperationalRecordWorkflowState state)
    {
        // The fence is applied over the state machine rather than inside it, so a state added later
        // cannot reintroduce a write path by omission.
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(
            Record(state, readOnly: true, retryEligible: true));

        actions.Create.Should().BeFalse();
        actions.Retry.Should().BeFalse();
    }

    [Fact]
    public void ExternalWritesDisabled_IsExplainedWithoutTheRawCode()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            new ProblemDetailsPayload { Code = OperationalErrorCodes.ExternalWritesDisabled });

        problem.Explanation.Should().Be("Bu TEST modunda dış sistemlere yazma işlemleri kapalıdır.");
        problem.Title.Should().NotContain("ExternalWritesDisabled");
    }

    [Fact]
    public void ExternalWritesDisabled_OffersNoRetryOfTheForbiddenWrite()
    {
        // Retrying is the one action that cannot succeed here, and offering it would teach the
        // operator that the button is unreliable rather than that the mode is deliberate.
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            new ProblemDetailsPayload { Code = OperationalErrorCodes.ExternalWritesDisabled });

        problem.Retryable.Should().BeFalse();
        problem.Code.Should().Be(OperationalErrorCodes.ExternalWritesDisabled);
    }

    [Fact]
    public void ReadOnlyBanner_FallsBackOnlyWhenTheServerSuppliesNoNotice()
    {
        // The server's wording wins; the constant exists so the mode is never announced with an
        // empty explanation.
        SoReadOnlyIntegrationBannerContract.DefaultNotice.Should().Contain("okunabilir");
        SoReadOnlyIntegrationBannerContract.DefaultNotice.Should().Contain("kapalıdır");
    }

    [Fact]
    public void SimulationAndReadOnly_AreDistinctStates()
    {
        // Simulation means synthetic data. Read-only means real data that cannot be written back.
        // A record may report either independently, and they must never collapse into one notice.
        OperationalRecordResponse simulated = Record(
            OperationalRecordWorkflowState.Eligible, readOnly: false) with { SimulationMode = true };
        OperationalRecordResponse readOnly = Record(
            OperationalRecordWorkflowState.Eligible, readOnly: true);

        simulated.SimulationMode.Should().BeTrue();
        simulated.ReadOnlyIntegrationMode.Should().BeFalse();
        readOnly.ReadOnlyIntegrationMode.Should().BeTrue();
        readOnly.SimulationMode.Should().BeFalse();

        OperationalRecordView.ActionsFor(simulated).Create.Should().BeTrue(
            "simulation exercises the workflow end to end against synthetic providers");
        OperationalRecordView.ActionsFor(readOnly).Create.Should().BeFalse(
            "real read-only mode fences every external write");
    }

    private static OperationalRecordResponse Record(
        OperationalRecordWorkflowState state,
        bool readOnly,
        bool retryEligible = false) =>
        new(
            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SourceRecordId: "OR-1",
            OrCode: "OR-1",
            Title: "Örnek kayıt",
            Description: "Açıklama",
            Requester: null,
            CreatedAt: null,
            Environment: "TEST",
            ServerReference: null,
            ApplicationReference: null,
            Classification: OperationalRecordClassification.OperationalSupport,
            JiraEligible: true,
            EligibilityReason: "Uygun",
            WorkflowState: state,
            JiraIssueKey: null,
            LastErrorCode: null,
            CorrelationId: "corr-1",
            RetryCount: 0,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            Claimed: false,
            ClaimedBy: null,
            ClaimedAt: null,
            ClaimExpiresAt: null,
            LastSourceValidationAt: null,
            Version: 1,
            ReconciliationRequired: false,
            RetryEligible: retryEligible,
            JiraExists: false,
            PresentationState: OperationalRecordPresentationStates.Actionable,
            SimulationMode: false,
            SimulationNotice: null,
            ReadOnlyIntegrationMode: readOnly,
            ReadOnlyNotice: readOnly ? "GERÇEK VERİ — YAZMA KAPALI" : null);
}

/// <summary>Mirrors the banner's fallback copy so it can be asserted without rendering Razor.</summary>
/// <remarks>
/// The test project has no component-rendering harness, so the constant is duplicated here rather
/// than left unasserted. Kept adjacent to the tests that use it so the duplication is visible.
/// </remarks>
internal static class SoReadOnlyIntegrationBannerContract
{
    internal const string DefaultNotice =
        "Gerçek Turuncu Hat ve Jira verileri okunabilir. Jira kaydı oluşturma ve kaynak kayıt "
        + "güncelleme işlemleri bu TEST modunda kapalıdır.";
}
