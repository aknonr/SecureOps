namespace SecureOps.Shared.Contracts.Reporting;

/// <summary>Stable machine-readable identities for management-report duration metrics.</summary>
public static class ManagementReportingDurationKeys
{
    /// <summary>First persisted import to first persisted preview.</summary>
    public const string ImportToPreview = "importToPreview";
    /// <summary>Workflow claim to durable Jira issue-key persistence.</summary>
    public const string ClaimToJiraCreation = "claimToJiraCreation";
    /// <summary>Workflow claim to durable workflow completion.</summary>
    public const string ClaimToCompletion = "claimToCompletion";
}

/// <summary>Stable machine-readable identities for management-report data limitations.</summary>
public static class ManagementReportingLimitationCodes
{
    /// <summary>Rate-limit rejections are not persisted as reporting evidence.</summary>
    public const string RateLimitRejectionsUnavailable = "RateLimitRejectionsUnavailable";
    /// <summary>Historical access-version conflicts are unavailable without audit events.</summary>
    public const string AccessVersionConflictHistoryUnavailable = "AccessVersionConflictHistoryUnavailable";
    /// <summary>Historical source-query outages are unavailable without audit events.</summary>
    public const string OperationalSourceOutageHistoryUnavailable = "OperationalSourceOutageHistoryUnavailable";
    /// <summary>Historical invalid bulk items lack individual terminal events.</summary>
    public const string BulkIdentityInvalidItemHistoryUnavailable = "BulkIdentityInvalidItemHistoryUnavailable";
    /// <summary>Duplicate-create prevention is measurable only after its event was introduced.</summary>
    public const string DuplicateCreatePreventionHistoryIncomplete = "DuplicateCreatePreventionHistoryIncomplete";
    /// <summary>Elapsed durations are not active handling effort or performance measures.</summary>
    public const string ElapsedDurationsNotActiveEffort = "ElapsedDurationsNotActiveEffort";
    /// <summary>The requested interval begins before authoritative persisted evidence.</summary>
    public const string HistoryBeforePersistenceUnavailable = "HistoryBeforePersistenceUnavailable";
    /// <summary>At least one evidence stream is held in process memory and is lost on restart.</summary>
    public const string NonDurableReportingSource = "NonDurableReportingSource";
}
