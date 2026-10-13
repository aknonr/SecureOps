using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using E = SecureOps.Domain.ServiceAccounts.ServiceAccountRules.Errors;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>OLAY-2a: <see cref="ServiceAccountRules.EvaluateClose"/> lists every missing close condition; <see cref="ServiceAccountRules.ValidateClose"/> keeps its first-code behaviour.</summary>
public sealed class ServiceAccountCloseReadinessTests
{
    private static readonly DateOnly _actual = new(2026, 9, 10);
    private static readonly ServiceAccountActionType[] _types = Enum.GetValues<ServiceAccountActionType>();

    [Fact]
    public void ValidateClose_MatchesThePreviousImplementation_ForEveryBranchAndSingleAction()
    {
        string?[] reasons = [null, " ", "gerekçe"];
        foreach (ServiceAccountRequestStatus status in Enum.GetValues<ServiceAccountRequestStatus>())
        {
            foreach (ServiceAccountCloseOutcome outcome in Enum.GetValues<ServiceAccountCloseOutcome>())
            {
                foreach (ServiceAccountActionType type in _types)
                {
                    foreach (string? reason in reasons)
                    {
                        foreach (bool owned in new[] { false, true })
                        {
                            AssertSame(status, type, outcome, reason, [], owned);
                        }
                    }
                }
            }
        }

        ActionFacts[] pool = [.. AllActions()];
        foreach (ServiceAccountActionType type in _types)
        {
            foreach (ActionFacts action in pool)
            {
                AssertSame(ServiceAccountRequestStatus.Open, type, ServiceAccountCloseOutcome.Completed, null, [action], false);
            }
        }

        var random = new Random(20261013);
        for (int i = 0; i < 4000; i++)
        {
            ActionFacts[] pair = [pool[random.Next(pool.Length)], pool[random.Next(pool.Length)]];
            AssertSame(ServiceAccountRequestStatus.Open, _types[random.Next(_types.Length)], ServiceAccountCloseOutcome.Completed, null, pair, false);
        }
    }

    [Fact]
    public void DeletionSubCodes_AreEmptyExactlyWhenTheActionIsAVerifiedClosure()
    {
        foreach (ActionFacts action in AllActions().Where(a => a.ActionType == ServiceAccountActionType.Deletion && ServiceAccountRules.IsPerformedReport(a)))
        {
            IReadOnlyList<CloseGap> gaps = Evaluate(ServiceAccountActionType.Deletion, action);
            if (ServiceAccountRules.IsVerifiedClosure(action))
            {
                gaps.Should().BeEmpty();
            }
            else
            {
                gaps.Should().ContainSingle().Which.SubCodes.Should().NotBeEmpty($"{action} must say what is missing");
            }
        }
    }

    [Fact]
    public void DeletionWithoutAnyAction_ListsBothTheMissingReportAndTheMissingVerifiedClosure()
    {
        IReadOnlyList<CloseGap> gaps = Evaluate(ServiceAccountActionType.Deletion);

        gaps.Select(g => g.Code).Should().Equal(E.NoMatchingAction, E.VerifiedClosureRequired);
        gaps[0].Remedy.Should().Be(CloseRemedy.ReportAction);
        gaps[0].ExpectedActionType.Should().Be(ServiceAccountActionType.Deletion);
        gaps[1].Remedy.Should().Be(CloseRemedy.VerifyAction);
        gaps[1].SubCodes.Should().BeEmpty("there is no candidate action to describe");
        gaps[1].RelatedActionId.Should().BeNull();
    }

    [Fact]
    public void DeletionReportedNotVerified_NamesEveryUnmetConditionOfThatAction()
    {
        var id = Guid.NewGuid();
        ActionFacts reported = new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate,
            _actual, null, false, true, false, false, id);

        CloseGap gap = Evaluate(ServiceAccountActionType.Deletion, reported).Should().ContainSingle().Subject;

        gap.Code.Should().Be(E.VerifiedClosureRequired);
        gap.RelatedActionId.Should().Be(id);
        gap.SubCodes.Should().Equal(E.NotVerified, E.NotClosureKind, E.VerifierRequired, E.OrRequiredForDeletion);
    }

    [Fact]
    public void DeletionWithSeveralCandidates_DescribesTheClosestOne_AndIgnoresVoidedOrPlanned()
    {
        Guid far = Guid.NewGuid(), close = Guid.NewGuid(), voided = Guid.NewGuid(), planned = Guid.NewGuid();
        ActionFacts[] actions =
        [
            new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate, null, null, false, false, false, false, far),
            new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, ServiceAccountRecordKind.Closure, _actual, _actual.AddDays(1), true, true, true, true, voided),
            new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Planned, ServiceAccountRecordKind.Closure, _actual, null, true, true, true, false, planned),
            new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, ServiceAccountRecordKind.Closure, _actual, _actual.AddDays(1), true, true, false, false, close),
        ];

        CloseGap gap = Evaluate(ServiceAccountActionType.Deletion, actions).Should().ContainSingle().Subject;

        gap.RelatedActionId.Should().Be(close);
        gap.SubCodes.Should().Equal(E.OrRequiredForDeletion);
    }

    [Fact]
    public void VerificationBeforeActionDate_IsNamed()
    {
        ActionFacts early = new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, ServiceAccountRecordKind.Closure,
            _actual, _actual.AddDays(-1), true, true, true, false);

        Evaluate(ServiceAccountActionType.Deletion, early).Single().SubCodes.Should().Equal(E.VerificationBeforeAction);
    }

    [Theory]
    [InlineData(ServiceAccountActionType.PasswordChange, ServiceAccountActionType.PasswordChange, true)]
    [InlineData(ServiceAccountActionType.PasswordChange, ServiceAccountActionType.Review, false)]
    [InlineData(ServiceAccountActionType.GmsaHandover, ServiceAccountActionType.GmsaConversion, true)]
    [InlineData(ServiceAccountActionType.GmsaConversion, ServiceAccountActionType.GmsaHandover, false)]
    [InlineData(ServiceAccountActionType.Evaluate, ServiceAccountActionType.Deletion, true)]
    public void Completed_NeedsAMatchingPerformedReport_NotAVerification(ServiceAccountActionType request, ServiceAccountActionType reported, bool canClose)
    {
        ActionFacts performed = new(reported, ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate, _actual, null, false, false, false, false);

        IReadOnlyList<CloseGap> gaps = Evaluate(request, performed);

        if (canClose)
        {
            gaps.Should().BeEmpty();
        }
        else
        {
            gaps.Should().ContainSingle().Which.Should().Be(new CloseGap(E.NoMatchingAction, CloseRemedy.ReportAction, gaps[0].SubCodes, request));
            gaps[0].SubCodes.Should().BeEmpty();
        }
    }

    [Fact]
    public void OtherBranches_ReturnOneGapWithItsRemedy()
    {
        ServiceAccountRules.EvaluateClose(ServiceAccountRequestStatus.Closed, ServiceAccountActionType.Deletion, ServiceAccountCloseOutcome.Completed, null, [])
            .Should().ContainSingle().Which.Should().Match<CloseGap>(g => g.Code == E.AlreadyClosed && g.Remedy == CloseRemedy.None);
        ServiceAccountRules.EvaluateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.Deletion, ServiceAccountCloseOutcome.Cancelled, null, [])
            .Should().ContainSingle().Which.Should().Match<CloseGap>(g => g.Code == E.ReasonRequired && g.Remedy == CloseRemedy.GiveReason);
        ServiceAccountRules.EvaluateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.Deletion, ServiceAccountCloseOutcome.NotNeeded, "gerek yok", [])
            .Should().BeEmpty();
        ServiceAccountRules.EvaluateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.OwnershipConfirmation, ServiceAccountCloseOutcome.Completed, null, [])
            .Should().ContainSingle().Which.Should().Match<CloseGap>(g => g.Code == E.OwnershipNotConfirmed && g.Remedy == CloseRemedy.ConfirmOwnership);
        ServiceAccountRules.EvaluateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.OwnershipConfirmation, ServiceAccountCloseOutcome.Completed, null, [], true)
            .Should().BeEmpty();
    }

    private static IReadOnlyList<CloseGap> Evaluate(ServiceAccountActionType requestType, params ActionFacts[] actions) =>
        ServiceAccountRules.EvaluateClose(ServiceAccountRequestStatus.Open, requestType, ServiceAccountCloseOutcome.Completed, null, actions);

    private static void AssertSame(ServiceAccountRequestStatus status, ServiceAccountActionType type, ServiceAccountCloseOutcome outcome, string? reason,
        ActionFacts[] actions, bool owned)
    {
        string? expected = PreviousValidateClose(status, type, outcome, reason, actions, owned);
        ServiceAccountRules.ValidateClose(status, type, outcome, reason, actions, owned)
            .Should().Be(expected, $"{status}/{type}/{outcome}/{reason}/{string.Join(",", actions.Select(a => a.ToString()))}");
        IReadOnlyList<CloseGap> gaps = ServiceAccountRules.EvaluateClose(status, type, outcome, reason, actions, owned);
        (gaps.Count == 0).Should().Be(expected is null, "EvaluateClose is empty exactly when ValidateClose accepts");
    }

    private static IEnumerable<ActionFacts> AllActions()
    {
        DateOnly?[] actuals = [null, _actual];
        DateOnly?[] verifieds = [null, _actual.AddDays(-1), _actual.AddDays(1)];
        bool[] flags = [false, true];
        foreach (ServiceAccountActionType type in _types)
        {
            foreach (ServiceAccountActionResult result in Enum.GetValues<ServiceAccountActionResult>())
            {
                foreach (ServiceAccountRecordKind kind in Enum.GetValues<ServiceAccountRecordKind>())
                {
                    foreach (DateOnly? actual in actuals)
                    {
                        foreach (DateOnly? verified in verifieds)
                        {
                            foreach (bool verifier in flags)
                            {
                                foreach (bool evidence in flags)
                                {
                                    foreach (bool or in flags)
                                    {
                                        foreach (bool voided in flags)
                                        {
                                            yield return new ActionFacts(type, result, kind, actual, verified, verifier, evidence, or, voided, Guid.NewGuid());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Verbatim copy of <c>ValidateClose</c> before OLAY-2a (master 633eed9); the reference for behaviour preservation.</summary>
    private static string? PreviousValidateClose(ServiceAccountRequestStatus status, ServiceAccountActionType requestType,
        ServiceAccountCloseOutcome outcome, string? reason, IEnumerable<ActionFacts> linkedActions, bool ownershipConfirmed)
    {
        if (status == ServiceAccountRequestStatus.Closed)
        {
            return E.AlreadyClosed;
        }

        if (outcome != ServiceAccountCloseOutcome.Completed)
        {
            return string.IsNullOrWhiteSpace(reason) ? E.ReasonRequired : null;
        }

        if (requestType == ServiceAccountActionType.OwnershipConfirmation)
        {
            return ownershipConfirmed ? null : E.OwnershipNotConfirmed;
        }

        ActionFacts[] matching = [.. linkedActions.Where(a => ServiceAccountRules.IsPerformedReport(a) && PreviousMatches(requestType, a.ActionType))];
        if (matching.Length == 0)
        {
            return E.NoMatchingAction;
        }

        return requestType == ServiceAccountActionType.Deletion && !matching.Any(ServiceAccountRules.IsVerifiedClosure)
            ? E.VerifiedClosureRequired
            : null;
    }

    private static bool PreviousMatches(ServiceAccountActionType requestType, ServiceAccountActionType actionType) =>
        requestType == actionType
        || requestType == ServiceAccountActionType.GmsaHandover && actionType == ServiceAccountActionType.GmsaConversion
        || requestType == ServiceAccountActionType.Evaluate;
}
