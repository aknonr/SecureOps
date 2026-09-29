using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// The module through the application's own API composition (<c>Program</c>): real authentication selection, the
/// platform capability authorization handler, the real access service and the module registration. The Test-environment
/// identity bridge only authenticates two fixed identities; module rights come solely from access role bundles, which no
/// platform role contains. Allowed journeys need the SQL access store, which the host only accepts with Integrated Security
/// (Windows runner procedure in docs/service-accounts/WINDOWS-ACCEPTANCE.md). No SQL is touched here.
/// </summary>
public sealed class ServiceAccountApiCompositionTests
{
    private static readonly string[] _moduleReads =
    [
        "/api/v1/service-accounts/me", "/api/v1/service-accounts/work-summary", "/api/v1/service-accounts/accounts",
        "/api/v1/service-accounts/accounts/export", "/api/v1/service-accounts/imports", "/api/v1/service-accounts/reports/weekly?weekStart=2026-09-14",
        "/api/v1/service-accounts/reminders"
    ];

    [Fact]
    public async Task Unauthenticated_IsChallenged_OnEveryModuleRoute()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient anonymous = factory.CreateClient();
        foreach (string route in _moduleReads)
        {
            (await anonymous.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, route);
        }
    }

    [Fact]
    public async Task PlatformRoles_WithoutModuleBundles_AreForbiddenEverywhere_IncludingAdmin()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        await ApproveAsync(factory, "demo:platform-admin", "Admin");
        await ApproveAsync(factory, "demo:team-lead", "Lead");
        foreach (string actor in new[] { DemoApiAuthentication.PlatformAdminActor, DemoApiAuthentication.TeamLeadActor })
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
            if (actor == DemoApiAuthentication.PlatformAdminActor)
            {
                (await client.GetAsync("/api/v1/access/users")).StatusCode.Should().Be(HttpStatusCode.OK, "the identity is approved and authorized for platform administration");
            }

            foreach (string route in _moduleReads)
            {
                (await client.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{actor} {route}");
            }

            (await client.PostAsync("/api/v1/service-accounts/scope-grants", JsonContent.Create(new { corporateIdentity = "x", scopeKind = "All", reason = "x" })))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{actor} cannot create module scope");
        }
    }

    [Fact]
    public void EveryModuleEndpoint_RequiresAModuleCapabilityPolicy()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient _ = factory.CreateClient();
        HashSet<string> modulePolicies = [.. ServiceAccountPolicies.Map.Select(m => m.Policy)];
        RouteEndpoint[] endpoints = [.. factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/v1/service-accounts", StringComparison.OrdinalIgnoreCase) == true)];

        endpoints.Should().HaveCountGreaterThan(40, "the module surface is routed through the normal controller pipeline");
        foreach (RouteEndpoint endpoint in endpoints)
        {
            endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Should().BeEmpty(endpoint.RoutePattern.RawText);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().Intersect(modulePolicies)
                .Should().NotBeEmpty($"{endpoint.DisplayName} must require a ServiceAccounts capability policy");
        }
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "false");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("ServiceAccounts:Provider", "SqlServer");
            // Module enabled with an unreachable server: every request here is decided before any SQL call.
            builder.UseSetting("ConnectionStrings:SecureOpsDb", "Server=sql.invalid;Database=SecureOps;Integrated Security=True;Connect Timeout=5");
        });

    /// <summary>Approves a fixed identity with a platform role (same fixture pattern as the platform's API access tests).</summary>
    private static async Task ApproveAsync(WebApplicationFactory<Program> factory, string identity, string role)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IAccessRepository repository = scope.ServiceProvider.GetRequiredService<IAccessRepository>();
        EnsureAccessUserResult ensured = await repository.EnsureUserAsync(new CorporatePrincipal(identity, "demo-api-bridge"), true, TimeSpan.FromMinutes(5),
            CancellationToken.None);
        (await repository.DecideRequestAsync(ensured.PendingRequest!.Id, AccessRequestStatus.Approved, ensured.PendingRequest.Version, "system:test-seed",
            [role], "Synthetic platform role fixture.", CancellationToken.None)).Disposition.Should().Be(AccessMutationDisposition.Applied);
    }
}
