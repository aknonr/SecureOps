using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureOps.Api.Security;
using SecureOps.Shared.Auth;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityAuthorizationPolicyTests
{
    [Theory]
    [InlineData(Policies.TeamLeadOrAbove, Capabilities.IdentityLookup)]
    [InlineData(Policies.CanViewOperationalRecords, Capabilities.OperationalRecordsView)]
    [InlineData(Policies.CanPreviewJira, Capabilities.OperationalRecordsCreateJiraPreview)]
    [InlineData(Policies.CanCreateJira, Capabilities.OperationalRecordsCreateJira)]
    [InlineData(Policies.CanRetryJira, Capabilities.OperationalRecordsRetry)]
    [InlineData(Policies.CanViewOperationalRecordDiagnostics, Capabilities.OperationalRecordsViewDiagnostics)]
    [InlineData(Policies.CanApproveAccessRequests, Capabilities.AccessApproveRequests)]
    [InlineData(Policies.CanViewDirectoryGroups, Capabilities.DirectoryGroupsView)]
    [InlineData(Policies.CanViewDirectoryGroupMembers, Capabilities.DirectoryGroupMembersView)]
    [InlineData(Policies.CanViewDirectoryPrivilegedGroups, Capabilities.DirectoryPrivilegedGroupsView)]
    public void Policies_RequireApplicationCapabilityInsteadOfAdGroup(string policyName, string capability)
    {
        using ServiceProvider provider = BuildServices();
        AuthorizationPolicy policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(policyName)!;

        policy.Requirements.OfType<CapabilityRequirement>().Should().ContainSingle()
            .Which.Capability.Should().Be(capability);
        policy.Requirements.Should().NotContain(requirement => requirement is RolesAuthorizationRequirement);
    }

    [Fact]
    public void NonDevelopmentFallbackPolicy_RequiresAuthentication()
    {
        using ServiceProvider provider = BuildServices();
        AuthorizationPolicy? fallback = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;

        fallback.Should().NotBeNull();
        fallback!.Requirements.Should().Contain(requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    private static ServiceProvider BuildServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddOptions();
        services.AddSecureOpsAuthorization(new ConfigurationBuilder().Build(), requireAuthenticatedFallback: true);
        return services.BuildServiceProvider();
    }
}
