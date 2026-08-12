using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureOps.Api.Security;
using SecureOps.Shared.Auth;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityAuthorizationPolicyTests
{
    [Theory]
    [InlineData("CONTOSO\\SecureOps-Leads", true)]
    [InlineData("CONTOSO\\SecureOps-Admins", true)]
    [InlineData("CONTOSO\\SecureOps-Operators", false)]
    [InlineData("CONTOSO\\SecureOps-Auditors", false)]
    public async Task TeamLeadOrAbovePolicy_MapsToConfiguredAdGroups(string group, bool expected)
    {
        await using ServiceProvider provider = BuildServices();
        IAuthorizationService authorization = provider.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal user = CreateUser(group);

        AuthorizationResult result = await authorization.AuthorizeAsync(user, null, Policies.TeamLeadOrAbove);

        result.Succeeded.Should().Be(expected);
    }

    [Fact]
    public async Task NonDevelopmentFallbackPolicy_RejectsAnonymousUsers()
    {
        await using ServiceProvider provider = BuildServices();
        IAuthorizationService authorization = provider.GetRequiredService<IAuthorizationService>();
        AuthorizationOptions options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        options.FallbackPolicy.Should().NotBeNull();

        AuthorizationResult result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            null,
            options.FallbackPolicy!);

        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(Policies.CanViewOperationalRecords, "CONTOSO\\SecureOps-Operators", true)]
    [InlineData(Policies.CanPreviewJira, "CONTOSO\\SecureOps-Operators", true)]
    [InlineData(Policies.CanCreateJira, "CONTOSO\\SecureOps-Operators", false)]
    [InlineData(Policies.CanCreateJira, "CONTOSO\\SecureOps-JiraPublishers", true)]
    [InlineData(Policies.CanRetryJira, "CONTOSO\\SecureOps-Leads", true)]
    [InlineData(Policies.CanViewOperationalRecordDiagnostics, "CONTOSO\\SecureOps-Auditors", true)]
    public async Task OperationalRecordPolicies_MapBootstrapGroups(string policy, string group, bool expected)
    {
        await using ServiceProvider provider = BuildServices();
        IAuthorizationService authorization = provider.GetRequiredService<IAuthorizationService>();

        AuthorizationResult result = await authorization.AuthorizeAsync(CreateUser(group), null, policy);

        result.Succeeded.Should().Be(expected);
    }

    private static ServiceProvider BuildServices()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rbac:OperatorsGroup"] = "CONTOSO\\SecureOps-Operators",
                ["Rbac:LeadsGroup"] = "CONTOSO\\SecureOps-Leads",
                ["Rbac:AdminsGroup"] = "CONTOSO\\SecureOps-Admins",
                ["Rbac:AuditorsGroup"] = "CONTOSO\\SecureOps-Auditors",
                ["Rbac:JiraPublishersGroup"] = "CONTOSO\\SecureOps-JiraPublishers"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddOptions();
        services.AddSecureOpsAuthorization(configuration, requireAuthenticatedFallback: true);
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUser(string group)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "CONTOSO\\identity.reviewer"),
                new Claim(ClaimTypes.Role, group)
            ],
            authenticationType: "TestAuth"));
    }
}
