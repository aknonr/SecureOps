using FluentAssertions;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Auth;

namespace SecureOps.Tests.Unit.Access;

public sealed class AccessRoleCatalogTests
{
    [Fact]
    public void Admin_ModuleNavigationAndAdministration_DoNotGrantOperationalActionsOrScope()
    {
        AccessRoleCatalog.GetCapabilities(["Admin"]).Intersect(ServiceAccountCapabilities.All)
            .Should().BeEquivalentTo(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        foreach (string role in AccessRoleCatalog.RoleCodes.Where(role => role != "Admin"))
        {
            AccessRoleCatalog.GetCapabilities([role]).Intersect(ServiceAccountCapabilities.All).Should().BeEmpty();
        }
    }

    [Fact]
    public void OperationalRoleLabels_AreDisplayOnlyAndMatchReviewedVocabulary()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Admin"] = "Sistem Yöneticisi",
            ["Auditor"] = "Denetim Görüntüleyicisi",
            ["JiraPublisher"] = "Jira İşlem Yetkilisi",
            ["Lead"] = "Takım Lideri",
            ["Operator"] = "Operasyon Uzmanı",
            ["ReadOnly"] = "Sadece Görüntüleme"
        };

        AccessRoleCatalog.RoleCodes.Should().BeEquivalentTo(expected.Keys.Concat(["ResourceCurator", "InUseReviewer", "InUseCoordinator"]));
        foreach ((string role, string label) in expected)
        {
            AccessLabels.RoleLabel(role).Should().Be(label);
            AccessLabels.RoleDescription(role).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void JiraPublishCapability_RemainsGrantedOnlyToReviewedPublishingRoles()
    {
        foreach (string role in new[] { "Admin", "Lead", "JiraPublisher" })
        {
            AccessRoleCatalog.GetCapabilities([role]).Should().Contain(Capabilities.OperationalRecordsCreateJira);
        }

        foreach (string role in new[] { "Operator", "Auditor", "ReadOnly" })
        {
            AccessRoleCatalog.GetCapabilities([role]).Should().NotContain(Capabilities.OperationalRecordsCreateJira);
        }
    }

    [Fact]
    public void ManagementReportingCapability_IsLimitedToAdminAndAuditor()
    {
        AccessRoleCatalog.GetCapabilities(["Admin"]).Should().Contain(Capabilities.ManagementReportingView);
        AccessRoleCatalog.GetCapabilities(["Auditor"]).Should().Contain(Capabilities.ManagementReportingView);

        foreach (string role in new[] { "Lead", "Operator", "JiraPublisher", "ReadOnly" })
        {
            AccessRoleCatalog.GetCapabilities([role]).Should().NotContain(Capabilities.ManagementReportingView);
        }
    }

    [Fact]
    public void PrivilegedDirectoryCapability_IsLimitedToAdmin()
    {
        AccessRoleCatalog.GetCapabilities(["Admin"]).Should().Contain(Capabilities.DirectoryPrivilegedGroupsView);

        foreach (string role in new[] { "Lead", "Operator", "JiraPublisher", "Auditor", "ReadOnly" })
        {
            AccessRoleCatalog.GetCapabilities([role]).Should().NotContain(Capabilities.DirectoryPrivilegedGroupsView);
        }
    }

    [Fact]
    public void DirectoryExportCapability_IsLimitedToAdmin()
    {
        AccessRoleCatalog.GetCapabilities(["Admin"]).Should().Contain(Capabilities.DirectoryGroupExport);

        foreach (string role in new[] { "Lead", "Operator", "JiraPublisher", "Auditor", "ReadOnly" })
        {
            AccessRoleCatalog.GetCapabilities([role]).Should().NotContain(Capabilities.DirectoryGroupExport);
        }
    }
}
