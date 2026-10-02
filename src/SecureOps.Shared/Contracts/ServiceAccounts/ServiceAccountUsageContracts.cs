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
