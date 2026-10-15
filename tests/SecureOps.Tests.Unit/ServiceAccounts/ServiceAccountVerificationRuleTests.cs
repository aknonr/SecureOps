using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// G-39: <see cref="ServiceAccountRules.EvaluateVerification"/> is the single source of the verify rule. Its first code must equal what the
/// verify rule returned before G-39 (<see cref="LegacyValidateVerification"/>, a frozen copy) on every combination of inputs.
/// </summary>
public sealed class ServiceAccountVerificationRuleTests
{
    private static readonly DateOnly _today = new(2026, 9, 19);

    [Fact]
    public void ValidateVerification_EqualsThePreG39Rule_OnEveryCombination()
    {
        int compared = 0;
        foreach (ActionFacts facts in AllFacts())
        {
            foreach (DateOnly verifiedOn in new DateOnly[] { new(2026, 9, 9), new(2026, 9, 10), new(2026, 9, 11), _today, new(2026, 9, 20) })
            {
                foreach ((bool verifier, bool evidence) in new[] { (true, true), (true, false), (false, true), (false, false) })
                {
                    string? legacy = LegacyValidateVerification(facts, verifiedOn, verifier, evidence, _today);
                    ServiceAccountRules.ValidateVerification(facts, verifiedOn, verifier, evidence, _today).Should().Be(legacy, facts.ToString());
                    IReadOnlyList<string> all = ServiceAccountRules.EvaluateVerification(facts, verifiedOn, verifier, evidence, _today);
                    (all.Count == 0 ? null : all[0]).Should().Be(legacy);
                    all.Should().OnlyHaveUniqueItems();
                    compared++;
                }
            }
        }

        compared.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void EvaluateVerification_WithoutADate_SkipsOnlyTheDateChecks_AndListsEveryOtherGap()
    {
        ActionFacts deletion = new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Closure,
            new(2026, 9, 10), null, false, false, false, false);
        ServiceAccountRules.EvaluateVerification(deletion, null, true, false, _today).Should()
            .Equal(ServiceAccountRules.Errors.EvidenceRequired, ServiceAccountRules.Errors.OrRequiredForDeletion);
        ServiceAccountRules.EvaluateVerification(deletion with { ActualOn = null, Voided = true }, null, true, true, _today).Should()
            .Equal(ServiceAccountRules.Errors.Voided, ServiceAccountRules.Errors.ActualDateRequired, ServiceAccountRules.Errors.OrRequiredForDeletion);
        ServiceAccountRules.EvaluateVerification(deletion with { HasOrReference = true }, null, true, true, _today).Should().BeEmpty();
        ServiceAccountRules.EvaluateVerification(deletion with { Result = ServiceAccountActionResult.Planned, HasOrReference = true }, null, true, true, _today)
            .Should().Equal(ServiceAccountRules.Errors.NotPerformed);
    }

    private static IEnumerable<ActionFacts> AllFacts()
    {
        foreach (ServiceAccountActionType type in new[] { ServiceAccountActionType.Deletion, ServiceAccountActionType.PasswordChange, ServiceAccountActionType.GmsaConversion })
        {
            foreach (ServiceAccountActionResult result in Enum.GetValues<ServiceAccountActionResult>())
            {
                foreach (ServiceAccountRecordKind kind in Enum.GetValues<ServiceAccountRecordKind>())
                {
                    foreach (DateOnly? actual in new DateOnly?[] { null, new(2026, 9, 10) })
                    {
                        foreach (bool hasOr in new[] { true, false })
                        {
                            foreach (bool voided in new[] { true, false })
                            {
                                yield return new ActionFacts(type, result, kind, actual, null, false, false, hasOr, voided);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>The verify rule exactly as it was before G-39 (frozen copy; do not edit).</summary>
    private static string? LegacyValidateVerification(ActionFacts current, DateOnly verifiedOn, bool hasVerifier, bool hasEvidence, DateOnly today)
    {
        if (current.Voided)
        {
            return ServiceAccountRules.Errors.Voided;
        }

        if (current.Result != ServiceAccountActionResult.Performed)
        {
            return ServiceAccountRules.Errors.NotPerformed;
        }

        if (current.ActualOn is not { } actual)
        {
            return ServiceAccountRules.Errors.ActualDateRequired;
        }

        if (verifiedOn < actual)
        {
            return ServiceAccountRules.Errors.VerificationBeforeAction;
        }

        if (verifiedOn > today)
        {
            return ServiceAccountRules.Errors.DateInFuture;
        }

        if (!hasVerifier)
        {
            return ServiceAccountRules.Errors.VerifierRequired;
        }

        if (!hasEvidence)
        {
            return ServiceAccountRules.Errors.EvidenceRequired;
        }

        return current.RecordKind == ServiceAccountRecordKind.Closure && current.ActionType == ServiceAccountActionType.Deletion && !current.HasOrReference
            ? ServiceAccountRules.Errors.OrRequiredForDeletion
            : null;
    }
}
