using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Tests.Integration.Api;

public sealed class ResourcesApiTests
{
    [Fact]
    public async Task Api_EnforcesManagementAndReturnsOrderedOwnerOnlySets()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient admin = Client(factory, "platform-admin");
        using HttpClient lead = Client(factory, "team-lead");
        using HttpClient anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/resources/me", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await lead.PostAsJsonAsync("/api/v1/resources/categories", new SaveResourceCategoryRequest("Denied"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ResourceCategory category = await ReadAsync<ResourceCategory>(await admin.PostAsJsonAsync("/api/v1/resources/categories", new SaveResourceCategoryRequest("Synthetic"), cancellationToken: TestContext.Current.CancellationToken));
        var request = new SaveResourceLinkRequest(category.Id, "Synthetic link", "https://example.invalid/d?orgId=1", "Synthetic purpose");
        ResourceLink link = await ReadAsync<ResourceLink>(await admin.PostAsJsonAsync("/api/v1/resources/links", request, cancellationToken: TestContext.Current.CancellationToken));
        ResourcePage page = await ReadAsync<ResourcePage>(await lead.GetAsync("/api/v1/resources/links?page=1&pageSize=1&search=Synthetic", TestContext.Current.CancellationToken));
        page.Items.Single().Id.Should().Be(link.Id);
        ResourcePreferencesResponse preferences = await ReadAsync<ResourcePreferencesResponse>(await lead.PostAsJsonAsync("/api/v1/resources/me/sets", new SaveShiftSetRequest("Synthetic shift", [link.Id], true), cancellationToken: TestContext.Current.CancellationToken));
        Guid id = preferences.Sets.Single().Id;
        (await admin.GetAsync($"/api/v1/resources/me/sets/{id}/resolve", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync($"/api/v1/resources/me/sets/{id}", new SaveShiftSetRequest("Cross user", [], false, 0), cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/v1/resources/me/sets/{id}?expectedVersion=0", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        preferences = await ReadAsync<ResourcePreferencesResponse>(await lead.PutAsJsonAsync($"/api/v1/resources/me/favourites/{link.Id}", new SaveFavouriteRequest(true, preferences.Version), cancellationToken: TestContext.Current.CancellationToken));
        (await ReadAsync<ResourcePreferencesResponse>(await admin.GetAsync("/api/v1/resources/me", TestContext.Current.CancellationToken))).Favourites.Should().BeEmpty();
        (await admin.PutAsJsonAsync($"/api/v1/resources/links/{link.Id}", request with { Archived = true, ExpectedVersion = link.Version }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<ShiftSetResponse>(await lead.GetAsync($"/api/v1/resources/me/sets/{id}/resolve", TestContext.Current.CancellationToken))).Links.Should().BeEmpty();
        (await ReadAsync<ResourcePreferencesResponse>(await lead.GetAsync("/api/v1/resources/me", TestContext.Current.CancellationToken))).Favourites.Should().BeEmpty();
        (await lead.GetAsync($"/api/v1/resources/links/{link.Id}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await lead.GetAsync("/api/v1/resources/links?includeArchived=true", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Api_ValidationConflictsAndNullDefaultsAreCompatible()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient admin = Client(factory, "platform-admin");
        ResourcePreferencesResponse empty = await ReadAsync<ResourcePreferencesResponse>(await admin.GetAsync("/api/v1/resources/me", TestContext.Current.CancellationToken));
        empty.Version.Should().Be(0);
        empty.DefaultSetId.Should().BeNull();
        empty.Favourites.Should().BeEmpty();
        empty.Sets.Should().BeEmpty();
        ResourceCategory category = await ReadAsync<ResourceCategory>(await admin.PostAsJsonAsync("/api/v1/resources/categories", new SaveResourceCategoryRequest("Synthetic"), cancellationToken: TestContext.Current.CancellationToken));
        var request = new SaveResourceLinkRequest(category.Id, "Synthetic", "https://example.invalid/?token=synthetic", "Synthetic");
        (await admin.PostAsJsonAsync("/api/v1/resources/links", request, cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/v1/resources/links?pageSize=101", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/v1/resources/categories/{category.Id}", new SaveResourceCategoryRequest("Changed", ExpectedVersion: 1), cancellationToken: TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage conflict = await admin.PutAsJsonAsync($"/api/v1/resources/categories/{category.Id}", new SaveResourceCategoryRequest("Stale", ExpectedVersion: 1), cancellationToken: TestContext.Current.CancellationToken);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var json = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("code").GetString().Should().Be("ResourceConcurrencyConflict");
        json.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task OpenApi_AllResourceOperationsExposeSuccessSchemasAndNullableDefaults()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        int count = 0;
        foreach (JsonProperty path in root.GetProperty("paths").EnumerateObject().Where(p => p.Name.StartsWith("/api/v1/resources", StringComparison.Ordinal)))
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                JsonElement schema = operation.Value.GetProperty("responses").GetProperty("200").GetProperty("content")
                    .GetProperty("application/json").GetProperty("schema");
                schema.ValueKind.Should().Be(JsonValueKind.Object);
                count++;
            }
        }
        count.Should().Be(17);
        JsonElement schemas = root.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("ResourcePreferencesResponse").GetProperty("properties").GetProperty("defaultSetId")
            .GetProperty("nullable").GetBoolean().Should().BeTrue();
        schemas.GetProperty("ResourceLink").GetProperty("properties").GetProperty("notes")
            .GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AdditiveContracts_LegacyOmissionsRetainMembersAndExplicitRemovalsRemainOwnerScoped()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient admin = Client(factory, "platform-admin");
        using HttpClient owner = Client(factory, "team-lead");
        ResourceCategory category = await ReadAsync<ResourceCategory>(await admin.PostAsJsonAsync("/api/v1/resources/categories", new SaveResourceCategoryRequest("Synthetic contract"), cancellationToken: TestContext.Current.CancellationToken));
        var request = new SaveResourceLinkRequest(category.Id, "Synthetic", "https://example.invalid/contract", "Synthetic", Environment: "Pilot");
        ResourceLink link = await ReadAsync<ResourceLink>(await admin.PostAsJsonAsync("/api/v1/resources/links", request, cancellationToken: TestContext.Current.CancellationToken));
        ResourcePreferencesResponse personal = await ReadAsync<ResourcePreferencesResponse>(await owner.PostAsJsonAsync("/api/v1/resources/me/sets", new SaveShiftSetRequest("Original", [link.Id]), cancellationToken: TestContext.Current.CancellationToken));
        Guid id = personal.Sets.Single().Id;
        await admin.PutAsJsonAsync($"/api/v1/resources/links/{link.Id}", request with { Archived = true, ExpectedVersion = 1 }, cancellationToken: TestContext.Current.CancellationToken);
        personal = await ReadAsync<ResourcePreferencesResponse>(await owner.PutAsJsonAsync($"/api/v1/resources/me/sets/{id}", new { name = "Legacy rename", linkIds = Array.Empty<Guid>(), isDefault = true, expectedVersion = personal.Version }, cancellationToken: TestContext.Current.CancellationToken));
        personal.Sets.Single().Links.Should().BeEmpty();
        personal.DefaultSetId.Should().Be(id);
        (await ReadAsync<ResourceEnvironmentOptions>(await owner.GetAsync("/api/v1/resources/environments", TestContext.Current.CancellationToken))).Values.Should().BeEmpty();
        (await owner.GetAsync("/api/v1/resources/environments?includeArchived=true", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await admin.PutAsJsonAsync($"/api/v1/resources/links/{link.Id}", request with { ExpectedVersion = 2 }, cancellationToken: TestContext.Current.CancellationToken);
        personal = await ReadAsync<ResourcePreferencesResponse>(await owner.GetAsync("/api/v1/resources/me", TestContext.Current.CancellationToken));
        personal.Sets.Single().Links.Single().Id.Should().Be(link.Id);
        (await ReadAsync<ResourceEnvironmentOptions>(await owner.GetAsync("/api/v1/resources/environments?search=pilot", TestContext.Current.CancellationToken))).Values.Should().Equal("Pilot");
        personal = await ReadAsync<ResourcePreferencesResponse>(await owner.PutAsJsonAsync("/api/v1/resources/me/guide", new DismissResourceGuideRequest(personal.Version), cancellationToken: TestContext.Current.CancellationToken));
        personal.GuideDismissed.Should().BeTrue();
        (await ReadAsync<ResourcePreferencesResponse>(await admin.GetAsync("/api/v1/resources/me", TestContext.Current.CancellationToken))).GuideDismissed.Should().BeFalse();
        personal = await ReadAsync<ResourcePreferencesResponse>(await owner.PutAsJsonAsync($"/api/v1/resources/me/sets/{id}", new SaveShiftSetRequest("Removed", [], true, personal.Version, [link.Id]), cancellationToken: TestContext.Current.CancellationToken));
        personal.Sets.Single().Links.Should().BeEmpty();
        (await owner.PutAsJsonAsync($"/api/v1/resources/me/sets/{id}", new SaveShiftSetRequest("Stale", [], ExpectedVersion: 1), cancellationToken: TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Workspace_ValidatesBodyVersionCapabilitiesAndCurrentOwner()
    {
        using WebApplicationFactory<Program> factory = Factory();
        using HttpClient owner = Client(factory, "team-lead");
        using HttpClient admin = Client(factory, "platform-admin");
        var layout = new ResourceWorkspaceLayout("list", "compact", 10, ["groups", "links"]);
        ResourcePreferencesResponse saved = await ReadAsync<ResourcePreferencesResponse>(
            await owner.PutAsJsonAsync("/api/v1/resources/me/layout", new SaveResourceLayoutRequest(layout, 0), cancellationToken: TestContext.Current.CancellationToken));
        saved.WorkspaceLayout.Should().BeEquivalentTo(layout);
        (await owner.PutAsJsonAsync("/api/v1/resources/me/layout", new SaveResourceLayoutRequest(layout, 0), cancellationToken: TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync<ResourcePreferencesResponse>(await admin.GetAsync("/api/v1/resources/me", TestContext.Current.CancellationToken))).Version.Should().Be(0);
        (await owner.PutAsJsonAsync("/api/v1/resources/me/layout", new SaveResourceLayoutRequest(layout with { Shortcuts = ["catalogue"] }, 1), cancellationToken: TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync("/api/v1/resources/me/layout", new { layout = (object?)null, expectedVersion = 1 }, cancellationToken: TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync("/api/v1/resources/links/resolve", new { linkIds = Array.Empty<Guid>() }, cancellationToken: TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync<ResourceLink[]>(await owner.PostAsJsonAsync("/api/v1/resources/links/resolve", new ResolveResourceLinksRequest([Guid.NewGuid()]), cancellationToken: TestContext.Current.CancellationToken)))
            .Should().BeEmpty();
    }

    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Demo");
        builder.UseSetting("DemoAuth:Enabled", "true");
        builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
        builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
        builder.UseSetting("Access:RepositoryProvider", "InMemory");
        builder.UseSetting("SessionSecurity:RepositoryProvider", "InMemory");
        builder.UseSetting("Audit:Provider", "InMemory");
        builder.UseSetting("Swagger:Enabled", "true");
    });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
