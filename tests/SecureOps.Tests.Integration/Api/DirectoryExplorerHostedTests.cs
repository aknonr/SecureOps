using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Audit;
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
            new { account = "CONTOSO\\pam12356", pageSize = 1 });
        HttpResponseMessage detailResponse = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/lookup",
            new { group = "ops-read" });

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
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient client = Client(factory, DemoApiAuthentication.TeamLeadActor);
        object request = new { group = "ops-read" };

        HttpResponseMessage cached = await admin.PostAsJsonAsync("/api/v1/directory/groups/members", request);
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/directory/groups/members", request);

        cached.StatusCode.Should().Be(HttpStatusCode.OK);
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
        object firstRequest = new { group = "ops-read", purpose = "first optional context" };
        object secondRequest = new { group = "ops-read", purpose = "different optional context" };

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/v1/directory/groups/lookup", firstRequest);
        HttpResponseMessage second = await client.PostAsJsonAsync("/api/v1/directory/groups/lookup", secondRequest);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var problem = JsonNode.Parse(await second.Content.ReadAsStringAsync());
        problem!["code"]!.GetValue<string>().Should().Be(OperationalErrorCodes.RateLimitExceeded);
        factory.Services.GetRequiredService<InMemoryAuditWriter>().Events
            .Should().Contain(item => item.Action == AuditActions.DirectoryGroupQueryRateLimited);
    }

    [Fact]
    public async Task MalformedDirectoryPayload_IsRejectedBeforeProviderAccess()
    {
        CountingProvider provider = new();
        using WebApplicationFactory<Program> factory = CreateFactory(provider: provider);
        using HttpClient client = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using StringContent payload = new("{\"group\":", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync("/api/v1/directory/groups/lookup", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        provider.Calls.Should().Be(0);
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
    public async Task DirectAndEnrichmentOperations_DoNotCollideInSharedCache()
    {
        DualDirectoryProvider provider = new();
        using WebApplicationFactory<Program> factory = CreateFactory(provider: provider, enrichmentProvider: provider);
        using HttpClient client = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage group = await client.PostAsJsonAsync(
            "/api/v1/directory/groups/lookup", new { group = "shared-principal" });
        HttpResponseMessage health = await client.PostAsJsonAsync(
            "/api/v1/directory/principals/account-health", new { account = "shared-principal" });

        group.StatusCode.Should().Be(HttpStatusCode.OK);
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.GroupLookupCalls.Should().Be(1);
        provider.PrincipalLookupCalls.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentAuthorizedActors_ShareOneIdenticalProviderRead()
    {
        BlockingGroupProvider provider = new();
        using WebApplicationFactory<Program> factory = CreateFactory(provider: provider);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        object request = new { group = "shared-group" };

        Task<HttpResponseMessage> first = lead.PostAsJsonAsync("/api/v1/directory/groups/lookup", request);
        Task<HttpResponseMessage> second = admin.PostAsJsonAsync("/api/v1/directory/groups/lookup", request);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        provider.Release();
        HttpResponseMessage[] responses = await Task.WhenAll(first, second);

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        provider.Calls.Should().Be(1);
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
            .And.Contain("/api/v1/directory/groups/members")
            .And.Contain("/api/v1/directory/groups/analysis")
            .And.Contain("/api/v1/directory/groups/export");
    }

    private const string Purpose = "Approved synthetic directory verification";

    private static WebApplicationFactory<Program> CreateFactory(
        int groupQueryLimit = 100,
        string identityProvider = "Mock",
        bool swagger = false,
        IDirectoryGroupProvider? provider = null,
        IDirectoryEnrichmentProvider? enrichmentProvider = null) => new WebApplicationFactory<Program>()
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
            if (enrichmentProvider is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IDirectoryEnrichmentProvider>();
                    services.AddSingleton(enrichmentProvider);
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

    private sealed class CountingProvider : IDirectoryGroupProvider
    {
        public int Calls { get; private set; }
        public string ProviderName => "Counting";
        public bool SupportsUpnLookup => false;
        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);
        }
        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<DirectoryGroupRecord?>(null);
        }
        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null);
        }
    }

    private sealed class DualDirectoryProvider : IDirectoryGroupProvider, IDirectoryEnrichmentProvider
    {
        public int GroupLookupCalls { get; private set; }
        public int PrincipalLookupCalls { get; private set; }
        public string ProviderName => "SharedSynthetic";
        public bool SupportsUpnLookup => false;

        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken)
        {
            GroupLookupCalls++;
            return Task.FromResult<DirectoryGroupRecord?>(new(
                "S-1-5-21-100", "Shared Principal", normalizedGroup, null, null, "Security", "Global"));
        }

        public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(string normalizedAccount, int maxSpns, CancellationToken cancellationToken)
        {
            PrincipalLookupCalls++;
            return Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(new(
                "S-1-5-21-200", "Shared Principal", normalizedAccount, null, true, false,
                null, null, null, null, null, null, [], 0, false, "User"));
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(new([], false));

        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(new([], false));

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(string normalizedAccount, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(new([], false));

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(DirectoryGroupRecord group, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(new([], false));
    }

    private sealed class BlockingGroupProvider : IDirectoryGroupProvider
    {
        private readonly TaskCompletionSource<DirectoryGroupRecord?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public string ProviderName => "BlockingSynthetic";
        public bool SupportsUpnLookup => false;

        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            return _result.Task;
        }

        public void Release() => _result.TrySetResult(new(
            "S-1-5-21-300", "Shared Group", "shared-group", null, null, "Security", "Global"));

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);

        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null);
    }
}
