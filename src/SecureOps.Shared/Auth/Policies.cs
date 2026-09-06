namespace SecureOps.Shared.Auth;

/// <summary>
/// Authorization policy names used by the API and UI.
/// </summary>
public static class Policies
{
    /// <summary>Approved catalogue reader and personal preference owner.</summary>
    public const string CanViewResources = "CanViewResources";
    /// <summary>Explicit shared catalogue management.</summary>
    public const string CanManageResources = "CanManageResources";
    /// <summary>Capability policy for one exact identity lookup.</summary>
    public const string CanIdentityLookup = "CanIdentityLookup";
    /// <summary>Capability policy for bounded bulk identity lookup.</summary>
    public const string CanBulkIdentityLookup = "CanBulkIdentityLookup";
    /// <summary>Capability policy for exact group metadata and principal direct groups.</summary>
    public const string CanViewDirectoryGroups = "CanViewDirectoryGroups";
    /// <summary>Capability policy for exact group direct-member enumeration.</summary>
    public const string CanViewDirectoryGroupMembers = "CanViewDirectoryGroupMembers";
    /// <summary>Capability policy for configured privileged-group evidence.</summary>
    public const string CanViewDirectoryPrivilegedGroups = "CanViewDirectoryPrivilegedGroups";
    /// <summary>Capability policy for bounded group membership export.</summary>
    public const string CanExportDirectoryGroups = "CanExportDirectoryGroups";
    /// <summary>Capability policy for team metadata.</summary>
    public const string CanTeamView = "CanTeamView";
    /// <summary>Capability policy for access administration.</summary>
    public const string CanAccessAdministration = "CanAccessAdministration";
    /// <summary>Capability policy for system diagnostics.</summary>
    public const string CanSystemDiagnostics = "CanSystemDiagnostics";
    /// <summary>View imported operational records.</summary>
    public const string CanViewOperationalRecords = "CanViewOperationalRecords";
    /// <summary>Generate a read-only Jira preview.</summary>
    public const string CanPreviewJira = "CanPreviewJira";
    /// <summary>Create one Jira issue through the durable workflow.</summary>
    public const string CanCreateJira = "CanCreateJira";
    /// <summary>Retry a failed durable Jira workflow.</summary>
    public const string CanRetryJira = "CanRetryJira";
    /// <summary>View operational workflow diagnostics.</summary>
    public const string CanViewOperationalRecordDiagnostics = "CanViewOperationalRecordDiagnostics";
    /// <summary>Manage application users.</summary>
    public const string CanManageUsers = "CanManageUsers";
    /// <summary>Approve or reject application access requests.</summary>
    public const string CanApproveAccessRequests = "CanApproveAccessRequests";
    /// <summary>Assign application roles.</summary>
    public const string CanAssignRoles = "CanAssignRoles";
    /// <summary>View access-control audit evidence.</summary>
    public const string CanViewAccessAudit = "CanViewAccessAudit";
    /// <summary>View backend-authoritative management reports.</summary>
    public const string CanViewManagementReports = "CanViewManagementReports";
    /// <summary>
    /// Operator, TeamLead, Admin, or Auditor where explicitly allowed by endpoint logic.
    /// </summary>
    public const string OperatorOrAbove = "OperatorOrAbove";

    /// <summary>
    /// TeamLead or Admin.
    /// </summary>
    public const string TeamLeadOrAbove = "TeamLeadOrAbove";

    /// <summary>
    /// Admin only.
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    /// Auditor only.
    /// </summary>
    public const string AuditorOnly = "AuditorOnly";

    /// <summary>
    /// Auditor or Admin audit-query access.
    /// </summary>
    public const string CanViewAudit = "CanViewAudit";

    /// <summary>
    /// TeamLead or Admin manual diagnostic access.
    /// </summary>
    public const string CanTriggerDiagnostic = "CanTriggerDiagnostic";
}
