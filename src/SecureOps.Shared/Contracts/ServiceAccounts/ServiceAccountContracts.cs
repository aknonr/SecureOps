namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>Human-readable reference to a module entity; IDs are for navigation only.</summary>
public sealed record SaRef(Guid Id, string Label, string? State = null);

/// <summary>External record reference; OR, OCO, JIRA and OTHER are never interchangeable.</summary>
public sealed record SaExternalRef(string Type, string Number, string? Url = null);

/// <summary>Account list query; filters are applied server-side after the scope filter.</summary>
public sealed record AccountListQuery(
    string? Search = null,
    Guid? OrganizationId = null,
    Guid? TeamId = null,
    Guid? PersonId = null,
    string? Status = null,
    string? Domain = null,
    bool MyTeam = false,
    DateOnly? DueBefore = null,
    string Sort = "name",
    bool Descending = false,
    int Page = 1,
    int PageSize = 25);

/// <summary>One account list row.</summary>
public sealed record AccountListItem(
    Guid Id,
    string AccountName,
    string? Domain,
    string IdentityState,
    SaRef? ReportOrganization,
    SaRef? OwnerTeam,
    SaRef? OwnerPerson,
    SaRef? FollowupPerson,
    bool OwnershipProposed,
    int OpenRequests,
    DateOnly? NearestDue,
    string Status,
    DateOnly? LastObservedOn,
    string? LastPresence,
    string Version);

/// <summary>Server-paged account list with stable ordering.</summary>
public sealed record AccountPage(IReadOnlyList<AccountListItem> Items, int Total, int Page, int PageSize);

/// <summary>Account summary header.</summary>
public sealed record AccountSummaryView(
    Guid Id,
    string AccountName,
    string? Domain,
    string? Sid,
    string IdentityState,
    SaRef? ReportOrganization,
    SaRef? OwnerTeam,
    SaRef? OwnerPerson,
    SaRef? ConsumerTeam,
    string LifecycleState,
    string? Notes,
    DateOnly? LastObservedOn,
    string? LastPresence,
    int OpenRequests,
    DateOnly? NearestDue,
    IReadOnlyList<SaExternalRef> References,
    string? LegacyDisplayId,
    string Version);

/// <summary>Ownership assignment history row.</summary>
public sealed record OwnershipView(Guid Id, SaRef? Team, SaRef? Person, string State, string Source, DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo, string? EvidenceNote, DateTimeOffset ProposedAt, DateTimeOffset? DecidedAt, string? DecisionReason, string Version);

/// <summary>Request (expected work) row; each open request is shown separately.</summary>
public sealed record RequestView(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string ActionType,
    string ActionLabel,
    string Status,
    string? CloseOutcome,
    SaRef? TargetTeam,
    SaRef? FollowupPerson,
    SaRef? ContactPerson,
    DateOnly? PlanStart,
    DateOnly? PlanEnd,
    DateOnly? PlanAnnouncedOn,
    DateOnly? NextFollowupOn,
    DateOnly? FirstSentOn,
    DateOnly? LastReplyOn,
    string? Notes,
    bool Overdue,
    bool AwaitingDate,
    IReadOnlyList<SaExternalRef> References,
    string? LegacyDisplayId,
    string? CloseReason,
    string Version);

/// <summary>Action row: plan, reported action and verification on one identity.</summary>
public sealed record ActionView(
    Guid Id,
    Guid AccountId,
    Guid? RequestId,
    string ActionType,
    string ActionLabel,
    string Result,
    string ResultLabel,
    string RecordKind,
    DateOnly? ActualOn,
    DateTimeOffset? ActualAt,
    string Precision,
    SaRef? PerformerTeam,
    SaRef? PerformerPerson,
    string? EvidenceNote,
    DateOnly? VerifiedOn,
    SaRef? VerifiedByPerson,
    string? VerificationNote,
    bool VerifiedClosure,
    bool Voided,
    string? VoidReason,
    string? SourceNote,
    IReadOnlyList<SaExternalRef> References,
    string? LegacyDisplayId,
    string Version);

/// <summary>Communication row with its linked accounts.</summary>
public sealed record CommunicationView(
    Guid Id,
    string Direction,
    string Kind,
    DateOnly? OccurredOn,
    DateTimeOffset? OccurredAt,
    string Precision,
    SaRef? ContactTeam,
    SaRef? ContactPerson,
    string? Subject,
    string? Summary,
    string? Link,
    string RecordScope,
    bool? MeaningfulReply,
    IReadOnlyList<SaRef> Accounts,
    bool HasProviderMessageId,
    string Version);

/// <summary>Technical finding; never an action or proof of non-use.</summary>
public sealed record FindingView(Guid Id, Guid AccountId, string? Server, string? ComponentType, string? ComponentName, string? Environment,
    DateTimeOffset? ScanAt, DateOnly? ScanOn, string ScanResult, string MatchResult, string? CoverageWindow, string? EvidenceNote,
    SaRef? OwningTeam, string Status, string? JobReference, string? Notes, IReadOnlyList<string> Gaps, string Version);

/// <summary>Source observation (append-only; not a human action).</summary>
public sealed record ObservationView(Guid Id, Guid BatchId, string SourceProfile, DateOnly? SourceReportDate, string Presence,
    DateTime? PasswordLastSet, DateTime? LastLogonAdOrLdap, DateTime? LastLogonAd, string? Organization, string? GroupDirectorate,
    string? Comment, string? SourceTeam, string? ConsumerTeam, string? HandoverFlag, string? SourceRow, DateTimeOffset RecordedAt);

/// <summary>Handover row.</summary>
public sealed record HandoverView(Guid Id, Guid AccountId, string AccountName, SaRef? SourceTeam, SaRef TargetTeam, SaRef? ConsumerTeam,
    string? CohortLabel, DateOnly? ProposedOn, string Status, DateOnly? DecidedOn, string? DecisionNote, string? SourceNote, string Version);

/// <summary>gMSA transition tracking; independent from handover acceptance.</summary>
public sealed record TransitionView(Guid Id, Guid AccountId, Guid? HandoverId, string Target, string Suitability, string? DecisionNote,
    DateOnly? PlannedOn, Guid? CompletedActionId, string Version);

/// <summary>Business timeline entry.</summary>
public sealed record HistoryView(string EntityType, Guid EntityId, string Action, string? ChangesJson, string? Reason, string Actor, DateTimeOffset OccurredAt);

/// <summary>Evidence metadata; download goes through the same scope policy.</summary>
public sealed record EvidenceView(Guid Id, string OwnerEntityType, Guid OwnerEntityId, string FileName, string ContentType, int SizeBytes,
    string Sha256, string? Label, DateTimeOffset CreatedAt);

/// <summary>Import rows that referenced this account (source history).</summary>
public sealed record SourceRowView(Guid BatchId, string Profile, string FileName, DateOnly? SourceReportDate, string Sheet, int RowNumber,
    string EntityKind, string Classification, string OriginalJson);

/// <summary>Why the caller sees an account, which bounds what they may change.</summary>
public static class ServiceAccountAccessBasis
{
    /// <summary>Organization-level scope or the confirmed owner team: account-wide work.</summary>
    public const string Responsible = "Responsible";
    /// <summary>Visible only through an open request targeted at the caller's team or an incoming handover.</summary>
    public const string Participant = "Participant";
    /// <summary>Read only.</summary>
    public const string Viewer = "Viewer";
}

/// <summary>
/// Which actions the current caller may take on this account (server-computed). <see cref="Work"/>, <see cref="Verify"/> and
/// ownership apply to the whole account and need the responsible basis. A participant team works only on the open
/// requests listed in <see cref="ParticipantRequestIds"/>: it may update them (not retarget them), report actions linked
/// to them and attach evidence to them, but may not change the account, its ownership, other requests or verification.
/// </summary>
public sealed record AccountPermissions(bool Work, bool AssignPerson, bool AssignTeam, bool Verify, bool DecideHandover, bool UploadEvidence,
    string Basis = ServiceAccountAccessBasis.Viewer, IReadOnlyList<Guid>? ParticipantRequestIds = null)
{
    /// <summary>Whether the caller may work on this request (account-wide work or a participant request).</summary>
    public bool CanWorkRequest(Guid requestId) => Work || (ParticipantRequestIds?.Contains(requestId) ?? false);

    /// <summary>Whether the caller has any request it may work on.</summary>
    public bool CanWorkAnyRequest => Work || ParticipantRequestIds is { Count: > 0 };
}

/// <summary>Complete scoped account detail.</summary>
public sealed record AccountDetail(
    AccountSummaryView Summary,
    IReadOnlyList<OwnershipView> Ownership,
    IReadOnlyList<RequestView> Requests,
    IReadOnlyList<ActionView> Actions,
    IReadOnlyList<CommunicationView> Communications,
    IReadOnlyList<FindingView> Findings,
    IReadOnlyList<ObservationView> Observations,
    IReadOnlyList<HandoverView> Handovers,
    IReadOnlyList<TransitionView> Transitions,
    IReadOnlyList<EvidenceView> Evidence,
    IReadOnlyList<SourceRowView> Sources,
    IReadOnlyList<HistoryView> History,
    AccountPermissions Permissions);

/// <summary>Create an account manually (no automatic provisioning from names).</summary>
public sealed record CreateAccountRequest(string AccountName, string? Domain, Guid? ReportOrganizationId, string Reason);

/// <summary>Update account notes/consumer team/report organization; blank never clears, ClearFields does with reason.</summary>
public sealed record UpdateAccountRequest(string ExpectedVersion, string? Notes = null, Guid? ConsumerTeamId = null,
    Guid? ReportOrganizationId = null, string? Domain = null, IReadOnlyList<string>? ClearFields = null, string? Reason = null,
    IReadOnlyList<SaExternalRef>? AddReferences = null);

/// <summary>Propose or confirm ownership. Confirm requires Assign authority for the target scope.</summary>
public sealed record OwnershipChangeRequest(string ExpectedVersion, Guid? TeamId, Guid? PersonId, string Mode, string Reason,
    DateOnly? EffectiveFrom = null, string? EvidenceNote = null);

/// <summary>Decide a proposed ownership.</summary>
public sealed record OwnershipDecisionRequest(string ExpectedVersion, string Decision, string Reason);

/// <summary>Create a request (expected work).</summary>
public sealed record CreateWorkRequest(
    string ActionType,
    Guid? TargetTeamId = null,
    Guid? FollowupPersonId = null,
    Guid? ContactPersonId = null,
    DateOnly? PlanStart = null,
    DateOnly? PlanEnd = null,
    DateOnly? PlanAnnouncedOn = null,
    DateOnly? NextFollowupOn = null,
    DateOnly? FirstSentOn = null,
    DateOnly? LastReplyOn = null,
    string? Notes = null,
    IReadOnlyList<SaExternalRef>? References = null);

/// <summary>Partial request update; null means unchanged, ClearFields clears with a reason.</summary>
public sealed record UpdateWorkRequest(
    string ExpectedVersion,
    string? ActionType = null,
    Guid? TargetTeamId = null,
    Guid? FollowupPersonId = null,
    Guid? ContactPersonId = null,
    DateOnly? PlanStart = null,
    DateOnly? PlanEnd = null,
    DateOnly? PlanAnnouncedOn = null,
    DateOnly? NextFollowupOn = null,
    DateOnly? FirstSentOn = null,
    DateOnly? LastReplyOn = null,
    string? Notes = null,
    IReadOnlyList<string>? ClearFields = null,
    string? Reason = null,
    IReadOnlyList<SaExternalRef>? AddReferences = null);

/// <summary>Explicitly close one request.</summary>
public sealed record CloseWorkRequest(string ExpectedVersion, string Outcome, string? Reason = null);

/// <summary>Report a planned or performed action. Adding an action never closes a request.</summary>
public sealed record ReportActionRequest(
    string ActionType,
    string Result,
    string RecordKind,
    Guid? RequestId = null,
    DateOnly? ActualOn = null,
    DateTimeOffset? ActualAt = null,
    Guid? PerformerTeamId = null,
    Guid? PerformerPersonId = null,
    string? EvidenceNote = null,
    IReadOnlyList<SaExternalRef>? References = null);

/// <summary>Move a planned action to performed on the same identity.</summary>
public sealed record UpdateActionRequest(string ExpectedVersion, string? Result = null, DateOnly? ActualOn = null, DateTimeOffset? ActualAt = null,
    Guid? PerformerTeamId = null, Guid? PerformerPersonId = null, string? EvidenceNote = null, string? RecordKind = null,
    IReadOnlyList<SaExternalRef>? AddReferences = null);

/// <summary>Verify a performed action; updates the same action identity.</summary>
public sealed record VerifyActionRequest(string ExpectedVersion, DateOnly VerifiedOn, Guid? VerifierPersonId = null,
    string? VerificationNote = null, Guid? EvidenceId = null);

/// <summary>Void an action (history kept).</summary>
public sealed record VoidActionRequest(string ExpectedVersion, string Reason);

/// <summary>Record one real communication linked to zero or more accounts.</summary>
public sealed record CreateCommunicationRequest(
    string Direction,
    string Kind,
    IReadOnlyList<Guid> AccountIds,
    DateOnly? OccurredOn = null,
    DateTimeOffset? OccurredAt = null,
    Guid? ContactTeamId = null,
    Guid? ContactPersonId = null,
    string? Subject = null,
    string? Summary = null,
    string? Link = null,
    string? ProviderMessageId = null,
    bool? MeaningfulReply = null,
    IReadOnlyList<SaExternalRef>? References = null);

/// <summary>Result of recording a communication; a repeated provider message ID resolves to the same mail.</summary>
public sealed record CommunicationSaveResult(CommunicationView Communication, bool ExistingMessage, int AddedLinks);

/// <summary>Record a technical finding.</summary>
public sealed record CreateFindingRequest(
    Guid AccountId,
    string ScanResult,
    string MatchResult,
    string Status = "Open",
    string? Server = null,
    string? ComponentType = null,
    string? ComponentName = null,
    string? Environment = null,
    DateTimeOffset? ScanAt = null,
    DateOnly? ScanOn = null,
    string? CoverageWindow = null,
    string? EvidenceNote = null,
    Guid? OwningTeamId = null,
    string? JobReference = null,
    string? Notes = null);

/// <summary>Change a finding status.</summary>
public sealed record UpdateFindingRequest(string ExpectedVersion, string Status, string? Notes = null);

/// <summary>Accept or reject a handover with the real decision date and evidence note.</summary>
public sealed record HandoverDecisionRequest(string ExpectedVersion, string Decision, DateOnly DecidedOn, string Note);

/// <summary>Propose a handover manually.</summary>
public sealed record CreateHandoverRequest(Guid TargetTeamId, Guid? SourceTeamId = null, Guid? ConsumerTeamId = null,
    string? CohortLabel = null, DateOnly? ProposedOn = null, string? Note = null, bool TrackGmsa = false);

/// <summary>Update gMSA suitability/plan; a decision needs a note.</summary>
public sealed record TransitionUpdateRequest(string ExpectedVersion, string Suitability, string? DecisionNote = null,
    DateOnly? PlannedOn = null, Guid? CompletedActionId = null);
