using System.Text.Json.Serialization;

namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>
/// "What happened?" draft (EVENT-GUIDANCE-DESIGN.md §5.2); preview and commit take the same body. The action type, result and
/// record kind are not part of it: the server derives them from <see cref="EventKind"/>. No verification field exists, and an
/// unknown member is refused (400). <see cref="IdempotencyKey"/> and <see cref="PreviewDigest"/> are required on commit and
/// ignored by the preview.
/// </summary>
/// <param name="EventKind">OwnerReplied, PasswordChanged, AccountDeleted, GmsaConverted, OwnerChanged, ServiceMoved or Other.</param>
/// <param name="OccurredOn">The real date of the event (not the mail date); required with DatePrecision DateOnly, refused with Unknown.</param>
/// <param name="DatePrecision">DateOnly or Unknown; Unknown never invents a date.</param>
/// <param name="Channel">Mail, Phone, Meeting or Other.</param>
/// <param name="AccountVersion">The account version the person saw.</param>
/// <param name="RequestId">An open request of this account to link; null links none.</param>
/// <param name="CreateRequest">Ask to open a new request (ServiceMoved, Other only; not with <see cref="RequestId"/>).</param>
/// <param name="CloseRequest">Also close the linked request (only with <see cref="RequestId"/>; default none).</param>
/// <param name="Communication">The correspondence record.</param>
/// <param name="Action">Who performed the reported action and a short evidence note.</param>
/// <param name="References">OR / OCO / JIRA / OTHER numbers (the existing external reference rule).</param>
/// <param name="Ownership">The ownership proposal (OwnerChanged only).</param>
/// <param name="IdempotencyKey">Commit only: the same key with the same draft returns the first result.</param>
/// <param name="PreviewDigest">Commit only: the digest of the preview the person confirmed.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EventDraft(
    string EventKind,
    DateOnly? OccurredOn,
    string DatePrecision,
    string Channel,
    string AccountVersion,
    Guid? RequestId = null,
    EventCreateRequest? CreateRequest = null,
    EventCloseRequest? CloseRequest = null,
    EventCommunication? Communication = null,
    EventAction? Action = null,
    IReadOnlyList<SaExternalRef>? References = null,
    EventOwnership? Ownership = null,
    Guid? IdempotencyKey = null,
    string? PreviewDigest = null);

/// <summary>A new request opened with the event; its type comes from the event (Review for ServiceMoved, Evaluate for Other).</summary>
/// <param name="ActionType">Optional; when sent it must equal the type the event derives.</param>
/// <param name="TargetTeamId">Team expected to do the work.</param>
/// <param name="Notes">Free notes.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EventCreateRequest(string? ActionType = null, Guid? TargetTeamId = null, string? Notes = null);

/// <summary>Close the linked request with the event; only Completed, checked by the close rule against the records as written.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EventCloseRequest(string Outcome);

/// <summary>
/// The correspondence record. Direction and kind are fixed by the event where the table says so (OwnerReplied: Incoming Reply,
/// PasswordChanged: Incoming Information); otherwise the person chooses. Summary is the person's own short note, not mail text.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EventCommunication(string? Direction = null, string? Kind = null, Guid? ContactTeamId = null, Guid? ContactPersonId = null,
    string? Subject = null, string? Summary = null, string? ProviderMessageId = null, bool? MeaningfulReply = null);

/// <summary>Who performed the reported action (directory team/person) and a short evidence note; the type, result and kind are derived.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EventAction(Guid? PerformerTeamId = null, Guid? PerformerPersonId = null, string? EvidenceNote = null);

/// <summary>An ownership proposal (never a confirmation; the decision stays in the ownership decision flow).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EventOwnership(Guid? TeamId, Guid? PersonId, string Reason, DateOnly? EffectiveFrom = null);

/// <summary>
/// What a commit of the draft would record and what it would not (EVENT-GUIDANCE-DESIGN.md §5.3). Nothing is written. A commit
/// revalidates everything; <see cref="PreviewDigest"/> only proves the person confirmed the same records.
/// </summary>
/// <param name="PreviewDigest">SHA-256 of the canonical draft and the versions of the account and the linked request.</param>
/// <param name="Items">Records that would be created, updated or linked.</param>
/// <param name="NotRecorded">What the event does not do: Verification, AccountClosure, RequestClose, OwnershipDecision, PlanChecklist.</param>
/// <param name="Warnings">Codes such as OrMissingForDeletion, DateUnknown, MailDuplicate, ReplyDateEarlier, ReplyDateUnknown.</param>
/// <param name="CloseReadiness">With a linked request: whether it could be closed Completed after these records (information only).</param>
/// <param name="FollowUps">Steps still to take (not done): Verify, CloseRequest, OpenPlan, UsageTab, OwnershipDecision.</param>
/// <param name="CanCommit">False when any item is not allowed for the caller; the commit then refuses the whole event.</param>
public sealed record EventPreview(
    string PreviewDigest,
    IReadOnlyList<EventPreviewItem> Items,
    IReadOnlyList<string> NotRecorded,
    IReadOnlyList<EventWarning> Warnings,
    CloseReadiness? CloseReadiness,
    IReadOnlyList<EventFollowUp> FollowUps,
    bool CanCommit);

/// <summary>One record of the preview.</summary>
/// <param name="Kind">Communication, Action, Reference, RequestUpdate, RequestCreate, OwnershipProposal or RequestClose.</param>
/// <param name="Operation">Create, Update or Link.</param>
/// <param name="TargetId">The existing record it updates or links, when there is one.</param>
/// <param name="Label">Turkish label; an action is always "reported, not verified".</param>
/// <param name="Fields">The values that would be written.</param>
/// <param name="Allowed">Whether the caller may write it.</param>
/// <param name="DeniedReason">NoWorkCapability, OutOfScope or NotParticipantRequest.</param>
public sealed record EventPreviewItem(string Kind, string Operation, Guid? TargetId, string Label, IReadOnlyList<EventField> Fields, bool Allowed,
    string? DeniedReason = null);

/// <summary>One value of a preview item.</summary>
public sealed record EventField(string Name, string? Value);

/// <summary>A warning that does not stop the commit.</summary>
public sealed record EventWarning(string Code, string? Field = null);

/// <summary>A step still to take after the commit; never shown as done.</summary>
public sealed record EventFollowUp(string Kind, string? Href = null);

/// <summary>Commit result: the account as now stored, the event and the records it produced.</summary>
/// <param name="Detail">The account detail after the commit.</param>
/// <param name="EventId">The recorded event.</param>
/// <param name="Created">Records created, updated or linked by the event.</param>
/// <param name="Replayed">True when the same idempotency key and draft were already recorded; nothing new was written.</param>
public sealed record EventCommitResult(AccountDetail Detail, Guid EventId, IReadOnlyList<EventCreatedRecord> Created, bool Replayed);

/// <summary>One record an event produced.</summary>
/// <param name="Kind">Item kind (as in the preview).</param>
/// <param name="EntityType">Communication, Action, ExternalRecord, Request or Ownership.</param>
/// <param name="Id">Record id.</param>
/// <param name="Operation">Create, Update or Link.</param>
public sealed record EventCreatedRecord(string Kind, string EntityType, Guid Id, string Operation);
