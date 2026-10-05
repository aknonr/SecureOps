namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>One recorded usage of an account (knowledge-base rule input). Removed usages stay visible with their reason.</summary>
public sealed record UsageView(Guid Id, string Kind, string KindLabel, string? DatabaseEngine, bool? NeedVerified, string? Server, string? Component,
    string? Notes, string? ExceptionReason, DateTimeOffset? ExceptionAt, bool Removed, string? RemovedReason, DateTimeOffset CreatedAt, string Version);

/// <summary>One explained rule result ("neden bu öneri").</summary>
public sealed record RuleItemView(string RuleCode, string Path, string PathLabel, string Reason, Guid? UsageId, bool Excepted);

/// <summary>Rule evaluation of an account, computed by the same implementation as the reports. It never changes data.</summary>
public sealed record RuleEvaluationView(string RuleSetVersion, string? Path, string? PathLabel, string Conformance, string ConformanceLabel,
    IReadOnlyList<RuleItemView> Items, string? GmsaStage, string? GmsaExecutorTeam, bool SqlTeamAccount);

/// <summary>Records a usage. Kind and engine use the controlled vocabulary; server/component are free labels.</summary>
public sealed record CreateUsageRequest(string Kind, string? DatabaseEngine = null, bool? NeedVerified = null, string? Server = null,
    string? Component = null, string? Notes = null);

/// <summary>Updates a usage at the expected version (kind cannot change; remove and record a new one).</summary>
public sealed record UpdateUsageRequest(string ExpectedVersion, string? DatabaseEngine = null, bool? NeedVerified = null, string? Server = null,
    string? Component = null, string? Notes = null);

/// <summary>Removes a usage with a reason (never hard-deleted).</summary>
public sealed record RemoveUsageRequest(string ExpectedVersion, string Reason);

/// <summary>Records (or clears) a reasoned rule exception; requires the Verify capability.</summary>
public sealed record UsageExceptionRequest(string ExpectedVersion, string Reason, bool Clear = false);

/// <summary>Team role values: SQL team (accounts evaluated as gMSA) and the single gMSA executing team.</summary>
public static class ServiceAccountTeamRoles
{
    /// <summary>Accounts of this team are evaluated as gMSA.</summary>
    public const string SqlTeam = "SqlTeam";
    /// <summary>The team that executes gMSA evaluations (one at a time).</summary>
    public const string GmsaExecutor = "GmsaExecutor";
}

/// <summary>Active team role.</summary>
public sealed record TeamRoleView(Guid Id, SaRef Team, string Role, string Reason, DateTimeOffset CreatedAt);

/// <summary>Assigns a team role (module dictionary; grants no access).</summary>
public sealed record CreateTeamRoleRequest(Guid TeamId, string Role, string Reason);

/// <summary>Revokes a team role with a reason.</summary>
public sealed record RevokeTeamRoleRequest(string Reason);

/// <summary>Bounded first-name or full-name directory search (ADR-0025). The query travels in the body, never in the URL.</summary>
public sealed record DirectoryNameSearchRequest(string Query);

/// <summary>
/// One directory result with only the fields needed to tell people apart. A Service Accounts link is present only when
/// exactly one record with that account name is inside the caller's scope; selecting a result grants and changes nothing.
/// </summary>
public sealed record DirectoryNameMatch(string DisplayName, string Account, string? Department, bool SameNameAsAnother, Guid? ServiceAccountId);

/// <summary>Search answer; <c>Truncated</c> asks the caller to refine the query instead of paging the directory.</summary>
public sealed record DirectoryNameSearchResponse(IReadOnlyList<DirectoryNameMatch> Matches, bool Truncated, int MinimumLetters, int MaximumResults);

/// <summary>
/// A usage scan attached to this account (ADR-0027): evidence a person produced under their own authority. Coverage, per-server
/// counts and the gMSA evidence are computed over ALL items matched to this account's searched name; <c>Items</c> holds only the
/// first page of each role (former-account items undecided first, then the expected-gMSA items), and the totals say how many
/// exist (<see cref="UsageScanItemPage"/> pages through the rest). Nothing here closes, frees or verifies the account.
/// </summary>
public sealed record UsageScanView(Guid ScanId, Guid LinkId, string Purpose, string MatchedAccount, string? ExpectedAccount, string FileName, string Sha256,
    string Tool, DateTimeOffset CombinedAt, DateTimeOffset? FirstScannedAt, DateTimeOffset? LastScannedAt, string RunStatement, string UploadedBy,
    DateTimeOffset UploadedAt, Guid? RequestId, DateTimeOffset LinkedAt, UsageScanCoverageView Coverage, UsageScanGmsaView? Gmsa,
    IReadOnlyList<UsageScanServerView> Servers, IReadOnlyList<UsageScanItemView> Items, int FormerTotal = 0, int FormerPending = 0,
    int ExpectedTotal = 0);

/// <summary>Planned servers by result and by what the scan says about this account.</summary>
public sealed record UsageScanCoverageView(int Planned, int Success, int Partial, int Failed, int Unreachable, int NoResult, int Found, int NotFound,
    int Uncertain, int NotCovered);

/// <summary>Derived gMSA conversion evidence for this account (never a verification).</summary>
public sealed record UsageScanGmsaView(string ExpectedAccount, string Conclusion, string ConclusionLabel, int StillFormerServers, int RunsAsGmsaServers,
    int NoComponentServers, int UnknownServers);

/// <summary>One planned server; <c>Outcome</c> is Found / NotFound / Uncertain / NotCovered for this account.</summary>
public sealed record UsageScanServerView(string ServerName, string Result, string ResultLabel, string? WindowsServices, string? ScheduledTasks, string? Iis,
    DateTimeOffset? ScannedAt, string? Warnings, int Matches, string Outcome, string OutcomeLabel, string? GmsaState, string? GmsaStateLabel);

/// <summary>
/// One matched component. <c>Role</c> is <c>Former</c> (runs as this account) or <c>Expected</c> (runs as the expected gMSA).
/// A person may turn a Former item into a usage record or dismiss it with a reason; <c>Decision</c> is null until then.
/// </summary>
public sealed record UsageScanItemView(Guid Id, string ServerName, string Role, string ComponentType, string ComponentTypeLabel, string ComponentName,
    string ConfiguredIdentity, string? State, string? Detail, string SuggestedKind, string? Decision, Guid? UsageId, string? DecisionReason,
    DateTimeOffset? DecidedAt);

/// <summary>Records a matched component as a usage (the person chooses the kind; same rules as a manual usage).</summary>
public sealed record RecordScanUsageRequest(string Kind, string? DatabaseEngine = null, bool? NeedVerified = null, string? Notes = null);

/// <summary>Leaves a matched component out of the usage records, with a reason (kept as a decision).</summary>
public sealed record DismissScanItemRequest(string Reason);

/// <summary>Paging bounds for usage scans on the account page (server-side; the UI only asks for pages).</summary>
public static class UsageScanPaging
{
    /// <summary>Scans per page, newest first (the account detail carries the first page).</summary>
    public const int ScanPageSize = 5;
    /// <summary>Items per page and role when the caller does not ask for another size.</summary>
    public const int DefaultItemPageSize = 25;
    /// <summary>Largest item page.</summary>
    public const int MaxItemPageSize = 100;
}

/// <summary>One page of the scans attached to an account (newest first); <c>Pending</c> counts undecided items across all of them.</summary>
public sealed record UsageScanPage(IReadOnlyList<UsageScanView> Scans, int Total, int Page, int PageSize, int Pending);

/// <summary>
/// One page of a scan's matched items for this account. <c>Role</c> is <c>Former</c> (undecided first) or <c>Expected</c>;
/// <c>PendingOnly</c> keeps only former-account items still waiting for a decision. Paging never changes coverage or outcomes.
/// </summary>
public sealed record UsageScanItemPage(IReadOnlyList<UsageScanItemView> Items, int Total, int Page, int PageSize, string Role, bool PendingOnly);
