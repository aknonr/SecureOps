namespace SecureOps.Shared.Auth;

/// <summary>Stable application capability identifiers.</summary>
public static class Capabilities
{
    /// <summary>Read one exact identity.</summary>
    public const string IdentityLookup = "Identity.Lookup";
    /// <summary>Read a bounded set of exact identities.</summary>
    public const string BulkIdentityLookup = IdentityLookup;
    /// <summary>View approved team metadata.</summary>
    public const string TeamView = "TeamView";
    /// <summary>View operational audit evidence.</summary>
    public const string AuditView = "AuditView";
    /// <summary>Administer application access requests and roles.</summary>
    public const string AccessAdministration = "AccessAdministration";
    /// <summary>Run or view approved system diagnostics.</summary>
    public const string SystemDiagnostics = "SystemDiagnostics";
    /// <summary>View operational records.</summary>
    public const string OperationalRecordsView = "OperationalRecords.View";
    /// <summary>Create read-only Jira previews.</summary>
    public const string OperationalRecordsCreateJiraPreview = "OperationalRecords.CreateJiraPreview";
    /// <summary>Create Jira issues through the durable workflow.</summary>
    public const string OperationalRecordsCreateJira = "OperationalRecords.CreateJira";
    /// <summary>Retry failed operational-record workflows.</summary>
    public const string OperationalRecordsRetry = "OperationalRecords.Retry";
    /// <summary>View operational-record workflow diagnostics.</summary>
    public const string OperationalRecordsViewDiagnostics = "OperationalRecords.ViewDiagnostics";
    /// <summary>Manage application users and disable access.</summary>
    public const string AccessManageUsers = "Access.ManageUsers";
    /// <summary>Approve or reject pending access requests.</summary>
    public const string AccessApproveRequests = "Access.ApproveRequests";
    /// <summary>Assign or remove application roles.</summary>
    public const string AccessAssignRoles = "Access.AssignRoles";
    /// <summary>View access-control audit evidence.</summary>
    public const string AccessViewAudit = "Access.ViewAudit";
    /// <summary>View backend-authoritative aggregate and paginated management reporting.</summary>
    public const string ManagementReportingView = "Reporting.ManagementView";
}
