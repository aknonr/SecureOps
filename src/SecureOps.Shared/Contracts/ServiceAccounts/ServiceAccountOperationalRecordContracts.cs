namespace SecureOps.Shared.Contracts.ServiceAccounts;

// Opening a Turuncu Hat OR from an approved gMSA change plan (ADR-0030, PROPOSED; OR-2 ships a Fake adapter only and the server
// default is Disabled; migration 035). One OR per plan account. The preview shows every field that will be sent; a separate
// confirmation sends exactly that (fingerprint). Nothing is ever retried automatically.
// Vocabulary (stable codes; labels are Turkish display text):
//   State   Sending (row written before the call; no persisted outcome) · NotSent (nothing dispatched; a new attempt is allowed)
//           · Created (OR number known) · PartiallyCreated (record exists, number or a field unconfirmed) · OutcomeUnknown
//           ("Belirsiz": an OR may exist; reconcile) · Linked (a person linked the number) · ConfirmedNotCreated (a person
//           stated that none exists; a new attempt is allowed).
//   Requester Matched · NotMatched · Ambiguous · Unknown — anything but Matched stops the flow (ADR-0032).
//   Lookup  Found · NotFound · Ambiguous · Unknown (never "absent").

/// <summary>
/// Preview of one account's OR. Null texts and environment mean "use the template / no choice yet"; an edited text is
/// previewed again before it can be sent, because the fingerprint covers the exact text.
/// </summary>
public sealed record OperationalRecordPreviewRequest(string? ShortDescription = null, string? Description = null, string? Environment = null);

/// <summary>
/// Sends the previewed OR once. <see cref="Fingerprint"/> is the preview's; any difference (text, environment, requester,
/// plan) is 409 <c>orPreviewStale</c> and nothing is written. <see cref="IdempotencyKey"/> is generated once per confirmation:
/// the same key with the same content returns the first result (replayed) without a second call; with other content 409.
/// </summary>
public sealed record OperationalRecordSubmitRequest(string? ShortDescription, string? Description, string? Environment, string? Fingerprint,
    Guid? IdempotencyKey);

/// <summary>Looks up an OR number a person found in Turuncu Hat during reconciliation; writes nothing.</summary>
public sealed record OperationalRecordCandidateRequest(string? Number);

/// <summary>Links an OR number to the account's unresolved submission (<see cref="SubmissionId"/>), with the person's reason.</summary>
public sealed record OperationalRecordLinkRequest(Guid SubmissionId, string? Number, string? Reason);

/// <summary>States that no OR was created for the account's unresolved submission; reason mandatory (a wrong answer may duplicate an OR).</summary>
public sealed record OperationalRecordNotCreatedRequest(Guid SubmissionId, string? Reason);

/// <summary>Result of mapping the person who presses the button to an ITSM user. Only Matched with a location may send.</summary>
/// <param name="Status">Matched, NotMatched, Ambiguous or Unknown.</param>
/// <param name="Label">Turkish explanation.</param>
/// <param name="UserId">ITSM user id when Matched (reporter and affected end user).</param>
/// <param name="HasHomeLocation">Whether the matched user has the home location the OR form requires.</param>
public sealed record OperationalRecordRequesterView(string Status, string Label, string? UserId, bool HasHomeLocation);

/// <summary>One field that will be sent: its name, value and where the value comes from.</summary>
public sealed record OperationalRecordFieldView(string Name, string Value, string Source);

/// <summary>
/// What a confirmation would send for one account. <see cref="Description"/> is the editable body; <see cref="SentDescription"/>
/// adds the fixed plan reference line and is sent byte for byte. <see cref="Fingerprint"/> is null while <see cref="CanSubmit"/>
/// is false; <see cref="BlockedReason"/> then names why (<c>requesterNotMatched</c>, <c>requesterLocationMissing</c>,
/// <c>environmentRequired</c>, <c>submissionExists</c>).
/// </summary>
public sealed record OperationalRecordPreview(
    Guid PlanId,
    Guid AccountId,
    string AccountName,
    string TargetGmsaName,
    int PreviewVersion,
    IReadOnlyList<string> Servers,
    string ShortDescription,
    string Description,
    string PlanReference,
    string SentDescription,
    int ShortDescriptionMax,
    int DescriptionMax,
    string? Environment,
    IReadOnlyList<string> Environments,
    OperationalRecordRequesterView Requester,
    IReadOnlyList<OperationalRecordFieldView> Fields,
    string Provider,
    bool Synthetic,
    bool CanSubmit,
    string? BlockedReason,
    string? BlockedLabel,
    string? Fingerprint);

/// <summary>One append-only state change of a submission.</summary>
public sealed record OperationalRecordEventView(string Event, string? FromState, string ToState, string? ReasonCode, string? RecordNumber, string? Reason,
    string Actor, DateTimeOffset At);

/// <summary>
/// One "open OR" attempt. <see cref="RecordNumber"/> and <see cref="ExternalId"/> are null until proven or linked;
/// <see cref="LinkVerification"/> is <c>Found</c> when the linked number was found by the ITSM lookup, <c>NotVerified</c>
/// otherwise (never "absent"). <see cref="Synthetic"/> marks Fake results.
/// </summary>
public sealed record OperationalRecordSubmissionView(
    Guid Id,
    Guid AccountId,
    string State,
    string StateLabel,
    string? ReasonCode,
    string? ReasonLabel,
    string Provider,
    bool Synthetic,
    string? RecordNumber,
    string? ExternalId,
    string? LinkVerification,
    string Environment,
    string ShortDescription,
    string Description,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool NeedsReconciliation,
    IReadOnlyList<OperationalRecordEventView> Events);

/// <summary>
/// One plan account and its OR attempts (newest first in <see cref="Earlier"/>). <see cref="OpenBlockedReason"/> names why the
/// caller cannot open an OR now; <see cref="CanLink"/>/<see cref="CanConfirmNotCreated"/> apply to <see cref="Current"/>.
/// </summary>
public sealed record OperationalRecordAccountView(Guid AccountId, string AccountName, string TargetGmsaName, OperationalRecordSubmissionView? Current,
    IReadOnlyList<OperationalRecordSubmissionView> Earlier, bool CanOpen, string? OpenBlockedReason, string? OpenBlockedLabel, bool CanLink,
    bool CanConfirmNotCreated);

/// <summary>
/// The plan's OR panel. <see cref="CreationAvailable"/> is false when the server's provider is Disabled (the default) or the plan
/// is not Approved/InProgress; <see cref="Synthetic"/> is true for the Fake provider. Permissions are server-computed per account.
/// </summary>
public sealed record PlanOperationalRecordsView(Guid PlanId, string PlanStatus, bool CreationAvailable, string Provider, bool Synthetic,
    string? UnavailableReason, IReadOnlyList<OperationalRecordAccountView> Accounts);

/// <summary>Result of a confirmation: the stored submission and whether it was a replay of the same key and content.</summary>
public sealed record OperationalRecordSubmitResult(OperationalRecordSubmissionView Submission, bool Replayed);

/// <summary>
/// Read-only tracking summary of the account's OR (ADR-0032), read live and not stored. Every null value means "unknown"
/// (the ITSM did not prove it), never "none"; <see cref="ActivitiesKnown"/>/<see cref="CommentsKnown"/> tell a proven zero from
/// an unknown. Status and stage are raw ITSM codes, never interpreted as approved or closed.
/// </summary>
public sealed record OperationalRecordTrackingView(
    string? RecordNumber,
    string Status,
    string StatusLabel,
    string? ReasonCode,
    string? LastModified,
    string? RawStatus,
    string? RawStage,
    bool ActivitiesKnown,
    int? ActivityCount,
    string? LastActivityGroup,
    string? LastActivityStatus,
    bool CommentsKnown,
    int? CommentCount,
    string? LastCommentAt,
    string Provider,
    bool Synthetic,
    DateTimeOffset CheckedAt);

/// <summary>
/// An OR number a person typed during reconciliation, as the ITSM lookup sees it now. <see cref="TitleMatchesSubmission"/>
/// compares the record title with the submission's short description (null when unknown); it is a hint, the person decides.
/// </summary>
public sealed record OperationalRecordCandidate(string Number, string LookupStatus, string LookupLabel, string? ReasonCode, string? Title,
    bool? TitleMatchesSubmission, OperationalRecordTrackingView? Tracking, string Provider, bool Synthetic, bool CanLink);
