using SecureOps.Shared.Audit;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Canonical action sets used by both SQL aggregation and report projection.</summary>
public static class ReportingMetricCatalog
{
    /// <summary>Terminal identity outcomes; intermediate request/cache/provider events are excluded.</summary>
    public static readonly string[] IdentityTerminalActions =
    [
        AuditActions.IdentityLookupSucceeded,
        AuditActions.IdentityLookupNotFound,
        AuditActions.IdentityLookupRejected,
        AuditActions.IdentityLookupFailed,
        AuditActions.IdentityLookupProviderTimeout,
        AuditActions.IdentityLookupForbidden
    ];

    /// <summary>Terminal Directory Explorer outcomes; request events are excluded.</summary>
    public static readonly string[] DirectoryTerminalActions =
    [
        AuditActions.DirectoryGroupQueryCompleted,
        AuditActions.DirectoryGroupQueryRejected,
        AuditActions.DirectoryGroupQueryFailed,
        AuditActions.DirectoryGroupQueryForbidden
    ];

    /// <summary>Access lifecycle actions visible in management aggregates.</summary>
    public static readonly string[] AccessActivityActions =
    [
        AuditActions.AccessRequested,
        AuditActions.AccessApproved,
        AuditActions.AccessRejected,
        AuditActions.AccessDisabled,
        AuditActions.RoleAssigned,
        AuditActions.RoleRemoved
    ];

    /// <summary>Reliable application-session lifecycle actions.</summary>
    public static readonly string[] SessionActions =
    [
        AuditActions.ApplicationSessionStarted,
        AuditActions.ApplicationSessionIdleTimedOut,
        AuditActions.ApplicationSessionAbsoluteTimedOut,
        AuditActions.ApplicationSessionLoggedOut,
        AuditActions.ApplicationSessionRevoked,
        AuditActions.ApplicationSessionAccessDisabled,
        AuditActions.ApplicationSessionAccessChanged
    ];

    /// <summary>Operational workflow actions counted as backend operations.</summary>
    public static readonly string[] OperationalWorkflowActions =
    [
        AuditActions.OperationalRecordImported,
        AuditActions.OperationalRecordClassified,
        AuditActions.JiraPreviewGenerated,
        AuditActions.JiraCreateRequested,
        AuditActions.JiraCreated,
        AuditActions.JiraCreateFailed,
        AuditActions.OperationalRecordCloseRequested,
        AuditActions.OperationalRecordClosed,
        AuditActions.OperationalRecordCloseFailed,
        AuditActions.WorkflowRetried,
        AuditActions.WorkflowCompleted,
        AuditActions.OperationalRecordConflict,
        AuditActions.OperationalRecordSourceChanged,
        AuditActions.JiraDuplicateCreatePrevented
    ];

    /// <summary>All actions needed for summary projection.</summary>
    public static readonly string[] SummaryActions = IdentityTerminalActions
        .Concat(DirectoryTerminalActions)
        .Concat(AccessActivityActions)
        .Concat(SessionActions)
        .Concat(OperationalWorkflowActions)
        .Concat([AuditActions.AuthorizationDenied])
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Actions that represent reviewed business usage for adoption counts.</summary>
    public static readonly string[] AdoptionActions = IdentityTerminalActions
        .Where(action => action != AuditActions.IdentityLookupForbidden)
        .Concat(DirectoryTerminalActions.Where(action => action != AuditActions.DirectoryGroupQueryForbidden))
        .Concat(AccessActivityActions)
        .Concat(OperationalWorkflowActions)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Actions that reliably prove an authenticated user was active.</summary>
    public static readonly string[] ActiveUserActions = AdoptionActions
        .Concat([AuditActions.ApplicationSessionStarted])
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Retry outcomes that end a retried correlation unsuccessfully.</summary>
    public static readonly string[] RetryFailureActions = [AuditActions.JiraCreateFailed, AuditActions.OperationalRecordCloseFailed];

    /// <summary>Audit actions whose first occurrence per correlation drives elapsed-time metrics.</summary>
    public static readonly string[] TimingActions = [AuditActions.OperationalRecordClaimed, AuditActions.JiraCreated, AuditActions.WorkflowCompleted];

    /// <summary>Every action a fact-based (non-SQL-aggregated) summary needs to read.</summary>
    public static readonly string[] FactActions =
    [
        .. SummaryActions.Concat(ActiveUserActions).Concat(RetryFailureActions).Concat(TimingActions)
            .Append(AuditActions.WorkflowRetried).Distinct(StringComparer.Ordinal)
    ];

    /// <summary>Maps one persisted action to its explainable workflow category.</summary>
    public static string? WorkflowFor(string action)
    {
        if (IdentityTerminalActions.Contains(action, StringComparer.Ordinal))
        {
            return "IdentityLookup";
        }

        if (AccessActivityActions.Contains(action, StringComparer.Ordinal))
        {
            return "Access";
        }

        if (DirectoryTerminalActions.Contains(action, StringComparer.Ordinal))
        {
            return "DirectoryExplorer";
        }

        return OperationalWorkflowActions.Contains(action, StringComparer.Ordinal)
            ? "OperationalRecordJira"
            : null;
    }
}
