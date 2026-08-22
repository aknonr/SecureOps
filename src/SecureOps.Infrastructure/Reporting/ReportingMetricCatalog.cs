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
        .Concat(AccessActivityActions)
        .Concat(OperationalWorkflowActions)
        .Concat([AuditActions.AuthorizationDenied])
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Actions that represent reviewed business usage for adoption counts.</summary>
    public static readonly string[] AdoptionActions = IdentityTerminalActions
        .Where(action => action != AuditActions.IdentityLookupForbidden)
        .Concat(AccessActivityActions)
        .Concat(OperationalWorkflowActions)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

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

        return OperationalWorkflowActions.Contains(action, StringComparer.Ordinal)
            ? "OperationalRecordJira"
            : null;
    }
}
