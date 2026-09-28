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
public sealed record ActionFacts(
    ServiceAccountActionType ActionType,
    ServiceAccountActionResult Result,
    ServiceAccountRecordKind RecordKind,
    DateOnly? ActualOn,
    DateOnly? VerifiedOn,
    bool HasVerifier,
    bool HasEvidence,
    bool HasOrReference,
    bool Voided);

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
    }

    /// <summary>Rule 8: a non-void Performed or Verified action is a performed-action report; counted once.</summary>
    public static bool IsPerformedReport(ActionFacts action) =>
        !action.Voided && action.Result is ServiceAccountActionResult.Performed or ServiceAccountActionResult.Verified;

    /// <summary>Rule 6: verified closure conditions; record kind alone never closes.</summary>
    public static bool IsVerifiedClosure(ActionFacts action) =>
        !action.Voided
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
    /// </summary>
    public static string? ValidateClose(ServiceAccountRequestStatus status, ServiceAccountActionType requestType,
        ServiceAccountCloseOutcome outcome, string? reason, IEnumerable<ActionFacts> linkedActions, bool ownershipConfirmed = false)
    {
        if (status == ServiceAccountRequestStatus.Closed)
        {
            return Errors.AlreadyClosed;
        }

        if (outcome != ServiceAccountCloseOutcome.Completed)
        {
            return string.IsNullOrWhiteSpace(reason) ? Errors.ReasonRequired : null;
        }

        if (requestType == ServiceAccountActionType.OwnershipConfirmation)
        {
            return ownershipConfirmed ? null : Errors.OwnershipNotConfirmed;
        }

        ActionFacts[] matching = [.. linkedActions.Where(a => IsPerformedReport(a) && Matches(requestType, a.ActionType))];
        if (matching.Length == 0)
        {
            return Errors.NoMatchingAction;
        }

        return requestType == ServiceAccountActionType.Deletion && !matching.Any(IsVerifiedClosure)
            ? Errors.VerifiedClosureRequired
            : null;
    }

    private static bool Matches(ServiceAccountActionType requestType, ServiceAccountActionType actionType) =>
        requestType == actionType
        || requestType == ServiceAccountActionType.GmsaHandover && actionType == ServiceAccountActionType.GmsaConversion
        || requestType == ServiceAccountActionType.Evaluate;
}
