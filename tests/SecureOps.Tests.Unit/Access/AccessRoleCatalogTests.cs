using FluentAssertions;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;

namespace SecureOps.Tests.Unit.Access;

public sealed class AccessRoleCatalogTests
{
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
