using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.Sessions;
using SecureOps.Ui.Pages;
using SecureOps.Ui.Services;
using SessionsPage = SecureOps.Ui.Pages.Sessions;

namespace SecureOps.Tests.Unit.Ui;

// #67: the three access screens at 390 px, 200 % and by keyboard. These render the server-side markup
// with synthetic data; what only a browser can show (no horizontal scroll, where focus lands after a page
// change) is proved by tests/browser/access-screens-responsive.cjs.
public sealed class AccessScreensResponsiveRenderTests
{
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Sessions_RenderEachSessionAsRowAndCard_WithTheRevokeActionInBoth_AndAnnounceThePage()
    {
        ISessionApiClient sessions = Substitute.For<ISessionApiClient>();
        sessions.GetActiveAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ActiveApplicationSessionsResponse(1, 50, [Session("demo:one"), Session("demo:two")]));

        string html = await RenderAsync<SessionsPage>(Capabilities.AccessManageUsers, services => services.AddSingleton(sessions));

        // One definition per rendering: the table row (wide container) and the card (narrow container).
        Regex.Matches(html, "Oturumu Sonlandır").Count.Should().Be(4);
        html.Should().Contain("so-session-cards").And.Contain("so-session-table");
        // The result of a page change reaches a screen reader, and the list heading can receive focus.
        html.Should().Contain("Sayfa 1, 2 oturum listelendi.");
        Regex.IsMatch(html, "<h2[^>]*tabindex=\"-1\"[^>]*>").Should().BeTrue();
    }

    [Fact]
    public async Task Users_UsesTheServerPageSizeForThePageCount_AndKeepsThePagerRendered()
    {
        IAccessAdminApiClient admin = Substitute.For<IAccessAdminApiClient>();
        admin.RolesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<AccessRoleDefinition>());
        admin.PageUsersAsync(Arg.Any<AccessPageQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AccessPage<AccessUserResponse>([User()], 30, 1, 10));

        string html = await RenderAsync<AccessUsers>(Capabilities.AccessManageUsers, services => services.AddSingleton(admin));

        // 30 users at the server's 10 per page is three pages; the page used to divide by a literal 25.
        html.Should().Contain("1 / 3");
        html.Should().Contain("aria-label=\"Kullanıcı sayfaları\"").And.Contain("aria-busy=\"false\"");
        html.Should().Contain("tabindex=\"-1\"");
    }

    [Fact]
    public async Task Users_EmptyResult_IsAnEmptyState_NotAFailure()
    {
        IAccessAdminApiClient admin = Substitute.For<IAccessAdminApiClient>();
        admin.RolesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<AccessRoleDefinition>());
        admin.PageUsersAsync(Arg.Any<AccessPageQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AccessPage<AccessUserResponse>([], 0, 1, 25));

        string html = await RenderAsync<AccessUsers>(Capabilities.AccessManageUsers, services => services.AddSingleton(admin));

        html.Should().Contain("so-empty").And.Contain("Eşleşen kullanıcı yok").And.NotContain("Servis şu anda yanıt veremiyor");
    }

    [Fact]
    public async Task Requests_TabsAreARovingGroupThatStaysFocusableWhileBusy_AndThePageIsAnnounced()
    {
        IAccessAdminApiClient admin = Substitute.For<IAccessAdminApiClient>();
        admin.PageRequestsAsync(Arg.Any<AccessPageQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AccessPage<AccessRequestResponse>([Request()], 1, 1, 25));

        string html = await RenderAsync<AccessRequests>(Capabilities.AccessApproveRequests, services => services.AddSingleton(admin));

        // Selected tab is the one tab stop; the others are reached with the arrow keys.
        Regex.Matches(html, "role=\"tab\"[^>]*tabindex=\"0\"").Count.Should().Be(1);
        Regex.Matches(html, "role=\"tab\"[^>]*tabindex=\"-1\"").Count.Should().Be(2);
        // Blazor writes a bool as an empty attribute; the selected tab must say "true" to a screen reader.
        html.Should().Contain("aria-selected=\"true\"").And.Contain("aria-selected=\"false\"");
        // A disabled button under the keyboard user's focus drops focus to <body>.
        Regex.IsMatch(html, "role=\"tab\"[^>]*\\sdisabled").Should().BeFalse();
        html.Should().Contain("Sayfa 1 / 1, 1 talep listelendi.");
        Regex.IsMatch(html, "<h2[^>]*id=\"request-list-title\"[^>]*tabindex=\"-1\"|<h2[^>]*tabindex=\"-1\"[^>]*id=\"request-list-title\"").Should().BeTrue();
    }

    private static ApplicationSessionResponse Session(string principal) => new(
        Guid.NewGuid(), Guid.NewGuid(), _now.AddHours(-1), _now.AddMinutes(-2), _now.AddHours(11), "SecureOpsDemo", 2,
        principal, principal, $"Demo {principal}", "demo-api-bridge");

    private static AccessUserResponse User() => new(
        Guid.NewGuid(), "demo:user", new AccessIdentityProfileResponse("Demo Kullanıcı", "demo.user", "demo.user@contoso.example", null, null),
        "Approved", [], [], null, [], 1, "SecureOpsDemo", _now.AddDays(-3), _now.AddHours(-1), null);

    private static AccessRequestResponse Request() => new(
        Guid.NewGuid(), Guid.NewGuid(), "demo:requester", "Pending", _now.AddHours(-2), null, null);

    private static async Task<string> RenderAsync<TComponent>(string capability, Action<IServiceCollection> configure)
        where TComponent : IComponent
    {
        CurrentAccessResponse current = new(Guid.NewGuid(), "Approved", ["Admin"], [capability], null, "test",
            new SessionPolicyResponse(15, 8, true, true, "Lax", true, "Synthetic"));
        ICurrentAccessProvider access = Substitute.For<ICurrentAccessProvider>();
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccessSnapshot(current, null, DateTimeOffset.UnixEpoch));

        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(access);
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new SyntheticNavigation());
        registrations.AddSupplyValueFromQueryProvider();
        configure(registrations);

        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using HtmlRenderer renderer = new(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<TComponent>()).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("http://localhost/", "http://localhost/access");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }
}
