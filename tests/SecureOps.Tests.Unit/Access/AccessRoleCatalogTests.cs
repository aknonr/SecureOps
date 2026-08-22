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
}
