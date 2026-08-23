using SecureOps.Shared.Auth;

namespace SecureOps.Infrastructure.Access;

/// <summary>Reviewed application role-to-capability mapping.</summary>
public static class AccessRoleCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> _roles =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] =
            [
                Capabilities.IdentityLookup,
                Capabilities.DirectoryGroupsView,
                Capabilities.DirectoryGroupMembersView,
                Capabilities.DirectoryPrivilegedGroupsView,
                Capabilities.TeamView,
                Capabilities.AuditView,
                Capabilities.AccessAdministration,
                Capabilities.SystemDiagnostics,
                Capabilities.OperationalRecordsView,
                Capabilities.OperationalRecordsCreateJiraPreview,
                Capabilities.OperationalRecordsCreateJira,
                Capabilities.OperationalRecordsRetry,
                Capabilities.OperationalRecordsViewDiagnostics,
                Capabilities.AccessManageUsers,
                Capabilities.AccessApproveRequests,
                Capabilities.AccessAssignRoles,
                Capabilities.AccessViewAudit,
                Capabilities.ManagementReportingView
            ],
            ["Lead"] =
            [
                Capabilities.IdentityLookup,
                Capabilities.DirectoryGroupsView,
                Capabilities.TeamView,
                Capabilities.SystemDiagnostics,
                Capabilities.OperationalRecordsView,
                Capabilities.OperationalRecordsCreateJiraPreview,
                Capabilities.OperationalRecordsCreateJira,
                Capabilities.OperationalRecordsRetry,
                Capabilities.OperationalRecordsViewDiagnostics
            ],
            ["Operator"] =
            [
                Capabilities.TeamView,
                Capabilities.OperationalRecordsView,
                Capabilities.OperationalRecordsCreateJiraPreview
            ],
            ["JiraPublisher"] =
            [
                Capabilities.OperationalRecordsView,
                Capabilities.OperationalRecordsCreateJiraPreview,
                Capabilities.OperationalRecordsCreateJira,
                Capabilities.OperationalRecordsRetry
            ],
            ["Auditor"] =
            [
                Capabilities.AuditView,
                Capabilities.OperationalRecordsView,
                Capabilities.OperationalRecordsViewDiagnostics,
                Capabilities.AccessViewAudit,
                Capabilities.ManagementReportingView
            ],
            ["ReadOnly"] = [Capabilities.OperationalRecordsView]
        };

    /// <summary>All reviewed role codes.</summary>
    public static IReadOnlyCollection<string> RoleCodes => _roles.Keys.ToArray();

    /// <summary>Returns whether a role code exists.</summary>
    public static bool IsKnownRole(string role) => _roles.ContainsKey(role);

    /// <summary>Returns distinct capabilities for a role set.</summary>
    public static IReadOnlyList<string> GetCapabilities(IEnumerable<string> roles) => roles
        .Where(IsKnownRole)
        .SelectMany(role => _roles[role])
        .Distinct(StringComparer.Ordinal)
        .OrderBy(capability => capability, StringComparer.Ordinal)
        .ToArray();
}
