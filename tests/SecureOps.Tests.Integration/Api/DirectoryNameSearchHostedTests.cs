using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.Api;

public sealed class DirectoryNameSearchHostedTests
{
    private const string _route = "/api/v1/identity/name-search";

    [Theory]
    [InlineData("ayse")]
    [InlineData("ayse yilmaz")]
    [InlineData("ismail isik")]
    public async Task IdentityOnlyUser_WithDisabledModule_CanSearchBoundedNamesWithoutInventoryLinks(string query)
    {
        CountingDirectory directory = new();
        ControlledAudit audit = new();
        using WebApplicationFactory<Program> factory = Factory(directory, audit);
        using HttpClient client = await ApprovedClientAsync(factory, "Lead");
        CurrentAccessResponse access = (await client.GetFromJsonAsync<CurrentAccessResponse>("/api/v1/access/me"))!;
        access.AccessStatus.Should().Be("Approved");
        access.Capabilities.Should().Contain(Capabilities.IdentityLookup).And.NotContain(ServiceAccountCapabilities.View);
        HttpResponseMessage response = await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest(query));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DirectoryNameSearchResponse found = (await response.Content.ReadFromJsonAsync<DirectoryNameSearchResponse>())!;
        found.Matches.Should().NotBeEmpty().And.OnlyContain(match => match.ServiceAccountId == null);
        found.Matches.Count.Should().BeLessThanOrEqualTo(10);
        (await client.GetAsync("/api/v1/service-accounts/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        directory.Calls.Should().Be(1);
        string details = JsonSerializer.Serialize(audit.Events.Where(e => e.Action.StartsWith("Identity.DirectoryNameSearch", StringComparison.Ordinal)));
        details.Should().NotContain(query).And.NotContain("syn.ayse").And.Contain("QueryHash");
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("ReadOnly", HttpStatusCode.Forbidden)]
    public async Task PersistedCapabilities_NotSuccessfulLogin_DecideNameSearch(string role, HttpStatusCode expected)
    {
        CountingDirectory directory = new();
        using WebApplicationFactory<Program> factory = Factory(directory, new ControlledAudit());
        using HttpClient client = await ApprovedClientAsync(factory, role);
        (await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest("ayse"))).StatusCode.Should().Be(expected);
        directory.Calls.Should().Be(expected == HttpStatusCode.OK ? 1 : 0);
        using HttpClient anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync(_route, new DirectoryNameSearchRequest("ayse"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("ay")]
    [InlineData("ay*")]
    [InlineData("*)(objectClass=*")]
    public async Task InvalidInput_IsRejectedAndAuditedBeforeProviderAccess(string query)
    {
        CountingDirectory directory = new();
        ControlledAudit audit = new();
        using WebApplicationFactory<Program> factory = Factory(directory, audit);
        using HttpClient client = await ApprovedClientAsync(factory, "Lead");
        (await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest(query))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        directory.Calls.Should().Be(0);
        audit.Events.Should().ContainSingle(e => e.Action == "Identity.DirectoryNameSearchRejected");
    }

    [Theory]
    [InlineData("Requested", 0)]
    [InlineData("Completed", 1)]
    [InlineData("Failed", 1)]
    public async Task RequiredAuditFailure_FailsClosedAtEveryStage(string stage, int expectedCalls)
    {
        CountingDirectory directory = new() { Fail = stage == "Failed" };
        using WebApplicationFactory<Program> factory = Factory(directory, new ControlledAudit(stage));
        using HttpClient client = await ApprovedClientAsync(factory, "Lead");
        HttpResponseMessage response = await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest("ayse"));
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("AuditStoreUnavailable").And.NotContain("ayse");
        directory.Calls.Should().Be(expectedCalls);
    }

    [Fact]
    public async Task ProviderFailure_ProducesUnavailableAndNameFreeAudit()
    {
        ControlledAudit audit = new();
        using WebApplicationFactory<Program> factory = Factory(new CountingDirectory { Fail = true }, audit);
        using HttpClient client = await ApprovedClientAsync(factory, "Lead");
        HttpResponseMessage response = await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest("ayse"));
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("displayName=");
        audit.Events.Should().ContainSingle(e => e.Action == "Identity.DirectoryNameSearchFailed");
    }

    [Fact]
    public async Task QueueAcknowledgement_CannotBypassRequiredDirectAuditPersistence()
    {
        CountingDirectory directory = new();
        using WebApplicationFactory<Program> factory = Factory(directory, new ControlledAudit("Requested"))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter>(new QueueAcknowledgementOnly());
            }));
        using HttpClient client = await ApprovedClientAsync(factory, "Lead");
        (await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest("ayse"))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        directory.Calls.Should().Be(0, "durable requested audit must complete before a directory read");
    }

    [Fact]
    public async Task SharedIdentityRateLimit_BoundsGeneralNameSearch()
    {
        CountingDirectory directory = new();
        using WebApplicationFactory<Program> factory = Factory(directory, new ControlledAudit(), 1);
        using HttpClient client = await ApprovedClientAsync(factory, "Lead");
        (await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest("ayse"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(_route, new DirectoryNameSearchRequest("ayse"))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        directory.Calls.Should().Be(1);
    }

    private static WebApplicationFactory<Program> Factory(CountingDirectory directory, ControlledAudit audit, int limit = 100) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "false");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("ServiceAccounts:Provider", "Disabled");
            builder.UseSetting("RateLimiting:IdentityLookup:PermitLimit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDirectoryNameSearchProvider>();
                services.AddSingleton<IDirectoryNameSearchProvider>(directory);
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter>(audit);
                services.RemoveAll<IAuditEventSink>();
                services.AddSingleton<IAuditEventSink>(audit);
            });
        });

    private static async Task<HttpClient> ApprovedClientAsync(WebApplicationFactory<Program> factory, string role)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IAccessRepository users = scope.ServiceProvider.GetRequiredService<IAccessRepository>();
        EnsureAccessUserResult pending = await users.EnsureUserAsync(new CorporatePrincipal("demo:team-lead", "test"), true, TimeSpan.Zero, default);
        (await users.DecideRequestAsync(pending.PendingRequest!.Id, AccessRequestStatus.Approved, pending.PendingRequest.Version,
            "system:test-seed", [role], "Synthetic approval", default)).Disposition.Should().Be(AccessMutationDisposition.Applied);
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "team-lead");
        return client;
    }

    private sealed class CountingDirectory : IDirectoryNameSearchProvider
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public Task<DirectoryNameSearchResult> SearchAsync(DirectoryNameQuery query, int limit, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                throw new InvalidOperationException("(displayName=ayse*)");
            }
            return new MockDirectoryNameSearchProvider().SearchAsync(query, limit, cancellationToken);
        }
    }

    private sealed class ControlledAudit(string? failAt = null) : IAuditWriter, IAuditEventSink
    {
        public List<AuditEvent> Events { get; } = [];
        public async Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
        {
            foreach (AuditEvent auditEvent in events)
            {
                await WriteAsync(auditEvent, cancellationToken);
            }
        }
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            if (auditEvent.Action == "Identity.DirectoryNameSearch" + failAt)
            {
                throw new IOException("Synthetic audit failure");
            }
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class QueueAcknowledgementOnly : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
