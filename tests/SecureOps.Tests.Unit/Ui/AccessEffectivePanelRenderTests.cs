using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

public sealed class AccessEffectivePanelRenderTests
{
    private static AccessEffectiveResponse Effective(string status, bool granted) => new(
        Guid.NewGuid(),
        status,
        ["Lead"],
        granted ? 1 : 0,
        [
            new("In Use",
            [
                new("InUse.View", "Kayıtları gör", "Okur.", granted, granted ? ["Lead"] : []),
                new("InUse.Assign", "İnceleyici ata", "Atar.", false, [])
            ])
        ]);

    [Fact]
    public async Task Approved_StatesGrantedAndDeniedInWordsAndNamesTheGrantingRole()
    {
        string html = await RenderAsync(Effective("Approved", granted: true), grantedOnly: false);

        html.Should().Contain("işlem, ").And.Contain("modülde yapılabilir.");
        html.Should().Contain("Yapabilir").And.Contain("Yapamaz");
        html.Should().Contain("Rol: ");
        html.Should().Contain("aria-label=\"In Use: 1 / 2 işlem\"");
        html.Should().Contain("1 / 2 işlem");
        html.Should().NotMatchRegex("<p[^>]*eff-roles");
    }

    [Fact]
    public async Task NotApproved_SaysNothingCanBeDoneAndGrantsNothing()
    {
        string html = await RenderAsync(Effective("Pending", granted: false), grantedOnly: false);

        html.Should().Contain("Erişim onaylı olmadığı için şu an hiçbir işlem yapılamaz.");
        html.Should().NotContain(">Yapabilir<");
        html.Should().NotContain("Rol: ");
    }

    [Fact]
    public async Task GrantedOnly_HidesNotGrantedActions()
    {
        string html = await RenderAsync(Effective("Approved", granted: true), grantedOnly: true);

        html.Should().Contain("Kayıtları gör").And.NotContain("İnceleyici ata");
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("https://wasas.test/", "https://wasas.test/access/me");
    }

    private static async Task<string> RenderAsync(AccessEffectiveResponse effective, bool grantedOnly)
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddSingleton(Substitute.For<IJSRuntime>()).AddSingleton<NavigationManager, SyntheticNavigation>().AddMudServices().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Dictionary<string, object?> parameters = new()
        {
            [nameof(AccessEffectivePanel.Effective)] = effective,
            [nameof(AccessEffectivePanel.StartWithGrantedOnly)] = grantedOnly
        };
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<AccessEffectivePanel>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }
}
