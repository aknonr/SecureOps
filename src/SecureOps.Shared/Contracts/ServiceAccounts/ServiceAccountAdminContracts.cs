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

/// <summary>Caller's module context for UI composition.</summary>
public sealed record ServiceAccountMe(bool Configured, IReadOnlyList<string> Capabilities, string ScopeKind, IReadOnlyList<SaRef> Organizations,
    IReadOnlyList<SaRef> Teams, bool HasScope);

/// <summary>Weekly report query.</summary>
public sealed record WeeklyReportQuery(DateOnly WeekStart, DateTimeOffset? AsOf = null, Guid? OrganizationId = null, Guid? TeamId = null);

/// <summary>Create an immutable snapshot of the report computed now for the given query.</summary>
public sealed record CreateSnapshotRequest(DateOnly WeekStart, DateTimeOffset? AsOf, Guid? OrganizationId, Guid? TeamId, string Kind, string? Label);

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
