using Microsoft.AspNetCore.Authorization;
using SecureOps.Shared.Auth;

namespace SecureOps.Api.Security;

/// <summary>
/// Central authorization policy registration for the SecureOps API.
/// </summary>
public static class SecureOpsAuthorization
{
    /// <summary>
    /// Adds SecureOps role policies.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="requireAuthenticatedFallback">Whether every endpoint requires authentication by default.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddSecureOpsAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireAuthenticatedFallback)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, CapabilityAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            AddCapability(options, Policies.CanViewResources, Capabilities.ResourcesView);
            AddCapability(options, Policies.CanViewInUse, Capabilities.InUseView);
            AddCapability(options, Policies.CanReviewInUse, Capabilities.InUseReview);
            AddCapability(options, Policies.CanAssignInUse, Capabilities.InUseAssign);
            AddCapability(options, Policies.CanRefreshInUse, Capabilities.InUseRefresh);
            AddCapability(options, Policies.CanManageResources, Capabilities.ResourcesManage);
            AddCapability(options, Policies.OperatorOrAbove, Capabilities.TeamView);
            AddCapability(options, Policies.TeamLeadOrAbove, Capabilities.IdentityLookup);
            AddCapability(options, Policies.AdminOnly, Capabilities.AccessManageUsers);
            AddCapability(options, Policies.AuditorOnly, Capabilities.AccessViewAudit);
            AddCapability(options, Policies.CanViewAudit, Capabilities.AccessViewAudit);
            AddCapability(options, Policies.CanTriggerDiagnostic, Capabilities.SystemDiagnostics);
            AddCapability(options, Policies.CanIdentityLookup, Capabilities.IdentityLookup);
            AddCapability(options, Policies.CanBulkIdentityLookup, Capabilities.IdentityLookup);
            AddCapability(options, Policies.CanViewDirectoryGroups, Capabilities.DirectoryGroupsView);
            AddCapability(options, Policies.CanViewDirectoryGroupMembers, Capabilities.DirectoryGroupMembersView);
            AddCapability(options, Policies.CanViewDirectoryPrivilegedGroups, Capabilities.DirectoryPrivilegedGroupsView);
            AddCapability(options, Policies.CanExportDirectoryGroups, Capabilities.DirectoryGroupExport);
            AddCapability(options, Policies.CanTeamView, Capabilities.TeamView);
            AddCapability(options, Policies.CanAccessAdministration, Capabilities.AccessManageUsers);
            AddCapability(options, Policies.CanSystemDiagnostics, Capabilities.SystemDiagnostics);
            AddCapability(options, Policies.CanViewOperationalRecords, Capabilities.OperationalRecordsView);
            AddCapability(options, Policies.CanPreviewJira, Capabilities.OperationalRecordsCreateJiraPreview);
            AddCapability(options, Policies.CanCreateJira, Capabilities.OperationalRecordsCreateJira);
            AddCapability(options, Policies.CanRetryJira, Capabilities.OperationalRecordsRetry);
            AddCapability(options, Policies.CanViewOperationalRecordDiagnostics, Capabilities.OperationalRecordsViewDiagnostics);
            AddCapability(options, Policies.CanManageUsers, Capabilities.AccessManageUsers);
            AddCapability(options, Policies.CanApproveAccessRequests, Capabilities.AccessApproveRequests);
            AddCapability(options, Policies.CanAssignRoles, Capabilities.AccessAssignRoles);
            AddCapability(options, Policies.CanViewAccessAudit, Capabilities.AccessViewAudit);
            AddCapability(options, Policies.CanViewManagementReports, Capabilities.ManagementReportingView);

            if (requireAuthenticatedFallback)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            }
        });

        return services;
    }

    private static void AddCapability(AuthorizationOptions options, string policyName, string capability) =>
        options.AddPolicy(policyName, policy => policy.RequireAuthenticatedUser().AddRequirements(new CapabilityRequirement(capability)));
}
