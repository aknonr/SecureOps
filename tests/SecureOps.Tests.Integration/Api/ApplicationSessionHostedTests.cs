using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Sessions;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Shared.Contracts.Sessions;
using SecureOps.Ui.Services;

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
        current.Principal.Should().Be("demo:platform-admin");
        current.NormalizedPrincipal.Should().Be("demo:platform-admin");
        current.AuthenticationProvider.Should().Be("demo-api-bridge");
        current.IsCurrent.Should().BeTrue();
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
    public async Task PreservedBrowserHandle_ReusesOneSession_WhileSeparateBrowserGetsAnother()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient browser = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient privateBrowser = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        (ApplicationSessionResponse first, string cookie) = await StartAsync(browser);

        using HttpRequestMessage refresh = Request(HttpMethod.Get, "/api/v1/sessions/current", cookie);
        ApplicationSessionResponse refreshed = (await (await browser.SendAsync(refresh)).Content
            .ReadFromJsonAsync<ApplicationSessionResponse>())!;
        using HttpRequestMessage navigation = Request(HttpMethod.Get, "/api/v1/access/me", cookie);
        HttpResponseMessage navigationResponse = await browser.SendAsync(navigation);
        using HttpRequestMessage anotherTab = Request(HttpMethod.Get, "/api/v1/sessions/current", cookie);
        ApplicationSessionResponse tab = (await (await browser.SendAsync(anotherTab)).Content
            .ReadFromJsonAsync<ApplicationSessionResponse>())!;
        (ApplicationSessionResponse separate, _) = await StartAsync(privateBrowser);

        refreshed.SessionId.Should().Be(first.SessionId);
        tab.SessionId.Should().Be(first.SessionId);
        navigationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        separate.SessionId.Should().NotBe(first.SessionId);
    }

    [Fact]
    public async Task OneLogicalUiSession_TwentyApiOperations_CreateExactlyOneActiveSession()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(simulation: true);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        ApiSessionStore store = new(cache);
        ApiSessionCookieHandler sessionHandler = new(store, NullLogger<ApiSessionCookieHandler>.Instance)
        {
            InnerHandler = factory.Server.CreateHandler()
        };
        using HttpClient browser = new(sessionHandler) { BaseAddress = new Uri("http://localhost/") };
        browser.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);
        browser.DefaultRequestHeaders.Add(ApiSessionHeaders.BrowserSession, "one-logical-browser-session");

        HttpResponseMessage first = await browser.GetAsync("/api/v1/operational-records");
        first.EnsureSuccessStatusCode();
        OperationalRecordResponse record = (await first.Content.ReadFromJsonAsync<OperationalRecordResponse[]>())!
            .Single(item => item.OrCode == "SIM-OR-100");

        HttpResponseMessage preview = await browser.PostAsync(
            $"/api/v1/operational-records/{record.Id}/jira-preview",
            null);
        preview.EnsureSuccessStatusCode();

        for (int operation = 0; operation < 18; operation++)
        {
            string path = (operation % 3) switch
            {
                0 => "/api/v1/access/me",
                1 => "/api/v1/sessions/current",
                _ => "/api/v1/operational-records"
            };
            (await browser.GetAsync(path)).EnsureSuccessStatusCode();
        }

        IApplicationSessionRepository sessions = factory.Services.GetRequiredService<IApplicationSessionRepository>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<SecureOps.Domain.Sessions.ApplicationSession> active = await sessions.ListActiveAsync(
            now,
            now.AddMinutes(-30),
            0,
            100,
            CancellationToken.None);
        InMemoryAuditWriter audit = factory.Services.GetRequiredService<InMemoryAuditWriter>();

        active.Should().ContainSingle();
        active[0].LastSeenAtUtc.Should().Be(active[0].StartedAtUtc,
            "activity is persisted only after the configured five-minute interval");
        audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionStarted).Should().Be(1);
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
        using HttpRequestMessage relist = Request(HttpMethod.Get, "/api/v1/sessions/active", adminState.AdminCookie);
        ActiveApplicationSessionsResponse afterRevoke = (await (await admin.SendAsync(relist)).Content
            .ReadFromJsonAsync<ActiveApplicationSessionsResponse>())!;

        await AssertProblemAsync(leadListResponse, HttpStatusCode.Forbidden, "AccessDenied");
        adminListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        active.Items.Select(item => item.SessionId).Should().Contain([leadState.LeadSession.SessionId, adminState.AdminSession.SessionId]);
        active.Items.Single(item => item.SessionId == adminState.AdminSession.SessionId).IsCurrent.Should().BeTrue();
        active.Items.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Principal));
        active.Items.Select(item => JsonSerializer.Serialize(item))
            .Should().OnlyContain(json => !json.Contains("\"ip\"", StringComparison.OrdinalIgnoreCase)
                && !json.Contains("\"device\"", StringComparison.OrdinalIgnoreCase)
                && !json.Contains("\"cookie\"", StringComparison.OrdinalIgnoreCase));
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertProblemAsync(replayResponse, HttpStatusCode.Forbidden, "SessionRevoked");
        afterRevoke.Items.Select(item => item.SessionId).Should().NotContain(leadState.LeadSession.SessionId);
    }

    [Fact]
    public async Task DisableUser_EndsExistingSessionsAndBlocksFurtherAccess()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        (ApplicationSessionResponse LeadSession, string LeadCookie) leadState = await StartAsync(lead);
        (ApplicationSessionResponse AdminSession, string AdminCookie) adminState = await StartAsync(admin);

        using HttpRequestMessage usersRequest = Request(HttpMethod.Get, "/api/v1/access/users", adminState.AdminCookie);
        AccessUserResponse[] users = (await (await admin.SendAsync(usersRequest)).Content
            .ReadFromJsonAsync<AccessUserResponse[]>())!;
        AccessUserResponse target = users.Single(user => user.UserId == leadState.LeadSession.UserId);
        using HttpRequestMessage disable = Request(HttpMethod.Post, $"/api/v1/access/users/{target.UserId}/disable", adminState.AdminCookie);
        disable.Content = JsonContent.Create(new DisableAccessRequest("Approved synthetic access-disable test.", target.Version));

        HttpResponseMessage disableResponse = await admin.SendAsync(disable);
        using HttpRequestMessage blocked = Request(HttpMethod.Get, "/api/v1/access/me", leadState.LeadCookie);
        HttpResponseMessage blockedResponse = await lead.SendAsync(blocked);
        using HttpRequestMessage relist = Request(HttpMethod.Get, "/api/v1/sessions/active", adminState.AdminCookie);
        ActiveApplicationSessionsResponse active = (await (await admin.SendAsync(relist)).Content
            .ReadFromJsonAsync<ActiveApplicationSessionsResponse>())!;

        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertProblemAsync(blockedResponse, HttpStatusCode.Forbidden, "AccessDisabled");
        active.Items.Select(item => item.SessionId).Should().NotContain(leadState.LeadSession.SessionId);
        active.Items.Select(item => item.SessionId).Should().Contain(adminState.AdminSession.SessionId);
    }

    [Fact]
    public async Task SelfRevoke_RetainsDeadBridgeUntilExplicitNewAuthentication()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        ApiSessionStore store = new(cache);
        ApiSessionCookieHandler sessionHandler = new(store, NullLogger<ApiSessionCookieHandler>.Instance)
        {
            InnerHandler = factory.Server.CreateHandler()
        };
        using HttpClient browser = new(sessionHandler) { BaseAddress = new Uri("http://localhost/") };
        browser.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", DemoApiAuthentication.PlatformAdminActor);
        browser.DefaultRequestHeaders.Add(ApiSessionHeaders.BrowserSession, "self-revoked-browser");

        ApplicationSessionResponse current = (await (await browser.GetAsync("/api/v1/sessions/current")).Content
            .ReadFromJsonAsync<ApplicationSessionResponse>())!;
        HttpResponseMessage revoke = await browser.PostAsJsonAsync(
            "/api/v1/sessions/revoke",
            new RevokeApplicationSessionRequest(current.SessionId, "Approved synthetic self-revocation."));
        HttpResponseMessage rejected = await browser.GetAsync("/api/v1/access/me");

        IApplicationSessionRepository sessions = factory.Services.GetRequiredService<IApplicationSessionRepository>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<SecureOps.Domain.Sessions.ApplicationSession> active = await sessions.ListActiveAsync(
            now,
            now.AddMinutes(-30),
            0,
            100,
            CancellationToken.None);
        InMemoryAuditWriter audit = factory.Services.GetRequiredService<InMemoryAuditWriter>();

        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertProblemAsync(rejected, HttpStatusCode.Forbidden, "SessionRevoked");
        active.Should().BeEmpty();
        audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionStarted).Should().Be(1);
        audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionRevoked).Should().Be(1);
        store.GetOrCreate("self-revoked-browser").RequiresReauthentication.Should().BeTrue();

        store.GetOrCreate("self-revoked-browser").GetApiCookieHeader(new Uri("http://localhost/")).Should().BeEmpty();
        browser.DefaultRequestHeaders.Remove(ApiSessionHeaders.BrowserSession);
        browser.DefaultRequestHeaders.Add(ApiSessionHeaders.BrowserSession, "explicit-new-authentication");
        ApplicationSessionResponse reauthenticated = (await (await browser.GetAsync("/api/v1/sessions/current")).Content
            .ReadFromJsonAsync<ApplicationSessionResponse>())!;

        reauthenticated.SessionId.Should().NotBe(current.SessionId);
        audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionStarted).Should().Be(2);
    }

    [Fact]
    public async Task AdminRevocation_RepeatedCookieRequestsCannotSilentlyStartAnotherSession()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient browser = Client(factory, DemoApiAuthentication.TeamLeadActor);
        (ApplicationSessionResponse adminSession, string adminCookie) = await StartAsync(admin);
        (ApplicationSessionResponse ended, string cookie) = await StartAsync(browser);
        using HttpRequestMessage revoke = Request(HttpMethod.Post, "/api/v1/sessions/revoke", adminCookie);
        revoke.Content = JsonContent.Create(new RevokeApplicationSessionRequest(ended.SessionId, "Synthetic administrative revocation."));
        (await admin.SendAsync(revoke)).EnsureSuccessStatusCode();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpRequestMessage request = Request(HttpMethod.Get, "/api/v1/sessions/current", cookie);
            using HttpResponseMessage response = await browser.SendAsync(request);
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "SessionRevoked");
            response.Headers.Contains("Set-Cookie").Should().BeFalse();
            response.Headers.GetValues(ApplicationSessionHeaders.ReauthenticationRequired).Should().ContainSingle("required");
        }

        InMemoryAuditWriter audit = factory.Services.GetRequiredService<InMemoryAuditWriter>();
        audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionStarted).Should().Be(2);
        audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionRevoked).Should().Be(1);
        using HttpRequestMessage list = Request(HttpMethod.Get, "/api/v1/sessions/active", adminCookie);
        ActiveApplicationSessionsResponse active = (await (await admin.SendAsync(list)).Content.ReadFromJsonAsync<ActiveApplicationSessionsResponse>())!;
        active.Items.Should().ContainSingle(item => item.SessionId == adminSession.SessionId);
    }

    [Fact]
    public async Task SelfRevocation_RetainsApiHandleAndRepeatedRequestsRemainTerminal()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient browser = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        (ApplicationSessionResponse ended, string cookie) = await StartAsync(browser);
        using HttpRequestMessage revoke = Request(HttpMethod.Post, "/api/v1/sessions/revoke", cookie);
        revoke.Content = JsonContent.Create(new RevokeApplicationSessionRequest(ended.SessionId, "Synthetic self-revocation."));
        using HttpResponseMessage response = await browser.SendAsync(revoke);
        response.EnsureSuccessStatusCode();
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
        response.Headers.GetValues(ApplicationSessionHeaders.ReauthenticationRequired).Should().ContainSingle("required");
        using HttpRequestMessage next = Request(HttpMethod.Get, "/api/v1/sessions/current", cookie);
        await AssertProblemAsync(await browser.SendAsync(next), HttpStatusCode.Forbidden, "SessionRevoked");
        factory.Services.GetRequiredService<InMemoryAuditWriter>().Events.Count(item => item.Action == AuditActions.ApplicationSessionStarted).Should().Be(1);
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

    [Fact]
    public async Task SessionHandle_CannotBeReusedByAnotherAuthenticatedUser()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient admin = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        using HttpClient lead = Client(factory, DemoApiAuthentication.TeamLeadActor);
        (_, string adminCookie) = await StartAsync(admin);
        using HttpRequestMessage crossUser = Request(
            HttpMethod.Get,
            "/api/v1/sessions/current",
            adminCookie);

        HttpResponseMessage response = await lead.SendAsync(crossUser);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "SessionRevoked");
    }

    [Fact]
    public async Task Logout_EndsCurrentSessionClearsHandleAndExcludesItFromActiveList()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient browser = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        (ApplicationSessionResponse ended, string cookie) = await StartAsync(browser);

        using HttpRequestMessage logout = Request(HttpMethod.Post, "/api/v1/access/logout", cookie);
        HttpResponseMessage logoutResponse = await browser.SendAsync(logout);
        using HttpRequestMessage replay = Request(HttpMethod.Get, "/api/v1/sessions/current", cookie);
        HttpResponseMessage replayResponse = await browser.SendAsync(replay);
        using HttpClient otherBrowser = Client(factory, DemoApiAuthentication.PlatformAdminActor);
        (ApplicationSessionResponse current, string currentCookie) = await StartAsync(otherBrowser);
        using HttpRequestMessage list = Request(HttpMethod.Get, "/api/v1/sessions/active", currentCookie);
        ActiveApplicationSessionsResponse active = (await (await otherBrowser.SendAsync(list)).Content
            .ReadFromJsonAsync<ActiveApplicationSessionsResponse>())!;

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        logoutResponse.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.StartsWith("__Host-SecureOps.ApplicationSession=", StringComparison.Ordinal)
            && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        await AssertProblemAsync(replayResponse, HttpStatusCode.Forbidden, "SessionRevoked");
        active.Items.Select(item => item.SessionId).Should().Contain(current.SessionId).And.NotContain(ended.SessionId);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool simulation = false) =>
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
            builder.UseSetting("OperationalRecords:SourceProvider", simulation ? "Simulation" : "Disabled");
            builder.UseSetting("OperationalRecords:RepositoryProvider", "InMemory");
            builder.UseSetting("Jira:Provider", simulation ? "Simulation" : "Disabled");
            builder.UseSetting("RateLimiting:OperationalRecordRefresh:PermitLimit", "100");
            builder.UseSetting("RateLimiting:JiraPreview:PermitLimit", "100");
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
