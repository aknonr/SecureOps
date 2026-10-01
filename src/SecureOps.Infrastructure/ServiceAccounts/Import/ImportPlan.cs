using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>Row decision codes.</summary>
public static class ImportDecisions
{
    /// <summary>Create the record.</summary>
    public const string Create = "Create";
    /// <summary>Do not import this row.</summary>
    public const string Skip = "Skip";
    /// <summary>Link the row to an existing account (records an alias) instead of creating one.</summary>
    public const string Link = "Link";
    /// <summary>Record source ownership as a proposal.</summary>
    public const string Propose = "Propose";
    /// <summary>Confirm source ownership (authorized coordinator approval).</summary>
    public const string Confirm = "Confirm";
    /// <summary>Keep the existing confirmed ownership (conflict).</summary>
    public const string Keep = "Keep";
    /// <summary>Apply additive updates (links/empty fields).</summary>
    public const string Apply = "Apply";
}

/// <summary>Planned row with preview information and a fingerprint compared at commit.</summary>
public sealed record PlannedRow(
    int RowKey,
    StagedRow Row,
    string Classification,
    Guid? AccountId,
    string? AccountLabel,
    IReadOnlyList<ImportFieldDiff> Diff,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ImportCandidate> Candidates,
    bool RequiresDecision,
    string DefaultDecision,
    IReadOnlyList<string> AllowedDecisions,
    string Fingerprint);

/// <summary>New dictionary entry to create at commit.</summary>
public sealed record NewNamed(Guid Id, string Name, string Key, Guid? ParentId);

/// <summary>Account to create.</summary>
public sealed record AccountCreate(Guid Id, string Name, string NameKey, string? Domain, string? DomainKey, string IdentityKey, Guid? OrganizationId,
    Guid? ConsumerTeamId, string? Notes, string? LegacyReference, string? LegacyDisplayId, Guid? MigrationKey, int RowKey);

/// <summary>Additive fill of empty account fields.</summary>
public sealed record AccountFill(Guid AccountId, string RowVersion, string? Notes, Guid? ConsumerTeamId, Guid? OrganizationId, int RowKey);

/// <summary>External reference link to add.</summary>
public sealed record ReferenceAdd(ExternalRecordType Type, string Number, string EntityType, Guid EntityId, Guid? AccountId);

/// <summary>Alias recorded when a row is explicitly linked to an existing account.</summary>
public sealed record AccountAliasAdd(Guid AccountId, string Alias, string NormalizedAlias, string? Domain);

/// <summary>Ownership proposal/confirmation.</summary>
public sealed record OwnershipAdd(Guid Id, Guid AccountId, Guid? TeamId, Guid? PersonId, OwnershipState State, string Source, string SourceKey, int RowKey);

/// <summary>Observation to append.</summary>
public sealed record ObservationAdd(Guid AccountId, string Profile, string Presence, DateTime? PasswordLastSet, DateTime? LastLogonAdOrLdap,
    DateTime? LastLogonAd, string? Organization, string? GroupDirectorate, string? Comment, string? SourceTeam, string? ConsumerTeam,
    string? HandoverFlag, string? SourceRow, string RawJson);

/// <summary>Request to create.</summary>
public sealed record RequestAdd(Guid Id, Guid AccountId, ServiceAccountActionType ActionType, ServiceAccountRequestStatus Status, Guid? TargetTeamId,
    Guid? FollowupPersonId, Guid? ContactPersonId, DateOnly? PlanStart, DateOnly? PlanEnd, DateOnly? PlanAnnouncedOn, DateOnly? NextFollowupOn,
    DateOnly? FirstSentOn, DateOnly? LastReplyOn, string? Notes, string? SourceKey, string? LegacyReference, string? LegacyDisplayId, Guid? MigrationKey);

/// <summary>Action to create.</summary>
public sealed record ActionAdd(Guid Id, Guid AccountId, ServiceAccountActionType ActionType, ServiceAccountActionResult Result, ServiceAccountRecordKind Kind,
    DateOnly? ActualOn, Guid? PerformerTeamId, Guid? PerformerPersonId, string? EvidenceNote, DateOnly? VerifiedOn, Guid? VerifiedByPersonId,
    string? SourceNote, string? LegacyReference, string? LegacyDisplayId, Guid? MigrationKey);

/// <summary>Communication to create (links deduplicated).</summary>
public sealed record CommunicationAdd(Guid Id, CommunicationDirection Direction, CommunicationKind Kind, DateOnly? OccurredOn, Guid? ContactTeamId,
    string? Subject, string? Summary, string? Link, string RecordScope, bool? MeaningfulReply, IReadOnlyList<Guid> Accounts, string? LegacyReference, Guid? MigrationKey);

/// <summary>Links to add to an existing communication.</summary>
public sealed record CommunicationLinkAdd(Guid CommunicationId, IReadOnlyList<Guid> Accounts);

/// <summary>Finding to create.</summary>
public sealed record FindingAdd(Guid Id, Guid AccountId, string? Server, string? ComponentType, string? ComponentName, string? Environment, DateOnly? ScanOn,
    FindingScanResult ScanResult, FindingMatchResult MatchResult, string? Coverage, string? Evidence, Guid? OwningTeamId, FindingStatus Status,
    string? JobReference, string? Notes, string? LegacyReference);

/// <summary>Handover proposal (and optional gMSA tracking) to create.</summary>
public sealed record HandoverAdd(Guid Id, Guid AccountId, Guid? SourceTeamId, Guid TargetTeamId, Guid? ConsumerTeamId, string? CohortLabel,
    DateOnly? ProposedOn, string? SourceNote, string SourceKey, string? LegacyReference, bool TrackGmsa);

/// <summary>Everything a commit writes, derived from a fresh re-plan inside the commit transaction.</summary>
public sealed class ImportWork
{
    /// <summary>Organizations to create.</summary>
    public List<NewNamed> Organizations { get; } = [];
    /// <summary>Teams to create.</summary>
    public List<NewNamed> Teams { get; } = [];
    /// <summary>People to create.</summary>
    public List<NewNamed> People { get; } = [];
    /// <summary>Person aliases (person, alias, key, evidence).</summary>
    public List<(Guid PersonId, string Alias, string Key, string Evidence)> PersonAliases { get; } = [];
    /// <summary>Accounts to create.</summary>
    public List<AccountCreate> AccountCreates { get; } = [];
    /// <summary>Account fills.</summary>
    public List<AccountFill> AccountFills { get; } = [];
    /// <summary>Account aliases.</summary>
    public List<AccountAliasAdd> AccountAliases { get; } = [];
    /// <summary>External references.</summary>
    public List<ReferenceAdd> References { get; } = [];
    /// <summary>Ownership proposals/confirmations.</summary>
    public List<OwnershipAdd> Ownerships { get; } = [];
    /// <summary>Observations.</summary>
    public List<ObservationAdd> Observations { get; } = [];
    /// <summary>Requests.</summary>
    public List<RequestAdd> Requests { get; } = [];
    /// <summary>Actions.</summary>
    public List<ActionAdd> Actions { get; } = [];
    /// <summary>Communications.</summary>
    public List<CommunicationAdd> Communications { get; } = [];
    /// <summary>Communication links.</summary>
    public List<CommunicationLinkAdd> CommunicationLinks { get; } = [];
    /// <summary>Findings.</summary>
    public List<FindingAdd> Findings { get; } = [];
    /// <summary>Handovers.</summary>
    public List<HandoverAdd> Handovers { get; } = [];

    /// <summary>Accounts that get a gMSA transition record (suitability unknown) without a handover, from gMSA routing.</summary>
    public List<Guid> GmsaTransitions { get; } = [];
    /// <summary>Rows applied without change.</summary>
    public int Unchanged { get; set; }
    /// <summary>Rows skipped by decision.</summary>
    public int Skipped { get; set; }
    /// <summary>Invalid rows.</summary>
    public int Invalid { get; set; }
}

/// <summary>Planner output.</summary>
public sealed record ImportPlanResult(IReadOnlyList<PlannedRow> Rows, ImportSummary Summary, ImportWork Work, IReadOnlyList<string> Warnings);
