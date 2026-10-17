using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Ui.Pages;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

// #79: the access action dialog and the user detail page. The server-side markup is checked here with synthetic data;
// what only a browser shows (focus entering and staying in the dialog, Esc, focus return, ring width, no horizontal
// scroll at 390 px) is proved by tests/browser/access-dialog-detail.cjs.
public sealed class AccessActionDialogRenderTests
{
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    private static readonly AccessRoleDefinition[] _roles =
    [
        new("Lead", "Takım Lideri", "Vardiya sorumlusu", 1, false, ["Identity.Lookup"]),
        new("Operator", "Operatör", "Vardiya operatörü", 1, false, ["Identity.Lookup", "InUse.View"]),
    ];

    [Fact]
    public async Task Dialog_RoleOptionsAreNativeCheckboxesNamedByTheirLabel_AndTheTitleIsAHeadingInsideTheFocusTrap()
    {
        string html = await RenderDialogAsync(AccessActionDialog.AccessAction.ReplaceRoles);

        // MudCheckBox renders an unnamed input and focuses a <span>; a native input inside its label has a name, a role and a focus ring.
        html.Should().NotContain("mud-checkbox");
        Regex.Matches(html, "<label class=\"so-role-option\"[^>]*>\\s*(<!--.*?-->)*\\s*<input type=\"checkbox\"").Count.Should().Be(2);
        html.Should().Contain("Takım Lideri").And.Contain("Operatör");
        // MudBlazor 6.16 puts no name on the dialog, so the landing block inside the focus trap carries the title for a screen reader.
        Regex.IsMatch(html, "<div class=\"so-dialog-subject\"[^>]*tabindex=\"-1\"[^>]*>\\s*(<!--.*?-->\\s*)*<span class=\"sr-only\"[^>]*>(<!--.*?-->)*Rolleri değiştir\\.").Should().BeTrue();
        html.Should().Contain("<h2 class=\"so-dialog-heading\"");
        html.Should().Contain("so-dialog-buttons");
    }

    [Fact]
    public async Task Dialog_DisableAsksForAReasonInsideAWrapperThatCarriesTheFocusRing()
    {
        string html = await RenderDialogAsync(AccessActionDialog.AccessAction.Disable);

        html.Should().Contain("so-dialog-reason-box").And.Contain("access-decision-reason");
        html.Should().NotContain("so-role-picker");
    }

    [Fact]
    public async Task Detail_TriggerButtonsHaveStableIds_AndNoOutcomeBlockBeforeAnyAction()
    {
        AccessUserResponse user = new(
            Guid.NewGuid(), "demo:user", new AccessIdentityProfileResponse("Demo Kullanıcı", "demo.user", "demo.user@contoso.example", null, null),
            "Approved", ["Lead"], ["Identity.Lookup"], null, [], 1, "SecureOpsDemo", _now.AddDays(-3), _now.AddHours(-1), null);
        IAccessAdminApiClient admin = Substitute.For<IAccessAdminApiClient>();
        admin.RolesAsync(Arg.Any<CancellationToken>()).Returns(_roles);
        admin.GetUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(user);

        string html = await RenderAsync<AccessUserDetail>(
            services => services.AddSingleton(admin),
            new Dictionary<string, object?> { [nameof(AccessUserDetail.UserId)] = user.UserId },
            Capabilities.AccessManageUsers, Capabilities.AccessAssignRoles);

        // The page returns focus to these after a cancelled dialog (MudBlazor 6.16 does not), so the ids are part of the contract.
        html.Should().Contain("id=\"access-user-replace-roles\"").And.Contain("id=\"access-user-disable\"");
        html.Should().NotContain("user-outcome");
    }

    private static Task<string> RenderDialogAsync(AccessActionDialog.AccessAction action)
    {
        IAccessAdminApiClient admin = Substitute.For<IAccessAdminApiClient>();
        admin.RolesAsync(Arg.Any<CancellationToken>()).Returns(_roles);
        return RenderAsync<DialogHost>(
            services => services.AddSingleton(admin),
            new Dictionary<string, object?> { [nameof(DialogHost.Action)] = action });
    }

    private static async Task<string> RenderAsync<TComponent>(Action<IServiceCollection> configure, Dictionary<string, object?> parameters, params string[] capabilities)
        where TComponent : IComponent
    {
        CurrentAccessResponse current = new(Guid.NewGuid(), "Approved", ["Admin"], capabilities, null, "test",
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
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    // The dialog needs the MudDialogInstance MudBlazor's dialog service creates; this host renders one around the dialog.
    private sealed class DialogHost : ComponentBase
    {
        [Parameter] public AccessActionDialog.AccessAction Action { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudDialogInstance>(0);
            builder.AddAttribute(1, nameof(MudDialogInstance.Options), new DialogOptions());
            builder.AddAttribute(2, nameof(MudDialogInstance.Title), string.Empty);
            builder.AddAttribute(3, nameof(MudDialogInstance.Id), Guid.NewGuid());
            builder.AddAttribute(4, nameof(MudDialogInstance.Content), (RenderFragment)(inner =>
            {
                inner.OpenComponent<AccessActionDialog>(0);
                inner.AddAttribute(1, nameof(AccessActionDialog.Action), Action);
                inner.AddAttribute(2, nameof(AccessActionDialog.SubjectLabel), "Demo Kullanıcı");
                inner.AddAttribute(3, nameof(AccessActionDialog.InitialRoles), (IReadOnlyList<string>)["Lead"]);
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("http://localhost/", "http://localhost/access");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }
}
