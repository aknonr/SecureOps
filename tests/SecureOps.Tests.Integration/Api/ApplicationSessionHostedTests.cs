using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Api.Security;
using SecureOps.Shared.Contracts.Sessions;

namespace SecureOps.Tests.Integration.Api;

public sealed class ApplicationSessionHostedTests
{
    [Fact]
    public async Task Current_CreatesSecureBrowserSessionHandleAfterAuthentication()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient anonymous = Client(factory);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);

        HttpResponseMessage anonymousResponse = await anonymous.GetAsync("/api/v1/sessions/current");
        HttpResponseMessage response = await admin.GetAsync("/api/v1/sessions/current");
        ApplicationSessionResponse current = (await response.Content.ReadFromJsonAsync<ApplicationSessionResponse>())!;
        string setCookie = response.Headers.GetValues("Set-Cookie").Single();

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousResponse.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        current.SessionId.Should().NotBeEmpty();
        current.UserId.Should().NotBeEmpty();
        current.AbsoluteExpiresAtUtc.Should().BeAfter(current.StartedAtUtc);
        setCookie.Should().StartWith("__Host-SecureOps.ApplicationSession=")
            .And.Contain("path=/")
            .And.Contain("secure")
            .And.Contain("httponly")
            .And.Contain("samesite=lax")
            .And.NotContain("expires=")
            .And.NotContain("max-age=");
    }

    [Fact]
    public async Task Admin_CanListAndRevokeExactSession_WhileLeadCannotList()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        (ApplicationSessionResponse LeadSession, string LeadCookie) leadState = await StartAsync(lead);
        (ApplicationSessionResponse AdminSession, string AdminCookie) adminState = await StartAsync(admin);

        using HttpRequestMessage leadList = Request(HttpMethod.Get, "/api/v1/sessions/active", leadState.LeadCookie);
        HttpResponseMessage leadListResponse = await lead.SendAsync(leadList);
        using HttpRequestMessage adminList = Request(HttpMethod.Get, "/api/v1/sessions/active?page=1&pageSize=50", adminState.AdminCookie);
        HttpResponseMessage adminListResponse = await admin.SendAsync(adminList);
        ActiveApplicationSessionsResponse active = (await adminListResponse.Content.ReadFromJsonAsync<ActiveApplicationSessionsResponse>())!;
        using HttpRequestMessage revoke = Request(HttpMethod.Post, "/api/v1/sessions/revoke", adminState.AdminCookie);
        revoke.Content = JsonContent.Create(new RevokeApplicationSessionRequest(leadState.LeadSession.SessionId, "Approved synthetic hosted revocation."));
        HttpResponseMessage revokeResponse = await admin.SendAsync(revoke);
        using HttpRequestMessage replay = Request(HttpMethod.Get, "/api/v1/sessions/current", leadState.LeadCookie);
        HttpResponseMessage replayResponse = await lead.SendAsync(replay);

        await AssertProblemAsync(leadListResponse, HttpStatusCode.Forbidden, "AccessDenied");
        adminListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        active.Items.Select(item => item.SessionId).Should().Contain([leadState.LeadSession.SessionId, adminState.AdminSession.SessionId]);
        active.Items.Select(item => JsonSerializer.Serialize(item))
            .Should().OnlyContain(json => !json.Contains("ip", StringComparison.OrdinalIgnoreCase)
                && !json.Contains("device", StringComparison.OrdinalIgnoreCase)
                && !json.Contains("cookie", StringComparison.OrdinalIgnoreCase));
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertProblemAsync(replayResponse, HttpStatusCode.Forbidden, "SessionRevoked");
    }

    [Fact]
    public async Task TamperedClientCookie_IsRejectedAndCannotBecomeAuthenticationProof()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpRequestMessage request = Request(
            HttpMethod.Get,
            "/api/v1/sessions/current",
            "__Host-SecureOps.ApplicationSession=client-supplied-unprotected-value");

        HttpResponseMessage response = await admin.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "SessionRevoked");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DemoAuth:Enabled", "true");
            builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
            builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
            builder.UseSetting("Access:RepositoryProvider", "InMemory");
            builder.UseSetting("SessionSecurity:RepositoryProvider", "InMemory");
            builder.UseSetting("DataProtection:Mode", "Ephemeral");
            builder.UseSetting("Audit:Provider", "InMemory");
            builder.UseSetting("IdentityLookup:Provider", "Mock");
            builder.UseSetting("OperationalRecords:SourceProvider", "Disabled");
            builder.UseSetting("OperationalRecords:RepositoryProvider", "InMemory");
            builder.UseSetting("Jira:Provider", "Disabled");
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? actor = null)
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (actor is not null)
        {
            client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", actor);
        }

        return client;
    }

    private static async Task<(ApplicationSessionResponse Session, string Cookie)> StartAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/v1/sessions/current");
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<ApplicationSessionResponse>())!, response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string cookie)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        problem.Extensions["code"]!.ToString().Should().Be(code);
    }
}
