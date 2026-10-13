namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Minimal action state needed by the closure and verification rules.</summary>
/// <param name="ActionType">Controlled action type.</param>
/// <param name="Result">Plan / performed / verified stage.</param>
/// <param name="RecordKind">Intermediate step or closure.</param>
/// <param name="ActualOn">Real action business date, if known.</param>
/// <param name="VerifiedOn">Verification business date, if verified.</param>
/// <param name="HasVerifier">Whether a verifier is recorded.</param>
/// <param name="HasEvidence">Whether evidence (note or file) is recorded.</param>
/// <param name="HasOrReference">Whether an OR record is linked.</param>
/// <param name="Voided">Whether the action was voided.</param>
/// <param name="ActionId">Action identity, when known; lets a close evaluation point at the action that still needs work.</param>
public sealed record ActionFacts(
    ServiceAccountActionType ActionType,
    ServiceAccountActionResult Result,
    ServiceAccountRecordKind RecordKind,
    DateOnly? ActualOn,
    DateOnly? VerifiedOn,
    bool HasVerifier,
    bool HasEvidence,
    bool HasOrReference,
    bool Voided,
    Guid? ActionId = null);

/// <summary>What a person can do to clear one missing close condition; the UI opens the matching step.</summary>
public enum CloseRemedy
{
    /// <summary>Nothing to do on this request (for example it is already closed).</summary>
    None,
    /// <summary>Report a performed action of the expected type on this request.</summary>
    ReportAction,
    /// <summary>Complete or verify an existing action (verification needs the Verify capability).</summary>
    VerifyAction,
    /// <summary>Confirm the account's ownership in the ownership decision flow.</summary>
    ConfirmOwnership,
    /// <summary>Give a close reason.</summary>
    GiveReason
}

/// <summary>One missing close condition.</summary>
/// <param name="Code">Stable rule code (<see cref="ServiceAccountRules.Errors"/>), the same code the close command returns.</param>
/// <param name="Remedy">The step that clears it.</param>
/// <param name="SubCodes">For <see cref="ServiceAccountRules.Errors.VerifiedClosureRequired"/>: the unmet conditions of the closest candidate action.</param>
/// <param name="ExpectedActionType">For <see cref="ServiceAccountRules.Errors.NoMatchingAction"/>: the request type whose performed action is missing.</param>
/// <param name="RelatedActionId">The candidate action the sub-conditions refer to, when one exists.</param>
public sealed record CloseGap(
    string Code,
    CloseRemedy Remedy,
    IReadOnlyList<string> SubCodes,
    ServiceAccountActionType? ExpectedActionType = null,
    Guid? RelatedActionId = null);

/// <summary>Business rules 3-9. Pure, deterministic, shared by API validation, import and reports.</summary>
public static class ServiceAccountRules
{
    /// <summary>Stable error codes; the UI maps them to Turkish field guidance.</summary>
    public static class Errors
    {
        /// <summary>Verification needs an action date.</summary>
        public const string ActualDateRequired = "ActualDateRequired";
        /// <summary>Verification date precedes action date.</summary>
        public const string VerificationBeforeAction = "VerificationBeforeAction";
        /// <summary>Verification needs a verifier.</summary>
        public const string VerifierRequired = "VerifierRequired";
        /// <summary>Verification needs evidence.</summary>
        public const string EvidenceRequired = "EvidenceRequired";
        /// <summary>Deletion closure needs an OR.</summary>
        public const string OrRequiredForDeletion = "OrRequiredForDeletion";
        /// <summary>Only performed actions can be verified.</summary>
        public const string NotPerformed = "ActionNotPerformed";
        /// <summary>Voided actions cannot change.</summary>
        public const string Voided = "ActionVoided";
        /// <summary>A completed close needs a matching performed action.</summary>
        public const string NoMatchingAction = "NoMatchingPerformedAction";
        /// <summary>Deletion request completion needs a verified closure.</summary>
        public const string VerifiedClosureRequired = "VerifiedClosureRequired";
        /// <summary>Non-completed close needs a reason.</summary>
        public const string ReasonRequired = "CloseReasonRequired";
        /// <summary>Request is already closed.</summary>
        public const string AlreadyClosed = "RequestAlreadyClosed";
        /// <summary>Plan range is invalid.</summary>
        public const string PlanRange = "PlanEndBeforeStart";
        /// <summary>Real action/verification dates cannot be in the future.</summary>
        public const string DateInFuture = "ActualDateInFuture";
        /// <summary>Ownership confirmation completes only with a confirmed assignment.</summary>
        public const string OwnershipNotConfirmed = "OwnershipNotConfirmed";
        /// <summary>Only a deletion or a gMSA conversion can be recorded as the account's closure.</summary>
        public const string ClosureKindNotAllowed = "ClosureKindNotAllowed";
        /// <summary>Close sub-condition: the candidate action is reported but not verified.</summary>
        public const string NotVerified = "NotVerified";
        /// <summary>Close sub-condition: the candidate action is not recorded as the account's closure.</summary>
        public const string NotClosureKind = "NotClosureKind";
    }

    /// <summary>Rule 8: a non-void Performed or Verified action is a performed-action report; counted once.</summary>
    public static bool IsPerformedReport(ActionFacts action) =>
        !action.Voided && action.Result is ServiceAccountActionResult.Performed or ServiceAccountActionResult.Verified;

    /// <summary>
    /// Action types that can retire an account: a deletion or an actual gMSA conversion. A review (including a closure
    /// review), an ownership confirmation, a password change, an evaluation or a handover never closes the account.
    /// </summary>
    public static bool CanCloseAccount(ServiceAccountActionType type) =>
        type is ServiceAccountActionType.Deletion or ServiceAccountActionType.GmsaConversion;

    /// <summary>A closure record kind is accepted only for action types that can close an account.</summary>
    public static string? ValidateRecordKind(ServiceAccountActionType type, ServiceAccountRecordKind kind) =>
        kind == ServiceAccountRecordKind.Closure && !CanCloseAccount(type) ? Errors.ClosureKindNotAllowed : null;

    /// <summary>Rule 6: verified closure conditions; record kind alone never closes.</summary>
    public static bool IsVerifiedClosure(ActionFacts action) =>
        !action.Voided
        && CanCloseAccount(action.ActionType)
        && action.Result == ServiceAccountActionResult.Verified
        && action.RecordKind == ServiceAccountRecordKind.Closure
        && action.ActualOn is { } actual
        && action.VerifiedOn is { } verified
        && verified >= actual
        && action.HasVerifier
        && action.HasEvidence
        && (action.ActionType != ServiceAccountActionType.Deletion || action.HasOrReference);

    /// <summary>
    /// Validates a verification of an existing performed action (rule 4: same identity, no new action).
    /// Returns the first violated rule, or null.
    /// </summary>
    public static string? ValidateVerification(ActionFacts current, DateOnly verifiedOn, bool hasVerifier, bool hasEvidence, DateOnly today)
    {
        if (current.Voided)
        {
            return Errors.Voided;
        }

        if (current.Result != ServiceAccountActionResult.Performed)
        {
            return Errors.NotPerformed;
        }

        if (current.ActualOn is not { } actual)
        {
            return Errors.ActualDateRequired;
        }

        if (verifiedOn < actual)
        {
            return Errors.VerificationBeforeAction;
        }

        if (verifiedOn > today)
        {
            return Errors.DateInFuture;
        }

        if (!hasVerifier)
        {
            return Errors.VerifierRequired;
        }

        if (!hasEvidence)
        {
            return Errors.EvidenceRequired;
        }

        return current.RecordKind == ServiceAccountRecordKind.Closure && current.ActionType == ServiceAccountActionType.Deletion && !current.HasOrReference
            ? Errors.OrRequiredForDeletion
            : null;
    }

    /// <summary>Validates a new or updated performed report; real dates cannot be in the future.</summary>
    public static string? ValidateReport(ServiceAccountActionResult result, DateOnly? actualOn, DateOnly today) =>
        result != ServiceAccountActionResult.Planned && actualOn is { } date && date > today ? Errors.DateInFuture : null;

    /// <summary>Plan range validation; single-day plans use start = end.</summary>
    public static string? ValidatePlan(DateOnly? start, DateOnly? end) =>
        start is { } s && end is { } e && e < s ? Errors.PlanRange : null;

    /// <summary>
    /// Rule 9: closing one request is explicit and validated against its own linked actions only.
    /// Completed requires a matching performed action; a deletion request requires a verified closure.
    /// Returns the first missing condition of <see cref="EvaluateClose"/>, or null.
    /// </summary>
    public static string? ValidateClose(ServiceAccountRequestStatus status, ServiceAccountActionType requestType,
        ServiceAccountCloseOutcome outcome, string? reason, IEnumerable<ActionFacts> linkedActions, bool ownershipConfirmed = false) =>
        EvaluateClose(status, requestType, outcome, reason, linkedActions, ownershipConfirmed) is [var first, ..] ? first.Code : null;

    /// <summary>
    /// Single source of the close rule (rule 9): every missing condition, first one first (the code <see cref="ValidateClose"/>
    /// returns). Empty means the request can be closed with this outcome now; the close command still revalidates.
    /// </summary>
    public static IReadOnlyList<CloseGap> EvaluateClose(ServiceAccountRequestStatus status, ServiceAccountActionType requestType,
        ServiceAccountCloseOutcome outcome, string? reason, IEnumerable<ActionFacts> linkedActions, bool ownershipConfirmed = false)
    {
        if (status == ServiceAccountRequestStatus.Closed)
        {
            return [new CloseGap(Errors.AlreadyClosed, CloseRemedy.None, [])];
        }

        if (outcome != ServiceAccountCloseOutcome.Completed)
        {
            return string.IsNullOrWhiteSpace(reason) ? [new CloseGap(Errors.ReasonRequired, CloseRemedy.GiveReason, [])] : [];
        }

        if (requestType == ServiceAccountActionType.OwnershipConfirmation)
        {
            return ownershipConfirmed ? [] : [new CloseGap(Errors.OwnershipNotConfirmed, CloseRemedy.ConfirmOwnership, [])];
        }

        ActionFacts[] matching = [.. linkedActions.Where(a => IsPerformedReport(a) && Matches(requestType, a.ActionType))];
        List<CloseGap> missing = [];
        if (matching.Length == 0)
        {
            missing.Add(new CloseGap(Errors.NoMatchingAction, CloseRemedy.ReportAction, [], requestType));
        }

        if (requestType == ServiceAccountActionType.Deletion && !matching.Any(IsVerifiedClosure))
        {
            // The closest candidate has the fewest unmet conditions; ties keep the caller's order (OrderBy is stable).
            ActionFacts? closest = matching.OrderBy(a => UnmetClosureConditions(a).Count).FirstOrDefault();
            missing.Add(new CloseGap(Errors.VerifiedClosureRequired, CloseRemedy.VerifyAction,
                closest is null ? [] : UnmetClosureConditions(closest), null, closest?.ActionId));
        }

        return missing;
    }

    /// <summary>The <see cref="IsVerifiedClosure"/> conditions a matching performed action does not meet yet, in a fixed order.</summary>
    private static IReadOnlyList<string> UnmetClosureConditions(ActionFacts action)
    {
        List<string> unmet = [];
        if (action.Result != ServiceAccountActionResult.Verified || action.VerifiedOn is null)
        {
            unmet.Add(Errors.NotVerified);
        }

        if (action.RecordKind != ServiceAccountRecordKind.Closure)
        {
            unmet.Add(Errors.NotClosureKind);
        }

        if (action.ActualOn is not { } actual)
        {
            unmet.Add(Errors.ActualDateRequired);
        }
        else if (action.VerifiedOn is { } verified && verified < actual)
        {
            unmet.Add(Errors.VerificationBeforeAction);
        }

        if (!action.HasVerifier)
        {
            unmet.Add(Errors.VerifierRequired);
        }

        if (!action.HasEvidence)
        {
            unmet.Add(Errors.EvidenceRequired);
        }

        if (action.ActionType == ServiceAccountActionType.Deletion && !action.HasOrReference)
        {
            unmet.Add(Errors.OrRequiredForDeletion);
        }

        return unmet;
    }

    /// <summary>Whether a performed action of <paramref name="actionType"/> counts for a request of <paramref name="requestType"/> (the close rule's match).</summary>
    public static bool RequestAccepts(ServiceAccountActionType requestType, ServiceAccountActionType actionType) => Matches(requestType, actionType);

    private static bool Matches(ServiceAccountActionType requestType, ServiceAccountActionType actionType) =>
        requestType == actionType
        || requestType == ServiceAccountActionType.GmsaHandover && actionType == ServiceAccountActionType.GmsaConversion
        || requestType == ServiceAccountActionType.Evaluate;
}
