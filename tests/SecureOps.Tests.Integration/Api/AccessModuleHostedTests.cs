using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Api.Security;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Tests.Integration.Api;

public sealed class AccessModuleHostedTests
{
    [Fact]
    public async Task Modules_AdministratorSeesEveryModuleWithGrantingRoles()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(demoCompatibilityEnabled: true);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        AccessModuleOverviewResponse overview = (await admin.GetFromJsonAsync<AccessModuleOverviewResponse>("/api/v1/access/modules"))!;

        overview.Modules.Select(module => module.Module).Should().Contain(["In Use", "Bağlantılar", "OCO"]);
        overview.Modules.SelectMany(module => module.Actions).Single(action => action.Code == Capabilities.InUseView)
            .GrantedByRoles.Should().Contain("Admin");
        overview.Roles.Should().Contain(role => role.Code == "Admin");
    }

    [Fact]
    public async Task Modules_OrdinaryOperatorIsDenied()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(demoCompatibilityEnabled: true);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);

        HttpResponseMessage response = await lead.GetAsync("/api/v1/access/modules");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Effective_AdministratorExplainsAnotherUserAndEveryGrantHasARole()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(demoCompatibilityEnabled: true);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        JsonNode me = (await lead.GetFromJsonAsync<JsonNode>("/api/v1/access/me"))!;
        Guid leadId = me["userId"]!.GetValue<Guid>();

        AccessEffectiveResponse effective = (await admin.GetFromJsonAsync<AccessEffectiveResponse>($"/api/v1/access/users/{leadId}/effective"))!;

        effective.UserId.Should().Be(leadId);
        effective.Status.Should().Be("Approved");
        AccessEffectiveActionResponse[] granted = [.. effective.Modules.SelectMany(module => module.Actions).Where(action => action.Granted)];
        granted.Should().NotBeEmpty().And.HaveCount((int)effective.GrantedCount);
        granted.Should().OnlyContain(action => action.ViaRoles.Count > 0);
        effective.Modules.SelectMany(module => module.Actions).Where(action => !action.Granted).Should().OnlyContain(action => action.ViaRoles.Count == 0);
    }

    [Fact]
    public async Task Effective_OrdinaryOperatorCannotReadAnotherUser()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(demoCompatibilityEnabled: true);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        Guid adminId = (await admin.GetFromJsonAsync<JsonNode>("/api/v1/access/me"))!["userId"]!.GetValue<Guid>();

        HttpResponseMessage response = await lead.GetAsync($"/api/v1/access/users/{adminId}/effective");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MyEffective_PendingUserHasNothingGranted()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(demoCompatibilityEnabled: false);
        using HttpClient pending = Client(factory, DemoApiAuthentication.TeamLeadActor);

        AccessEffectiveResponse effective = (await pending.GetFromJsonAsync<AccessEffectiveResponse>("/api/v1/access/me/effective"))!;

        effective.Status.Should().Be("Pending");
        effective.GrantedCount.Should().Be(0);
        effective.Modules.SelectMany(module => module.Actions).Should().OnlyContain(action => !action.Granted);
    }

    // Before 2026-10-03 the paged lists returned 503 for every non-SQL provider, so Demo/local admin pages were empty.
    [Fact]
    public async Task PagedLists_WorkWithoutSqlAndKeepTheirPermissions()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(demoCompatibilityEnabled: true);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        await lead.GetAsync("/api/v1/access/me");

        AccessPage<AccessUserResponse> users = (await admin.GetFromJsonAsync<AccessPage<AccessUserResponse>>("/api/v1/access/users/page?page=1&pageSize=25"))!;
        AccessPage<AccessRequestResponse> requests = (await admin.GetFromJsonAsync<AccessPage<AccessRequestResponse>>("/api/v1/access/requests/page?page=1&pageSize=25"))!;
        HttpResponseMessage invalid = await admin.GetAsync("/api/v1/access/users/page?page=0&pageSize=25");
        HttpResponseMessage denied = await lead.GetAsync("/api/v1/access/users/page?page=1&pageSize=25");

        users.Total.Should().BeGreaterThanOrEqualTo(2);
        users.Items.Should().OnlyContain(user => user.RequestHistory.Count == 0);
        requests.Total.Should().BeGreaterThanOrEqualTo(0);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(bool demoCompatibilityEnabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", demoCompatibilityEnabled ? "true" : "false");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
        });
}
