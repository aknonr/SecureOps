namespace SecureOps.Shared.Contracts.Reporting;

/// <summary>Resolved UTC reporting interval.</summary>
public sealed record ReportingWindowResponse(
    string Selection,
    DateTimeOffset FromInclusiveUtc,
    DateTimeOffset ToExclusiveUtc);

/// <summary>One UTC-day identity lookup trend bucket.</summary>
public sealed record IdentityLookupTrendPointResponse(
    DateOnly DateUtc,
    long Total,
    long Succeeded,
    long NotFound,
    long Rejected,
    long ProviderUnavailable,
    long Forbidden);

/// <summary>Explainable exact-account lookup metrics.</summary>
public sealed record IdentityLookupMetricsResponse(
    long TotalLookups,
    long SuccessfulLookups,
    long NotFound,
    long RejectedOrInvalid,
    long ProviderUnavailable,
    long AuthorizationDenied,
    long UniqueActiveOperators,
    IReadOnlyList<IdentityLookupTrendPointResponse> Trend);

/// <summary>Named aggregate count.</summary>
public sealed record NamedCountResponse(string Name, long Count);

/// <summary>Elapsed-time statistics for a workflow interval.</summary>
public sealed record DurationStatisticsResponse(
    string Definition,
    long SampleCount,
    double? MinimumSeconds,
    double? AverageSeconds,
    double? MaximumSeconds);

/// <summary>Retry request and terminal outcome counts.</summary>
public sealed record RetryOutcomeMetricsResponse(
    long Requested,
    long Succeeded,
    long Failed,
    long UnresolvedAtWindowEnd);

/// <summary>Operational Record and synthetic/approved Jira workflow metrics.</summary>
public sealed record OperationalWorkflowMetricsResponse(
    long RecordsImported,
    long Eligible,
    long Previewed,
    long JiraCreated,
    long Completed,
    long SourceChangedPrevented,
    long ClosedOrMissingPrevented,
    long Failures,
    long ReconciliationRequired,
    long DuplicateCreatePrevented,
    RetryOutcomeMetricsResponse Retries,
    IReadOnlyList<DurationStatisticsResponse> Durations);

/// <summary>Team-level adoption and access activity aggregates.</summary>
public sealed record PlatformAdoptionMetricsResponse(
    long DailyActiveUsers,
    long WeeklyActiveUsers,
    long MonthlyActiveUsers,
    long UniqueActiveUsersInWindow,
    IReadOnlyList<NamedCountResponse> OperationsByWorkflow,
    IReadOnlyList<NamedCountResponse> AccessRequestActivity);

/// <summary>Security and quality evidence available from persisted backend events.</summary>
public sealed record SecurityQualityMetricsResponse(
    long AuthorizationFailures,
    long ConcurrencyConflicts,
    long ReconciliationEvents,
    long ProviderUnavailableEvents,
    long? RateLimitEvents);

/// <summary>Reliable server-side application-session lifecycle aggregates.</summary>
public sealed record SessionGovernanceMetricsResponse(
    long Started,
    long IdleTimedOut,
    long AbsoluteTimedOut,
    long LoggedOut,
    long Revoked,
    long AccessDisabledTerminations,
    long AccessChangedTerminations);

/// <summary>Backend-authoritative management report summary.</summary>
public sealed record ManagementReportResponse(
    ReportingWindowResponse Window,
    IdentityLookupMetricsResponse IdentityLookup,
    OperationalWorkflowMetricsResponse OperationalWorkflow,
    PlatformAdoptionMetricsResponse PlatformAdoption,
    SessionGovernanceMetricsResponse SessionGovernance,
    SecurityQualityMetricsResponse SecurityAndQuality,
    IReadOnlyList<string> DataLimitations);

/// <summary>One authorized per-operator aggregate without directory enrichment.</summary>
public sealed record OperatorActivityResponse(
    string Actor,
    long OperationCount,
    DateTimeOffset FirstActivityAt,
    DateTimeOffset LastActivityAt,
    IReadOnlyList<NamedCountResponse> OperationsByWorkflow);

/// <summary>Paginated per-operator activity aggregates.</summary>
public sealed record OperatorActivityPageResponse(
    ReportingWindowResponse Window,
    int Page,
    int PageSize,
    long TotalItems,
    IReadOnlyList<OperatorActivityResponse> Items);
