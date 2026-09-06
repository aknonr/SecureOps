using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Covers the rules that decide what an operator is allowed to do with a record.
/// </summary>
/// <remarks>
/// The duplicate-Jira cases are the reason this file exists. Two states carry a Jira issue that
/// already exists or may already exist — <c>OperationalRecordCloseFailed</c> and <c>CreatingJira</c>
/// — and in both, offering "create" would risk a second issue. Those are pinned first.
/// </remarks>
public sealed class OperationalRecordViewTests
{
    private const string Me = "demo:platform-admin";
    private const string Other = "demo:team-lead";

    [Theory]
    [InlineData(OperationalRecordWorkflowState.JiraCreateFailed)]
    [InlineData(OperationalRecordWorkflowState.CreatingJira)]
    [InlineData(OperationalRecordWorkflowState.Eligible)]
    public void ReconciliationEvidence_OverridesDefinitiveStagePresentation(OperationalRecordWorkflowState state)
    {
        OperationalRecordResponse record = Record(state, reconciliationRequired: true);
        Assert.Equal("Jira sonucu belirsiz", OperationalRecordView.StateLabel(record));
        Assert.Equal(SoStatusBadge.BadgeTone.Caution, OperationalRecordView.StateTone(record));
        Assert.Contains("mutabakat", OperationalRecordView.StateDetail(record));
        Assert.False(OperationalRecordView.ActionsFor(record).Create);
    }

    private static OperationalRecordResponse Record(
        OperationalRecordWorkflowState state,
        string? jiraKey = null,
        bool eligible = true,
        bool claimed = false,
        DateTimeOffset? claimExpires = null,
        string? claimedBy = null,
        bool reconciliationRequired = false,
        bool retryEligible = true) => new(
        Id: Guid.NewGuid(),
        SourceRecordId: "SRC-1",
        OrCode: "OR-1",
        Title: "Sunucu erişimi",
        Description: "Açıklama",
        Requester: "ornek.kullanici",
        CreatedAt: DateTimeOffset.UtcNow.AddHours(-3),
        Environment: "TEST",
        ServerReference: null,
        ApplicationReference: null,
        Classification: OperationalRecordClassification.ServerRequest,
        JiraEligible: eligible,
        EligibilityReason: "Onaylı kural",
        WorkflowState: state,
        JiraIssueKey: jiraKey,
        LastErrorCode: null,
        CorrelationId: "corr-1",
        RetryCount: 0,
        UpdatedAt: DateTimeOffset.UtcNow,
        Claimed: claimed,
        ClaimedBy: claimedBy,
        ClaimedAt: claimedBy is null ? null : DateTimeOffset.UtcNow.AddMinutes(-5),
        ClaimExpiresAt: claimExpires,
        LastSourceValidationAt: DateTimeOffset.UtcNow.AddMinutes(-1),
        Version: 3,
        ReconciliationRequired: reconciliationRequired,
        RetryEligible: retryEligible,
        JiraExists: jiraKey is not null);

    // ---------------------------------------------------------------- duplicate safety

    [Fact]
    public void CloseFailed_OffersRetryButNeverCreate()
    {
        // The Jira issue exists; only the source close is outstanding. Creating again would
        // duplicate it.
        OperationalRecordView.Actions actions =
            OperationalRecordView.ActionsFor(Record(OperationalRecordWorkflowState.OperationalRecordCloseFailed, "FAKE-1"));

        Assert.False(actions.Create);
        Assert.True(actions.Retry);
    }

    [Fact]
    public void CloseFailed_IsNotTonedAsCritical()
    {
        // Painting a partial success as an error invites someone to "fix" it with a second issue.
        Assert.Equal(
            SoStatusBadge.BadgeTone.Caution,
            OperationalRecordView.StateTone(OperationalRecordWorkflowState.OperationalRecordCloseFailed));
    }

    [Fact]
    public void UnknownOutcome_OffersNothingAtAll()
    {
        OperationalRecordResponse record = Record(OperationalRecordWorkflowState.CreatingJira);
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(record);

        Assert.True(OperationalRecordView.OutcomeUnknown(record));
        Assert.False(actions.Create);
        Assert.False(actions.Retry);
        Assert.False(actions.Preview);
        Assert.NotNull(actions.BlockedReason);
    }

    [Fact]
    public void UnknownOutcome_IsFalseOnceAnIssueKeyExists()
    {
        // With a key persisted the outcome is no longer unknown, whatever stage it is in.
        Assert.False(OperationalRecordView.OutcomeUnknown(
            Record(OperationalRecordWorkflowState.CreatingJira, "FAKE-9")));
    }

    [Fact]
    public void Completed_OffersNothing()
    {
        OperationalRecordView.Actions actions =
            OperationalRecordView.ActionsFor(Record(OperationalRecordWorkflowState.Completed, "FAKE-2"));

        Assert.False(actions.Create);
        Assert.False(actions.Retry);
        Assert.False(actions.Preview);
    }

    [Fact]
    public void NoStateEverOffersCreateWhenAJiraKeyExists()
    {
        // The property that actually matters, asserted across the whole state machine rather than
        // case by case.
        foreach (OperationalRecordWorkflowState state in Enum.GetValues<OperationalRecordWorkflowState>())
        {
            OperationalRecordView.Actions actions =
                OperationalRecordView.ActionsFor(Record(state, jiraKey: "FAKE-7"));

            Assert.False(actions.Create, $"{state} offered create with an existing Jira key.");
        }
    }

    // ---------------------------------------------------------------- ordinary flow

    [Theory]
    [InlineData(OperationalRecordWorkflowState.Eligible)]
    [InlineData(OperationalRecordWorkflowState.Previewed)]
    public void EligibleAndPreviewed_AllowPreviewAndCreate(OperationalRecordWorkflowState state)
    {
        OperationalRecordView.Actions actions = OperationalRecordView.ActionsFor(Record(state));

        Assert.True(actions.Preview);
        Assert.True(actions.Create);
        Assert.False(actions.Retry);
        Assert.Null(actions.BlockedReason);
    }

    [Fact]
    public void JiraCreateFailed_AllowsRetryOnly()
    {
        OperationalRecordView.Actions actions =
            OperationalRecordView.ActionsFor(Record(OperationalRecordWorkflowState.JiraCreateFailed));

        Assert.True(actions.Retry);
        Assert.False(actions.Create);
    }

    [Fact]
    public void IneligibleRecord_OffersNothingRegardlessOfState()
    {
        OperationalRecordView.Actions actions =
            OperationalRecordView.ActionsFor(Record(OperationalRecordWorkflowState.Eligible, eligible: false));

        Assert.False(actions.Preview);
        Assert.False(actions.Create);
        Assert.False(actions.Retry);
        Assert.NotNull(actions.BlockedReason);
    }

    [Fact]
    public void NeedsManualReview_IsBlockedAndFlaggedForAttention()
    {
        OperationalRecordResponse record = Record(OperationalRecordWorkflowState.NeedsManualReview);

        Assert.False(OperationalRecordView.ActionsFor(record).Create);
        Assert.True(OperationalRecordView.NeedsAttention(record));
    }

    [Theory]
    [InlineData(OperationalRecordWorkflowState.JiraCreateFailed)]
    [InlineData(OperationalRecordWorkflowState.OperationalRecordCloseFailed)]
    [InlineData(OperationalRecordWorkflowState.CreatingJira)]
    [InlineData(OperationalRecordWorkflowState.NeedsManualReview)]
    public void StuckStates_NeedAttention(OperationalRecordWorkflowState state)
    {
        Assert.True(OperationalRecordView.NeedsAttention(Record(state)));
    }

    [Theory]
    [InlineData(OperationalRecordWorkflowState.Completed)]
    [InlineData(OperationalRecordWorkflowState.Eligible)]
    public void SettledStates_DoNotNeedAttention(OperationalRecordWorkflowState state)
    {
        Assert.False(OperationalRecordView.NeedsAttention(Record(state)));
    }

    // ---------------------------------------------------------------- ownership

    [Fact]
    public void NoClaim_AndNoHistory_ReadsAsAvailable()
    {
        Assert.Equal(
            OperationalRecordView.Ownership.Available,
            OperationalRecordView.OwnershipOf(
                Record(OperationalRecordWorkflowState.Eligible), DateTimeOffset.UtcNow, Me));
    }

    [Fact]
    public void LiveClaimByCurrentActor_ReadsAsMine()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible,
            claimed: true, claimExpires: now.AddMinutes(2), claimedBy: Me);

        Assert.Equal(OperationalRecordView.Ownership.Mine, OperationalRecordView.OwnershipOf(record, now, Me));
    }

    [Fact]
    public void LiveClaimByAnotherActor_ReadsAsOther()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible,
            claimed: true, claimExpires: now.AddMinutes(2), claimedBy: Other);

        Assert.Equal(OperationalRecordView.Ownership.Other, OperationalRecordView.OwnershipOf(record, now, Me));
    }

    [Fact]
    public void ExpiredClaimRetainingOwner_IsNeverPresentedAsOwned()
    {
        // The case the contract warns about: claimed=false while claimedBy is still populated.
        // Reading ownership from claimedBy alone would block a record nobody holds.
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible,
            claimed: false, claimedBy: Me);

        OperationalRecordView.Ownership owner =
            OperationalRecordView.OwnershipOf(record, DateTimeOffset.UtcNow, Me);

        Assert.Equal(OperationalRecordView.Ownership.Lapsed, owner);
        Assert.NotEqual(OperationalRecordView.Ownership.Mine, owner);
        Assert.NotEqual(OperationalRecordView.Ownership.Other, owner);
    }

    [Fact]
    public void ExpiredClaimRetainingAnotherOwner_IsAlsoLapsed()
    {
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible,
            claimed: false, claimedBy: Other);

        Assert.Equal(
            OperationalRecordView.Ownership.Lapsed,
            OperationalRecordView.OwnershipOf(record, DateTimeOffset.UtcNow, Me));
    }

    [Fact]
    public void LiveFlagWithLapsedLease_ReadsAsLapsed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible,
            claimed: true, claimExpires: now.AddMinutes(-1), claimedBy: Other);

        Assert.Equal(OperationalRecordView.Ownership.Lapsed, OperationalRecordView.OwnershipOf(record, now, Me));
    }

    [Fact]
    public void UnknownCurrentActor_NeverReportsAClaimAsMine()
    {
        // If /identity/me could not be read, the safe answer is "not yours".
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible,
            claimed: true, claimExpires: now.AddMinutes(2), claimedBy: Me);

        Assert.Equal(OperationalRecordView.Ownership.Other, OperationalRecordView.OwnershipOf(record, now, null));
    }

    [Fact]
    public void OwnerComparison_IsCaseInsensitive()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible,
            claimed: true, claimExpires: now.AddMinutes(2), claimedBy: "DEMO:Platform-Admin");

        Assert.Equal(OperationalRecordView.Ownership.Mine, OperationalRecordView.OwnershipOf(record, now, Me));
    }

    // ------------------------------------------------- reconciliation and retry eligibility

    [Fact]
    public void ReconciliationRequired_BlocksCreateInAnyState()
    {
        foreach (OperationalRecordWorkflowState state in Enum.GetValues<OperationalRecordWorkflowState>())
        {
            OperationalRecordResponse record = Record(state, reconciliationRequired: true);

            Assert.False(
                OperationalRecordView.ActionsFor(record).Create,
                $"{state} offered create while reconciliation was required.");
        }
    }

    [Fact]
    public void ReconciliationRequired_OffersRetryOnlyWhenServerAllowsIt()
    {
        OperationalRecordResponse allowed = Record(
            OperationalRecordWorkflowState.CreatingJira, reconciliationRequired: true, retryEligible: true);
        OperationalRecordResponse blocked = Record(
            OperationalRecordWorkflowState.CreatingJira, reconciliationRequired: true, retryEligible: false);

        Assert.True(OperationalRecordView.ActionsFor(allowed).Retry);
        Assert.False(OperationalRecordView.ActionsFor(blocked).Retry);
        Assert.NotNull(OperationalRecordView.ActionsFor(blocked).BlockedReason);
    }

    [Fact]
    public void ReconciliationRequired_IsSurfacedAsUnknownOutcomeAndNeedsAttention()
    {
        OperationalRecordResponse record = Record(
            OperationalRecordWorkflowState.Eligible, reconciliationRequired: true);

        Assert.True(OperationalRecordView.OutcomeUnknown(record));
        Assert.True(OperationalRecordView.NeedsAttention(record));
    }

    [Fact]
    public void RetryIsNeverOfferedWhenTheServerSaysIneligible()
    {
        // Retry eligibility is authoritative, not inferred from the stage.
        foreach (OperationalRecordWorkflowState state in Enum.GetValues<OperationalRecordWorkflowState>())
        {
            OperationalRecordResponse record = Record(state, retryEligible: false);

            Assert.False(
                OperationalRecordView.ActionsFor(record).Retry,
                $"{state} offered retry while the server reported it ineligible.");
        }
    }

    [Fact]
    public void JiraExists_BlocksCreateEvenWithoutAKey()
    {
        // jiraExists is authoritative on its own; a missing key must not re-enable create.
        OperationalRecordResponse record = Record(OperationalRecordWorkflowState.Eligible) with
        {
            JiraExists = true,
            JiraIssueKey = null
        };

        Assert.True(OperationalRecordView.HasJira(record));
        Assert.False(OperationalRecordView.ActionsFor(record).Create);
    }

    // ---------------------------------------------------------------- labelling

    [Fact]
    public void EveryWorkflowState_HasALabel()
    {
        foreach (OperationalRecordWorkflowState state in Enum.GetValues<OperationalRecordWorkflowState>())
        {
            string label = OperationalRecordView.StateLabel(state);

            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(state.ToString(), label);
        }
    }
}

/// <summary>
/// Covers the one place a stable code alone is not enough to choose the right message.
/// </summary>
public sealed class ReconciliationProblemTests
{
    private static ProblemDetailsPayload Payload(string code, string? stage, bool? retryable) => new()
    {
        Code = code,
        Stage = stage,
        Retryable = retryable,
        CorrelationId = "corr-x"
    };

    [Fact]
    public void WorkflowConflict_AtReconciliationStage_IsNotOfferedAsRetryable()
    {
        // The critical case: an unknown Jira outcome must never render as "try again".
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            Payload(OperationalErrorCodes.WorkflowConflict, "jira-reconciliation", retryable: false));

        Assert.False(problem.Retryable);
        Assert.True(problem.RequiresRefresh);
        Assert.Contains("doğrulan", problem.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowConflict_AtReconciliationStage_TellsTheOperatorNotToCreateAnother()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            Payload(OperationalErrorCodes.WorkflowConflict, "jira-reconciliation", retryable: false));

        Assert.Contains(
            problem.NextSteps,
            step => step.Contains("mükerrer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkflowConflict_AtOtherStages_KeepsTheOrdinaryConflictMessage()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            Payload(OperationalErrorCodes.WorkflowConflict, "workflow", retryable: true));

        Assert.DoesNotContain("mükerrer", string.Join(" ", problem.NextSteps), StringComparison.OrdinalIgnoreCase);
        Assert.True(problem.Retryable);
    }

    [Fact]
    public void AlreadyClaimed_IsDistinctFromReconciliation()
    {
        UiProblem claimed = UiProblemFactory.FromResponse(
            409,
            Payload(OperationalErrorCodes.OperationalRecordAlreadyClaimed, "claim", retryable: true));
        UiProblem reconcile = UiProblemFactory.FromResponse(
            409,
            Payload(OperationalErrorCodes.WorkflowConflict, "jira-reconciliation", retryable: false));

        Assert.NotEqual(claimed.Title, reconcile.Title);
        Assert.True(claimed.Retryable);
        Assert.False(reconcile.Retryable);
    }

    [Fact]
    public void SourceChanged_IsItsOwnStateAndNotAValidationError()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            Payload(OperationalErrorCodes.OperationalRecordChanged, "source-validation", retryable: false));

        Assert.Equal(UiProblemKind.Conflict, problem.Kind);
        Assert.True(problem.RequiresRefresh);
    }

    [Fact]
    public void AlreadyTransferred_DoesNotInviteAnotherCreate()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            Payload(OperationalErrorCodes.JiraAlreadyCreated, "jira-create", retryable: false));

        Assert.False(problem.Retryable);
    }
}
