namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>Organization dictionary entry.</summary>
public sealed record OrganizationView(Guid Id, string Name, Guid? ParentId, string Kind, string Version);

/// <summary>Team dictionary entry; provisional teams came from source labels.</summary>
public sealed record TeamView(Guid Id, string Name, Guid? OrganizationId, bool Provisional, string Version);

/// <summary>Module business-person reference; never a login and grants nothing.</summary>
public sealed record PersonView(Guid Id, string DisplayName, string VerificationState, bool HasDirectoryIdentity, IReadOnlyList<string> Aliases, string Version);

/// <summary>Create or rename an organization.</summary>
public sealed record SaveOrganizationRequest(string Name, string Kind, Guid? ParentId, string? ExpectedVersion = null);

/// <summary>Create or move/rename a team.</summary>
public sealed record SaveTeamRequest(string Name, Guid? OrganizationId, bool Provisional = false, string? ExpectedVersion = null);

/// <summary>Create a provisional person reference.</summary>
public sealed record CreatePersonRequest(string DisplayName);

/// <summary>Record a verified directory identity for a person (authorized administrator; evidence required).</summary>
public sealed record VerifyPersonRequest(string ExpectedVersion, string? Upn, string? DirectoryObjectId, string Evidence);

/// <summary>Add an alias with evidence; similar spelling never merges automatically.</summary>
public sealed record AddPersonAliasRequest(string Alias, string Evidence);

/// <summary>Active scope grant.</summary>
public sealed record ScopeGrantView(Guid Id, Guid UserId, string UserLabel, string ScopeKind, SaRef? Organization, SaRef? Team,
    string Reason, DateTimeOffset GrantedAt, string Version);

/// <summary>Grant data scope to an existing approved application user identified exactly.</summary>
public sealed record CreateScopeGrantRequest(string CorporateIdentity, string ScopeKind, Guid? OrganizationId, Guid? TeamId, string Reason);

/// <summary>Revoke a grant.</summary>
public sealed record RevokeScopeGrantRequest(string ExpectedVersion, string Reason);

/// <summary>
/// One-time first scope grant (ADR-0026): available only while the module has never had any scope grant and the
/// schema carries the bootstrap guard. The caller receives "All" scope; afterwards self-grants are refused again.
/// </summary>
/// <param name="Available">The bootstrap can still be used.</param>
/// <param name="SchemaReady">The database has the one-time guard (candidate SA-003 / its numbered migration).</param>
/// <param name="Used">A bootstrap grant was already made (active or revoked).</param>
public sealed record ScopeBootstrapState(bool Available, bool SchemaReady, bool Used);

/// <summary>Request the one-time first scope grant for the caller.</summary>
public sealed record ScopeBootstrapRequest(string Reason);

/// <summary>An approved application user who can receive scope (no directory lookup; only users already admitted to the application).</summary>
/// <param name="CorporateIdentity">Exact identity sent back in <see cref="CreateScopeGrantRequest"/>.</param>
/// <param name="Label">Display name, else login name, else identity.</param>
/// <param name="LoginName">Login name, when known.</param>
/// <param name="IsCaller">The current user (cannot receive a grant from themself).</param>
/// <param name="HasServiceAccountsAccess">Holds at least the module View capability; without it a scope grant has no effect.</param>
public sealed record ScopeGrantCandidate(string CorporateIdentity, string Label, string? LoginName, bool IsCaller, bool HasServiceAccountsAccess);

/// <summary>Caller's module context for UI composition.</summary>
public sealed record ServiceAccountMe(bool Configured, IReadOnlyList<string> Capabilities, string ScopeKind, IReadOnlyList<SaRef> Organizations,
    IReadOnlyList<SaRef> Teams, bool HasScope);

/// <summary>Weekly report query.</summary>
/// <remarks><c>Period</c>: Week (default, Monday of <c>WeekStart</c>), Month (month of <c>WeekStart</c>) or Custom (<c>WeekStart</c>..<c>PeriodEnd</c> inclusive, at most 366 days).</remarks>
public sealed record WeeklyReportQuery(DateOnly WeekStart, DateTimeOffset? AsOf = null, Guid? OrganizationId = null, Guid? TeamId = null,
    string? Period = null, DateOnly? PeriodEnd = null);

/// <summary>Create an immutable snapshot of the report computed now for the given query.</summary>
public sealed record CreateSnapshotRequest(DateOnly WeekStart, DateTimeOffset? AsOf, Guid? OrganizationId, Guid? TeamId, string Kind, string? Label,
    string? Period = null, DateOnly? PeriodEnd = null);

/// <summary>Snapshot list item.</summary>
public sealed record SnapshotItem(Guid Id, string Kind, DateOnly PeriodStart, DateOnly PeriodEnd, DateTimeOffset AsOf, string ScopeLabel,
    string MetricDefinitionVersion, string PayloadSha256, string? Label, DateTimeOffset CreatedAt);

/// <summary>In-app reminder or coordinator draft.</summary>
public sealed record ReminderView(Guid Id, Guid RequestId, Guid AccountId, string AccountName, string RuleCode, string RuleLabel,
    DateOnly DueDate, string Channel, string Status, int AttemptCount, string? LastError, string Message, DateTimeOffset CreatedAt, string Version);

/// <summary>Reminder evaluation outcome.</summary>
public sealed record ReminderRunResult(int Evaluated, int Enqueued, int Delivered, int Failed, int DeadLettered, bool BusinessDayRulesConfigured);

/// <summary>Dismiss a reminder.</summary>
public sealed record DismissReminderRequest(string ExpectedVersion);
