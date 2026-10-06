using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Contracts.Directory;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Integration.Api;

public sealed class DirectoryExplorerPhase2HostedTests
{
    [Theory]
    [InlineData("CONTOSO\\SYN.GMSA$", "GroupManagedServiceAccount")]
    [InlineData("syn.msa$", "ManagedServiceAccount")]
    [InlineData("pam12356", "User")]
    [InlineData("pam12356@contoso.local", "User")]
    public async Task ExactAccount_IdentityAndDirectoryRoutes_ReturnSafeClassEvidence(string account, string evidence)
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.TeamLeadActor);
        IdentityLookupResponse identity = await PostAsync<IdentityLookupResponse>(client,
            "/api/v1/identity/lookup", new { account });
        DirectoryServiceEvidenceResponse service = await PostAsync<DirectoryServiceEvidenceResponse>(client,
            "/api/v1/directory/principals/service-evidence", new { account });
        DirectoryAccountHealthResponse health = await PostAsync<DirectoryAccountHealthResponse>(client,
            "/api/v1/directory/principals/account-health", new { account });
        DirectoryGroupPageResponse groups = await PostAsync<DirectoryGroupPageResponse>(client,
            "/api/v1/directory/principals/groups", new { account });
        DirectoryPrincipalMembershipsResponse memberships = await PostAsync<DirectoryPrincipalMembershipsResponse>(client,
            "/api/v1/directory/principals/memberships", new { account });
        DirectoryMembershipPathResponse paths = await PostAsync<DirectoryMembershipPathResponse>(client,
            "/api/v1/directory/principals/membership-paths", new { account, targetGroup = "platform-privileged" });
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        DirectoryPrivilegedMembershipResponse privileged = await PostAsync<DirectoryPrivilegedMembershipResponse>(admin,
            "/api/v1/directory/principals/privileged-memberships", new { account });

        identity.User!.AccountTypeEvidence.Should().Be(evidence);
        service.AccountTypeEvidence.Should().Be(evidence);
        health.Enabled.Should().BeTrue();
        groups.Items.Should().NotBeEmpty();
        memberships.DirectGroups.Should().ContainSingle(group => group.Group.MembershipKind == "Primary");
        paths.IsMember.Should().BeTrue();
        privileged.Groups.Should().ContainSingle(group => group.ConfiguredIdentifier == "ops-read" && group.Direct);
    }

    [Theory]
    [InlineData("missing.gmsa$", HttpStatusCode.NotFound)]
    [InlineData("syn.gmsa", HttpStatusCode.NotFound)]
    [InlineData("$", HttpStatusCode.BadRequest)]
    [InlineData("syn$gmsa", HttpStatusCode.BadRequest)]
    [InlineData("syn.gmsa$$", HttpStatusCode.BadRequest)]
    [InlineData("syn.gmsa$@example.invalid", HttpStatusCode.BadRequest)]
    [InlineData("syn.gmsa*$", HttpStatusCode.BadRequest)]
    [InlineData("syn.gmsa?", HttpStatusCode.BadRequest)]
    [InlineData("syn.gmsa%", HttpStatusCode.BadRequest)]
    [InlineData("syn.gmsa$)(objectClass=*)", HttpStatusCode.BadRequest)]
    public async Task ExactAccount_MissingOrUnsafeInput_RejectsOnIdentityAndDirectoryRoutes(string account, HttpStatusCode status)
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        foreach (string route in new[] { "identity/lookup", "directory/principals/service-evidence",
            "directory/principals/account-health", "directory/principals/groups", "directory/principals/memberships",
            "directory/principals/membership-paths", "directory/principals/privileged-memberships" })
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/" + route,
                new { account, targetGroup = "platform-privileged" });
            response.StatusCode.Should().Be(status);
        }
    }

    [Fact]
    public async Task TeamLead_CanUseGeneralReadOnlyEnrichmentEndpoints()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.TeamLeadActor);

        DirectoryPrincipalMembershipsResponse memberships = await PostAsync<DirectoryPrincipalMembershipsResponse>(
            client,
            "/api/v1/directory/principals/memberships",
            new { account = "CONTOSO\\pam12356" });
        DirectoryMembershipPathResponse paths = await PostAsync<DirectoryMembershipPathResponse>(
            client,
            "/api/v1/directory/principals/membership-paths",
            new { account = "pam12356", targetGroup = "platform-privileged" });
        DirectoryAccountHealthResponse health = await PostAsync<DirectoryAccountHealthResponse>(
            client,
            "/api/v1/directory/principals/account-health",
            new { account = "pam12356" });
        DirectoryServiceEvidenceResponse evidence = await PostAsync<DirectoryServiceEvidenceResponse>(
            client,
            "/api/v1/directory/principals/service-evidence",
            new { account = "pam12356" });

        memberships.DirectGroups.Should().HaveCount(3);
        memberships.DirectGroups.Should().ContainSingle(group => group.Group.MembershipKind == "Primary");
        memberships.TransitiveGroups.Should().ContainSingle(group =>
            group.Group.SamAccountName == "platform-privileged");
        memberships.Traversal.CycleDetected.Should().BeTrue();
        paths.IsMember.Should().BeTrue();
        paths.IsDirect.Should().BeFalse();
        paths.Paths.Should().HaveCount(2);
        health.Enabled.Should().BeTrue();
        health.LastLogonTimestampIsApproximate.Should().BeTrue();
        evidence.ServicePrincipalNames.Should().BeEmpty();
        evidence.ServicePrincipalNameCount.Should().Be(0);
        evidence.ServicePrincipalNamesTruncated.Should().BeFalse();
        evidence.AccountTypeEvidence.Should().Be("User");
    }

    [Fact]
    public async Task MembershipPath_DistinguishesUnknownTargetAndProvenNonMembership()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.TeamLeadActor);

        HttpResponseMessage unknown = await client.PostAsJsonAsync(
            "/api/v1/directory/principals/membership-paths",
            new { account = "pam12356", targetGroup = "missing-group", purpose = _purpose });
        HttpResponseMessage unrelated = await client.PostAsJsonAsync(
            "/api/v1/directory/principals/membership-paths",
            new { account = "pam12356", targetGroup = "unrelated-group", purpose = _purpose });
        DirectoryMembershipPathResponse? result =
            await unrelated.Content.ReadFromJsonAsync<DirectoryMembershipPathResponse>();

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unrelated.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.IsMember.Should().BeFalse();
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task PrivilegedMemberships_AreAdminOnlyAndUseExactConfiguredGroups()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        var request = new { account = "pam12356", purpose = _purpose };

        HttpResponseMessage allowed = await admin.PostAsJsonAsync(
            "/api/v1/directory/principals/privileged-memberships", request);
        HttpResponseMessage forbidden = await lead.PostAsJsonAsync(
            "/api/v1/directory/principals/privileged-memberships", request);
        DirectoryPrivilegedMembershipResponse? response =
            await allowed.Content.ReadFromJsonAsync<DirectoryPrivilegedMembershipResponse>();

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        response!.Groups.Should().ContainSingle(group => group.ConfiguredIdentifier == "ops-read" && group.Direct);
        response.Groups.Should().ContainSingle(group =>
            group.ConfiguredIdentifier == "platform-privileged" && group.Transitive);
        response.Groups.Should().ContainSingle(group =>
            group.ConfiguredIdentifier == "missing-group" && !group.GroupFound);
    }

    [Fact]
    public async Task EnrichmentRateLimit_IsAppliedIndependently()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(enrichmentLimit: 1);
        using HttpClient client = Client(factory, DemoApiAuthentication.TeamLeadActor);
        var firstRequest = new { account = "pam12356", purpose = "first optional context" };
        var secondRequest = new { account = "pam12356", purpose = "different optional context" };

        HttpResponseMessage first = await client.PostAsJsonAsync(
            "/api/v1/directory/principals/account-health", firstRequest);
        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/v1/directory/principals/account-health", secondRequest);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void DependencyInjection_SelectsMatchingEnrichmentProviderChains()
    {
        using WebApplicationFactory<Program> mockFactory = CreateFactory();
        mockFactory.Services.GetRequiredService<IDirectoryEnrichmentProvider>()
            .Should().BeOfType<MockDirectoryEnrichmentProvider>();

        using WebApplicationFactory<Program> activeFactory = CreateFactory(identityProvider: "ActiveDirectory");
        activeFactory.Services.GetRequiredService<IDirectoryEnrichmentProvider>()
            .Should().BeOfType<ActiveDirectoryDirectoryEnrichmentProvider>();
        activeFactory.Services.GetRequiredService<IActiveDirectoryEnrichmentClient>()
            .Should().BeOfType<ActiveDirectoryGroupClient>();
        typeof(ActiveDirectoryDirectoryEnrichmentProvider).GetConstructors().Should().ContainSingle();
        typeof(ActiveDirectoryGroupClient).GetConstructors().Should().ContainSingle();
    }

    [Fact]
    public async Task OpenApi_ContainsPhase2Routes()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(swagger: true);
        using HttpClient client = factory.CreateApiClient();

        string openApi = await client.GetStringAsync("/swagger/v1/swagger.json");

        openApi.Should().Contain("/api/v1/directory/principals/memberships")
            .And.Contain("/api/v1/directory/principals/membership-paths")
            .And.Contain("/api/v1/directory/principals/account-health")
            .And.Contain("/api/v1/directory/principals/service-evidence")
            .And.Contain("/api/v1/directory/principals/privileged-memberships");
    }

    private const string _purpose = "Approved synthetic directory enrichment verification";

    private static WebApplicationFactory<Program> CreateFactory(
        int enrichmentLimit = 100,
        string identityProvider = "Mock",
        bool swagger = false) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", identityProvider);
            builder.UseSetting("IdentityLookup:DomainName", "example.invalid");
            builder.UseSetting("IdentityLookup:EnableUpnLookup", "true");
            builder.UseSetting("DirectoryExplorer:PrivilegedGroupIdentifiers:0", "ops-read");
            builder.UseSetting("DirectoryExplorer:PrivilegedGroupIdentifiers:1", "platform-privileged");
            builder.UseSetting("DirectoryExplorer:PrivilegedGroupIdentifiers:2", "missing-group");
            builder.UseSetting(
                "RateLimiting:DirectoryEnrichment:PermitLimit",
                enrichmentLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:DirectoryPrivilegedGroups:PermitLimit", "100");
            builder.UseSetting("Swagger:Enabled", swagger ? "true" : "false");
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
