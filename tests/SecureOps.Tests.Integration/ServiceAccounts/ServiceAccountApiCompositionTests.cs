using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Api.Security;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Domain.Access;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// The module through the application's own API composition (<c>Program</c>): real authentication selection, the
/// platform capability authorization handler, the real access service and the module registration. The Test-environment
/// identity bridge only authenticates two fixed identities; module rights come solely from access role bundles. Protected
/// Admin has all current module capabilities. Allowed data journeys still need an enabled provider and explicit scope, with Integrated Security
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
        using HttpClient anonymous = factory.CreateApiClient();
        foreach (string route in _moduleReads)
        {
            (await anonymous.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, route);
        }
    }

    [Fact]
    public async Task AdminCapabilities_DoNotActivateDisabledModule_LeadRemainsForbidden()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        await ApproveAsync(factory, "demo:platform-admin", "Admin");
        await ApproveAsync(factory, "demo:team-lead", "Lead");
        foreach (string actor in new[] { DemoApiAuthentication.PlatformAdminActor, DemoApiAuthentication.TeamLeadActor })
        {
            using HttpClient client = factory.CreateApiClient();
            client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
            if (actor == DemoApiAuthentication.PlatformAdminActor)
            {
                (await client.GetAsync("/api/v1/access/users")).StatusCode.Should().Be(HttpStatusCode.OK, "the identity is approved and authorized for platform administration");
            }

            foreach (string route in _moduleReads)
            {
                HttpStatusCode expected = actor == DemoApiAuthentication.PlatformAdminActor
                    ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden;
                (await client.GetAsync(route)).StatusCode.Should().Be(expected, $"{actor} {route}");
            }

            (await client.PostAsync("/api/v1/service-accounts/scope-grants", JsonContent.Create(new { corporateIdentity = "x", scopeKind = "All", reason = "x" })))
                .StatusCode.Should().Be(actor == DemoApiAuthentication.PlatformAdminActor ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
                    $"{actor}: page access does not activate the module");
        }
    }

    [Fact]
    public async Task DirectoryNameSearch_NeedsTheModuleAndIdentityLookup_AndIsRateLimited()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        await ApproveAsync(factory, "demo:platform-admin", "Admin");
        await ApproveAsync(factory, "demo:team-lead", "Lead");
        const string route = "/api/v1/service-accounts/directory/name-search";
        using (HttpClient anonymous = factory.CreateApiClient())
        {
            (await anonymous.PostAsync(route, JsonContent.Create(new { query = "ayşe" }))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        foreach (string actor in new[] { DemoApiAuthentication.PlatformAdminActor, DemoApiAuthentication.TeamLeadActor })
        {
            using HttpClient client = factory.CreateApiClient();
            client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
            (await client.PostAsync(route, JsonContent.Create(new { query = "ayşe" }))).StatusCode
                .Should().Be(actor == DemoApiAuthentication.PlatformAdminActor ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
                    $"{actor}: module search still requires View and activation");
        }

        RouteEndpoint endpoint = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "api/v1/service-accounts/directory/name-search");
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Should()
            .Contain([ServiceAccountPolicies.View, SecureOps.Shared.Auth.Policies.CanIdentityLookup]);
        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()!.PolicyName
            .Should().Be(SecureOps.Api.Security.ApiRateLimits.IdentityLookup);
    }

    [Fact]
    public async Task UsageScanRoutes_NeedTheWorkCapability_AndBoundTheUpload()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using (HttpClient anonymous = factory.CreateApiClient())
        {
            using MultipartFormDataContent form = [];
            form.Add(new ByteArrayContent("{}"u8.ToArray()), "file", "scan.json");
            (await anonymous.PostAsync($"/api/v1/service-accounts/accounts/{Guid.NewGuid()}/usage-scans", form)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized);
            (await anonymous.PostAsync("/api/v1/service-accounts/usage-scans", form)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        RouteEndpoint[] endpoints = [.. factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("usage-scan", StringComparison.Ordinal) == true)];
        static string[] Methods(RouteEndpoint e) => [.. e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods];
        RouteEndpoint[] writes = [.. endpoints.Where(e => !Methods(e).Contains("GET"))];
        RouteEndpoint[] reads = [.. endpoints.Where(e => Methods(e).Contains("GET"))];
        writes.Select(e => e.RoutePattern.RawText).Should().BeEquivalentTo(
        [
            "api/v1/service-accounts/accounts/{id:guid}/usage-scans",
            "api/v1/service-accounts/accounts/{id:guid}/usage-scan-items/{itemId:guid}/usage",
            "api/v1/service-accounts/accounts/{id:guid}/usage-scan-items/{itemId:guid}/dismiss",
            "api/v1/service-accounts/usage-scans"
        ], "evidence is attached (to one account, or to several accounts in one upload) and decided per account; there is no scan start route");
        foreach (RouteEndpoint endpoint in writes)
        {
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Should().Contain(ServiceAccountPolicies.Work);
            Methods(endpoint).Should().Equal("POST");
        }

        // Read-only paging and comparison of what the account detail already shows (same scope, searched name only); never the stored file.
        reads.Select(e => e.RoutePattern.RawText).Should().BeEquivalentTo(
        [
            "api/v1/service-accounts/accounts/{id:guid}/usage-scans",
            "api/v1/service-accounts/accounts/{id:guid}/usage-scans/{linkId:guid}/items",
            "api/v1/service-accounts/accounts/{id:guid}/usage-scans/{linkId:guid}/diff"
        ], "scans are paged and compared on the account (G-33); there is no download route");
        foreach (RouteEndpoint endpoint in reads)
        {
            Methods(endpoint).Should().Equal("GET");
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Should().Contain(ServiceAccountPolicies.View).And.NotContain(ServiceAccountPolicies.Work);
        }

        writes.Where(e => e.RoutePattern.RawText!.EndsWith("usage-scans", StringComparison.Ordinal)).Select(e =>
            e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()!.MaxRequestBodySize).Should().Equal(5L * 1024 * 1024, 5L * 1024 * 1024);
    }

    [Fact]
    public void ChangePlanRoutes_PoliciesAndMethods()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient _ = factory.CreateApiClient();
        RouteEndpoint[] endpoints = [.. factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/v1/service-accounts/change-plans", StringComparison.Ordinal) == true)];
        static string Method(RouteEndpoint e) => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Single();
        static string[] Policies(RouteEndpoint e) => [.. e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>()];

        // T6: every route reads its resource under the plan route; writes are POST/PATCH only; there is no DELETE and no
        // route that runs anything on a server. Cancel is View at the route because the planner (Work) or a verifier
        // (Verify) may cancel; the service decides which applies.
        endpoints.Select(e => (Method(e), e.RoutePattern.RawText!)).Should().BeEquivalentTo(new[]
        {
            ("GET", "api/v1/service-accounts/change-plans"),
            ("GET", "api/v1/service-accounts/change-plans/{id:guid}"),
            ("GET", "api/v1/service-accounts/change-plans/{id:guid}/items"),
            ("POST", "api/v1/service-accounts/change-plans"),
            ("PATCH", "api/v1/service-accounts/change-plans/{id:guid}"),
            ("POST", "api/v1/service-accounts/change-plans/{id:guid}/preview"),
            ("POST", "api/v1/service-accounts/change-plans/{id:guid}/approve"),
            ("POST", "api/v1/service-accounts/change-plans/{id:guid}/cancel")
        });
        foreach (RouteEndpoint endpoint in endpoints)
        {
            string route = endpoint.RoutePattern.RawText!;
            string[] policies = Policies(endpoint);
            policies.Should().Contain(ServiceAccountPolicies.View, route);
            string[] expected = Method(endpoint) == "GET" || route.EndsWith("/cancel", StringComparison.Ordinal) ? []
                : route.EndsWith("/approve", StringComparison.Ordinal) ? [ServiceAccountPolicies.Verify] : [ServiceAccountPolicies.Work];
            policies.Except([ServiceAccountPolicies.View]).Should().BeEquivalentTo(expected, route);
        }
    }

    [Fact]
    public void ChangePlanRefusals_MapToStableStatuses()
    {
        ControllerBase controller = new Probe { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        foreach ((string code, string? field, int status) in new[]
        {
            (SaErrors.ChangePlanState, (string?)"previewStale", StatusCodes.Status409Conflict),
            (SaErrors.ChangePlanState, "planNotPreviewed", StatusCodes.Status409Conflict),
            (SaErrors.Forbidden, "approverIsPlanner", StatusCodes.Status403Forbidden),
            (SaErrors.Forbidden, "approverChangedPlan", StatusCodes.Status403Forbidden),
            (SaErrors.ChangePlanAccountsRefused, "accounts", StatusCodes.Status400BadRequest),
            (SaErrors.ChangePlansNotInstalled, null, StatusCodes.Status503ServiceUnavailable)
        })
        {
            ObjectResult result = ServiceAccountReplies.Reply(controller, SaResult<ChangePlanView>.Fail(code, field)).Result.Should().BeOfType<ObjectResult>().Subject;
            result.StatusCode.Should().Be(status, code + " " + field);
            ProblemDetails problem = result.Value.Should().BeAssignableTo<ProblemDetails>().Subject;
            problem.Extensions["code"].Should().Be(code);
            if (field is not null)
            {
                problem.Extensions["field"].Should().Be(field);
            }
        }
    }

    [Fact]
    public void SecondScanDecision_IsA409Conflict_NamingTheRule()
    {
        ControllerBase controller = new Probe { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        ObjectResult result = ServiceAccountReplies.Reply(controller, SaResult<AccountDetail>.Fail(SaErrors.AlreadyDecided, "alreadyDecided")).Result
            .Should().BeOfType<ObjectResult>().Subject;

        result.StatusCode.Should().Be(StatusCodes.Status409Conflict, "the item was decided before; the request itself is valid");
        ProblemDetails problem = result.Value.Should().BeAssignableTo<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(SaErrors.AlreadyDecided);
        problem.Extensions["field"].Should().Be("alreadyDecided");
    }

    [Fact]
    public void EveryModuleEndpoint_RequiresAModuleCapabilityPolicy()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient _ = factory.CreateApiClient();
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

    [Fact]
    public async Task UsageScanUploads_OpenApiDescribesTheFormAsSent()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(swagger: true);
        using HttpClient client = factory.CreateClient();
        JsonNode document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;

        foreach ((string path, string[] required, string[] optional) in new[]
        {
            ("/api/v1/service-accounts/accounts/{id}/usage-scans", new[] { "file", "runStatement" }, new[] { "requestId" }),
            ("/api/v1/service-accounts/usage-scans", ["file", "runStatement", "accountIds"], Array.Empty<string>())
        })
        {
            JsonNode schema = document["paths"]![path]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;
            JsonObject properties = schema["properties"]!.AsObject();
            properties.Select(p => p.Key).Should().BeEquivalentTo([.. required, .. optional], $"{path}: the form fields, not IFormFile's own properties");
            schema["required"]!.AsArray().Select(r => (string?)r).Should().BeEquivalentTo(required, path);
            ((string?)properties["file"]!["type"], (string?)properties["file"]!["format"]).Should().Be(("string", "binary"), path);
            ((int?)properties["runStatement"]!["minLength"], (int?)properties["runStatement"]!["maxLength"]).Should().Be((5, 400), path);
        }

        JsonNode ids = document["paths"]!["/api/v1/service-accounts/usage-scans"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!
            ["properties"]!["accountIds"]!;
        ((string?)ids["type"], (int?)ids["minItems"], (int?)ids["maxItems"], (bool?)ids["uniqueItems"], (string?)ids["items"]!["format"])
            .Should().Be(("array", 1, UsageScanBatch.MaxAccounts, true, "uuid"));
    }

    private static WebApplicationFactory<Program> CreateFactory(bool swagger = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("Swagger:Enabled", swagger ? "true" : "false");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "false");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("ServiceAccounts:Provider", "Disabled");
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

    private sealed class Probe : ControllerBase;
}
