using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Tests.Integration.Api;

public sealed class DirectoryExplorerHostedTests
{
    [Fact]
    public async Task TeamLead_CanViewPrincipalGroupsAndExactGroupMetadata()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.TeamLeadActor);

        HttpResponseMessage groupsResponse = await client.PostAsJsonAsync(
            "/api/v1/directory/principals/groups",
            new { account = "CONTOSO\\pam12356", purpose = Purpose, pageSize = 1 });
        HttpResponseMessage detailResponse = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/lookup",
            new { group = "ops-read", purpose = Purpose });

        groupsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        DirectoryGroupPageResponse? groups = await groupsResponse.Content.ReadFromJsonAsync<DirectoryGroupPageResponse>();
        groups!.Items.Should().ContainSingle(); groups.ContinuationToken.Should().NotBeNullOrWhiteSpace();
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        DirectoryGroupDetailResponse? detail = await detailResponse.Content.ReadFromJsonAsync<DirectoryGroupDetailResponse>();
        detail!.Group.Category.Should().Be("Security"); detail.Group.Scope.Should().Be("Global");
    }

    [Fact]
    public async Task PlatformAdmin_CanPageDirectTypedMembers()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/members",
            new { group = "ops-read", purpose = Purpose, pageSize = 2 });
        DirectoryMemberPageResponse? first = await firstResponse.Content.ReadFromJsonAsync<DirectoryMemberPageResponse>();
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/members",
            new { group = "ops-read", purpose = Purpose, pageSize = 2, continuationToken = first!.ContinuationToken });
        DirectoryMemberPageResponse? second = await secondResponse.Content.ReadFromJsonAsync<DirectoryMemberPageResponse>();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK); secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Items.Concat(second!.Items).Select(item => item.MemberType).Should().BeEquivalentTo("User", "Group", "Computer");
        second.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public async Task TeamLead_IsForbiddenFromDirectMemberEnumeration()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.TeamLeadActor);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/members",
            new { group = "ops-read", purpose = Purpose });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("missing-group", HttpStatusCode.NotFound, OperationalErrorCodes.DirectoryGroupNotFound)]
    [InlineData("(objectClass=*)", HttpStatusCode.BadRequest, OperationalErrorCodes.DirectoryInvalidInput)]
    public async Task GroupLookup_ReturnsStableSafeProblemDetails(string group, HttpStatusCode status, string code)
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/lookup",
            new { group, purpose = Purpose });
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(status);
        problem!["code"]!.GetValue<string>().Should().Be(code);
        problem["correlationId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DirectoryGroupRateLimit_ReturnsPlatformProblemDetails()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(groupQueryLimit: 1);
        using HttpClient client = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        object request = new { group = "ops-read", purpose = Purpose };

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/v1/directory/groups/lookup", request);
        HttpResponseMessage second = await client.PostAsJsonAsync("/api/v1/directory/groups/lookup", request);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var problem = JsonNode.Parse(await second.Content.ReadAsStringAsync());
        problem!["code"]!.GetValue<string>().Should().Be(OperationalErrorCodes.RateLimitExceeded);
    }

    [Fact]
    public async Task ProviderUnavailable_ReturnsStableServiceUnavailableProblem()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(provider: new UnavailableProvider());
        using HttpClient client = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/lookup",
            new { group = "ops-read", purpose = Purpose });
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        problem!["code"]!.GetValue<string>().Should().Be(OperationalErrorCodes.DirectoryProviderUnavailable);
        problem["stage"]!.GetValue<string>().Should().Be("provider");
    }

    [Fact]
    public void DependencyInjection_SelectsMatchingMockAndActiveDirectoryGroupChains()
    {
        using WebApplicationFactory<Program> mockFactory = CreateFactory();
        mockFactory.Services.GetRequiredService<IDirectoryGroupProvider>().Should().BeOfType<MockDirectoryGroupProvider>();

        using WebApplicationFactory<Program> activeFactory = CreateFactory(identityProvider: "ActiveDirectory");
        activeFactory.Services.GetRequiredService<IDirectoryGroupProvider>().Should().BeOfType<ActiveDirectoryDirectoryGroupProvider>();
        typeof(ActiveDirectoryDirectoryGroupProvider).GetConstructors().Should().ContainSingle();
        typeof(ActiveDirectoryGroupClient).GetConstructors().Should().ContainSingle();
    }

    [Fact]
    public async Task OpenApi_ContainsStableDirectoryExplorerRoutes()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(swagger: true);
        using HttpClient client = factory.CreateClient();

        string openApi = await client.GetStringAsync("/swagger/v1/swagger.json");

        openApi.Should().Contain("/api/v1/directory/principals/groups")
            .And.Contain("/api/v1/directory/groups/lookup")
            .And.Contain("/api/v1/directory/groups/members");
    }

    private const string Purpose = "Approved synthetic directory verification";

    private static WebApplicationFactory<Program> CreateFactory(
        int groupQueryLimit = 100,
        string identityProvider = "Mock",
        bool swagger = false,
        IDirectoryGroupProvider? provider = null) => new WebApplicationFactory<Program>()
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
            builder.UseSetting("RateLimiting:DirectoryGroupQuery:PermitLimit", groupQueryLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:DirectoryGroupMembers:PermitLimit", "100");
            builder.UseSetting("Swagger:Enabled", swagger ? "true" : "false");
            if (provider is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IDirectoryGroupProvider>();
                    services.AddSingleton(provider);
                });
            }
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string actor)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        return client;
    }

    private sealed class UnavailableProvider : IDirectoryGroupProvider
    {
        public string ProviderName => "Unavailable";
        public bool SupportsUpnLookup => false;
        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) => throw new DirectoryProviderUnavailableException();
        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) => throw new DirectoryProviderUnavailableException();
        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) => throw new DirectoryProviderUnavailableException();
    }
}
