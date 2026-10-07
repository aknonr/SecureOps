namespace SecureOps.Shared.Contracts.ServiceAccounts;

// gMSA conversion change plan (docs/service-accounts/CHANGE-PLAN-DESIGN.md, migration 033). The system keeps the plan, the
// preview, the approval and (PR 2) the person's checklist; it never connects to a server and no password field exists.
// Vocabulary (all strings are stable codes; labels are Turkish display text):
//   Kind    GmsaConversion (only kind)
//   Status  Draft → Previewed → Approved → InProgress → Completed; Cancelled from any open status. A change after the
//           preview returns the plan to Draft and invalidates the preview (and with it any approval request built on it).
//   Flag    Ok · StaleScan (scan older than ScanFreshDays) · NoScan (no Discovery scan on the account; one "no information"
//           row) · NotCovered (server not scanned or partly scanned: the component list may be incomplete) · ManualOnly
//           (IIS site/application/virtual directory "connect as": not supported with a gMSA or unclear) · NameTooLong.
//           No flag blocks the plan; the approver sees the counts.

/// <summary>One account of a plan as sent by the client: the account and the gMSA name it will use (at most 15 counted characters).</summary>
/// <param name="AccountId">Service account.</param>
/// <param name="TargetGmsaName">gMSA name (031 rule: no domain prefix, UPN suffix or trailing <c>$</c> counted).</param>
/// <param name="RequestId">Optional open gMSA request of the same account the plan carries out; closing the plan never closes it.</param>
public sealed record ChangePlanAccountInput(Guid AccountId, string TargetGmsaName, Guid? RequestId = null);

/// <summary>Creates a Draft plan with 1–20 accounts. Every account is checked on its own; if any is refused nothing is written.</summary>
public sealed record CreateChangePlanRequest(string Title, IReadOnlyList<ChangePlanAccountInput> Accounts);

/// <summary>
/// Replaces the plan's account list with <see cref="Accounts"/> (1–20) at <see cref="ExpectedVersion"/>: accounts not in the
/// current list are added, missing ones removed, a changed name or request is a rename. Each change is a new append-only
/// row. A real change from Previewed returns the plan to Draft; an identical list changes nothing.
/// </summary>
public sealed record UpdateChangePlanRequest(string ExpectedVersion, IReadOnlyList<ChangePlanAccountInput> Accounts);

/// <summary>Builds a new preview version from each account's latest Discovery scan (Draft or Previewed only).</summary>
public sealed record PreviewChangePlanRequest(string ExpectedVersion);

/// <summary>
/// Approves exactly the preview the approver looked at: <see cref="PreviewVersion"/> and <see cref="Sha256"/> must be the
/// plan's current preview (otherwise 409 <c>previewStale</c>). <see cref="OcoNumber"/> is checked for format only.
/// The approver may not be the planner, the person who produced this preview or anyone who changed the plan (403).
/// </summary>
public sealed record ApproveChangePlanRequest(int PreviewVersion, string Sha256, string OcoNumber, DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd, string Reason);

/// <summary>Cancels an open plan with a reason at <see cref="ExpectedVersion"/>; every record is kept.</summary>
public sealed record CancelChangePlanRequest(string ExpectedVersion, string Reason);

/// <summary>Plan list filter: status code, plans that currently hold one account, and paging (1–100 per page).</summary>
public sealed record ChangePlanListQuery(string? Status = null, Guid? AccountId = null, int Page = 1, int PageSize = 25);

/// <summary>One plan in the list (only plans whose every account is inside the caller's scope are listed).</summary>
public sealed record ChangePlanListItem(Guid Id, string Kind, string Title, string Status, string StatusLabel, int AccountCount,
    int? CurrentPreviewVersion, string? OcoNumber, DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd, string CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Server-paged plan list, newest change first.</summary>
public sealed record ChangePlanPage(IReadOnlyList<ChangePlanListItem> Items, int Total, int Page, int PageSize);

/// <summary>An account currently in the plan (from its latest append-only row).</summary>
public sealed record ChangePlanAccountView(Guid AccountId, string AccountName, string? Domain, string TargetGmsaName, Guid? RequestId,
    DateTimeOffset ChangedAt);

/// <summary>How many preview rows carry each flag.</summary>
public sealed record ChangePlanFlagCounts(int Ok, int StaleScan, int NoScan, int NotCovered, int ManualOnly, int NameTooLong);

/// <summary>
/// The plan's current preview: version, server-computed SHA-256 of its rows (the approval binds to both), freshness rule,
/// row count and flag counts. Rows are read page by page from the items route.
/// </summary>
public sealed record ChangePlanPreviewView(int Version, string Sha256, int ScanFreshDays, int ItemCount, ChangePlanFlagCounts Flags,
    string CreatedBy, DateTimeOffset CreatedAt);

/// <summary>
/// One preview row: a component found by the account's latest Discovery scan, or one "no information" row (NoScan, or a
/// server the scan did not answer). Server, type, name and current identity are null when the scan has nothing to say.
/// </summary>
public sealed record ChangePlanItemView(Guid Id, Guid AccountId, string AccountName, string? ServerName, string? ComponentType,
    string? ComponentName, string? CurrentIdentity, string TargetIdentity, string Flag, string FlagLabel, DateTimeOffset? ScanAt);

/// <summary>Server-paged rows of the plan's current preview, ordered by account, server, type and name.</summary>
public sealed record ChangePlanItemPage(int PreviewVersion, IReadOnlyList<ChangePlanItemView> Items, int Total, int Page, int PageSize);

/// <summary>The approval of the current preview (who approved is operational evidence, not a performance record).</summary>
public sealed record ChangePlanApprovalView(int PreviewVersion, string Sha256, string OcoNumber, DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd, string Reason, string ApprovedBy, DateTimeOffset ApprovedAt);

/// <summary>One append-only plan event (Created, Updated, Previewed, Approved, Cancelled; PR 2 adds checklist events).</summary>
public sealed record ChangePlanEventView(string Event, string? FromStatus, string ToStatus, int? PreviewVersion, string? Reason,
    string Actor, DateTimeOffset At);

/// <summary>
/// What the caller may do now (server-computed; the server decides again on every command). <see cref="ApproveBlockedReason"/>
/// is <c>approverIsPlanner</c> or <c>approverChangedPlan</c> when only the separation rule blocks approval, otherwise null.
/// </summary>
public sealed record ChangePlanPermissions(bool Edit, bool Preview, bool Approve, string? ApproveBlockedReason, bool Cancel);

/// <summary>Complete plan view. <see cref="Version"/> is the expected version for update, preview and cancel.</summary>
public sealed record ChangePlanView(
    Guid Id,
    string Kind,
    string Title,
    string Status,
    string StatusLabel,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version,
    IReadOnlyList<ChangePlanAccountView> Accounts,
    ChangePlanPreviewView? Preview,
    ChangePlanApprovalView? Approval,
    IReadOnlyList<ChangePlanEventView> Events,
    ChangePlanPermissions Permissions);

/// <summary>
/// Why one account of a create or update request was accepted or refused. <c>Outcome</c>: <c>Accepted</c>,
/// <c>Unavailable</c> (missing, out of scope or not the caller's to plan — indistinguishable; no name returned),
/// <c>InOpenPlan</c> (already in another open plan), <c>InvalidName</c>, <c>InvalidRequest</c> (not an open gMSA request of
/// this account) or <c>Duplicate</c>. <c>AccountName</c> is present only for an account inside the caller's scope.
/// Sent as the <c>current</c> extension of a 400 <c>ServiceAccountChangePlanAccountsRefused</c>.
/// </summary>
public sealed record ChangePlanAccountResult(Guid AccountId, string? AccountName, string Outcome, string OutcomeLabel);
