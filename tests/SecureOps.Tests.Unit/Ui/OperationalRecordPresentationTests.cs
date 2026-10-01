using FluentAssertions;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the operator-facing presentation of an operational record.
/// </summary>
/// <remarks>
/// The screen used to derive its own categories from workflow state and action availability, so the
/// list and the record page could disagree about what a record needed. The backend now owns the
/// category, and these tests hold the UI to the documented mapping rather than to English prose that
/// travels alongside it.
/// </remarks>
public sealed class OperationalRecordPresentationTests
{
    [Theory]
    [InlineData(OperationalRecordPresentationStates.NeedsAttention, "İnceleme Gerekiyor")]
    [InlineData(OperationalRecordPresentationStates.Actionable, "Jira'ya Aktarılabilir")]
    [InlineData(OperationalRecordPresentationStates.InProgress, "İşlemde")]
    [InlineData(OperationalRecordPresentationStates.SourceOpen, "Jira var, kaynak açık")]
    [InlineData(OperationalRecordPresentationStates.Completed, "Tamamlandı")]
    public void PresentationLabels_FollowTheDocumentedMapping(string state, string expected)
    {
        OperationalRecordView.PresentationLabel(state).Should().Be(expected);
    }

    [Fact]
    public void UnknownPresentationState_IsReportedAsUnknownRatherThanGuessed()
    {
        // A category this UI has never seen is a contract gap. Quietly folding it into one of the
        // four would hide that while asserting something about the record that may be false.
        OperationalRecordView.PresentationLabel("SomethingAddedLater").Should().Be("Durum bilinmiyor");
        OperationalRecordView.PresentationLabel(null).Should().Be("Durum bilinmiyor");
    }

    [Fact]
    public void OperatorSteps_AreTheDocumentedSequence()
    {
        OperationalRecordView.OperatorSteps.Should().Equal(
            "Kaydı İncele",
            "Jira Taslağını Önizle",
            "Bilgileri Doğrula",
            "Jira Kaydı Oluştur",
            "Kaynak Kaydı Tamamla");
    }

    [Theory]
    [InlineData(OperationalRecordWorkflowState.Eligible, 0)]
    [InlineData(OperationalRecordWorkflowState.Previewed, 2)]
    [InlineData(OperationalRecordWorkflowState.CreatingJira, 3)]
    [InlineData(OperationalRecordWorkflowState.JiraCreateFailed, 3)]
    [InlineData(OperationalRecordWorkflowState.JiraCreated, 4)]
    [InlineData(OperationalRecordWorkflowState.OperationalRecordCloseFailed, 4)]
    [InlineData(OperationalRecordWorkflowState.Completed, 4)]
    public void CurrentStep_ComesFromDurableStateNotFromWhatWasClicked(
        OperationalRecordWorkflowState state,
        int expected)
    {
        OperationalRecordView.CurrentStep(Record(state)).Should().Be(expected);
    }

    [Fact]
    public void CompletedLabelWithoutVerifiedEvidence_DoesNotClaimSourceClosure()
    {
        OperationalRecordResponse legacy = Record(OperationalRecordWorkflowState.Completed);
        OperationalRecordView.StateLabel(legacy).Should().Contain("doğrulanmadı");
        OperationalRecordView.CurrentStep(legacy with { SourceClosureVerified = true }).Should().Be(5);
    }

    [Fact]
    public void SourceCloseFailure_KeepsThePersistedJiraKeyAndOffersOnlyRetry()
    {
        // The issue exists. Offering "create" here is how a duplicate gets made, and losing the key
        // from the screen is how an operator concludes nothing happened.
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.OperationalRecordCloseFailed,
            jiraIssueKey: "OPS-1234",
            jiraExists: true,
            retryEligible: true);

        OperationalRecordView.HasJira(record).Should().BeTrue();
        record.JiraIssueKey.Should().Be("OPS-1234");

        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(record);
        actions.Create.Should().BeFalse();
        actions.Preview.Should().BeFalse();
        actions.Retry.Should().BeTrue();
    }

    [Fact]
    public void UnknownOutcome_BlocksEveryActionUntilReconciled()
    {
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.CreatingJira,
            reconciliationRequired: true);

        OperationalRecordView.OutcomeUnknown(record).Should().BeTrue();

        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(record);
        actions.Create.Should().BeFalse();
        actions.Retry.Should().BeFalse();
        actions.BlockedReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void UnknownOutcome_IsExplainedInOperationalLanguage()
    {
        OperationalRecordView.StateDetail(OperationalRecordWorkflowState.CreatingJira)
            .Should().Be(
                "Jira isteğinin sonucu kesin olarak doğrulanamadı. Yeni kayıt oluşturmadan önce "
                + "uzlaştırma gereklidir.");
    }

    [Fact]
    public void SourceCloseFailure_IsExplainedAsSafeToRetryWithoutRecreatingJira()
    {
        OperationalRecordView.StateDetail(OperationalRecordWorkflowState.OperationalRecordCloseFailed)
            .Should().Be(
                "Jira kaydı oluşturuldu ancak kaynak kayıt tamamlanamadı. Jira tekrar oluşturulmadan "
                + "kaynak tamamlama işlemi yeniden denenebilir.");
    }

    [Fact]
    public void SimulationMode_IsOffUnlessTheBackendSaysOtherwise()
    {
        // The UI never decides this for itself. A client-side simulation would be a screen that
        // lies about what the server is going to do.
        Record(OperationalRecordWorkflowState.Eligible).SimulationMode.Should().BeFalse();
    }

    private static OperationalRecordResponse Record(
        OperationalRecordWorkflowState state,
        string? jiraIssueKey = null,
        bool jiraExists = false,
        bool retryEligible = false,
        bool reconciliationRequired = false,
        string presentationState = OperationalRecordPresentationStates.Actionable) =>
        new(
            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SourceRecordId: "TH-1",
            OrCode: "OR-1",
            Title: "Sunucu erişim talebi",
            Description: "Örnek açıklama",
            Requester: "kullanici01",
            CreatedAt: null,
            Environment: "TEST",
            ServerReference: null,
            ApplicationReference: null,
            Classification: OperationalRecordClassification.ServerRequest,
            JiraEligible: true,
            EligibilityReason: "Eligible",
            WorkflowState: state,
            JiraIssueKey: jiraIssueKey,
            LastErrorCode: null,
            CorrelationId: "corr-1",
            RetryCount: 0,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            Claimed: false,
            ClaimedBy: null,
            ClaimedAt: null,
            ClaimExpiresAt: null,
            LastSourceValidationAt: null,
            Version: 3,
            ReconciliationRequired: reconciliationRequired,
            RetryEligible: retryEligible,
            JiraExists: jiraExists,
            PresentationState: presentationState);
}
