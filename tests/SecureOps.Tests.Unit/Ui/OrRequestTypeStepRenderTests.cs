using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

public sealed class OrRequestTypeStepRenderTests
{
    private static readonly RequestTypeSuggestionResponse _hint =
        new(OperationalRecordClassification.SoftwareInstallation, ["kurulumu"], RequestTypeSuggester.RuleVersion);

    [Fact]
    public async Task Suggestion_ShowsTypeAndMatchedWords_WithOneClickConfirmAndNoSelect()
    {
        string html = await RenderAsync(_hint, declared: null);

        html.Should().Contain("Önerilen tür").And.Contain("Uygulama Kurulumu").And.Contain("“kurulumu”");
        html.Should().Contain("Doğru, inceleme taslağını hazırla").And.Contain("Türü değiştir");
        html.Should().Contain("uygunluk veya yayımlama onayı vermez");
        html.Should().NotContain("id=\"sdm-request-type\"");
    }

    [Fact]
    public async Task NoSuggestion_SaysSoAndAsksTheOperatorToChoose()
    {
        string html = await RenderAsync(null, declared: null);

        html.Should().Contain("Metinden tür önerilemedi");
        html.Should().MatchRegex("<label for=\"sdm-request-type\"[^>]*>Talep türü</label>");
        html.Should().Contain("Taslak için önce bir tür seçin.");
        html.Should().NotContain("operatör beyanı");
    }

    [Fact]
    public async Task RetirementSelected_StatesItIsTrackingOnly()
    {
        string html = await RenderAsync(null, OperationalRecordClassification.ServerRetirement);

        html.Should().Contain("yalnız talep takibidir");
        html.Should().Contain("SDM takip gerekçesi:");
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("https://wasas.test/", "https://wasas.test/operational-records");
    }

    private static async Task<string> RenderAsync(RequestTypeSuggestionResponse? hint, OperationalRecordClassification? declared)
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddSingleton(Substitute.For<IJSRuntime>()).AddSingleton<NavigationManager, SyntheticNavigation>().AddMudServices().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Dictionary<string, object?> parameters = new()
        {
            [nameof(OrRequestTypeStep.Hint)] = hint,
            [nameof(OrRequestTypeStep.DeclaredType)] = declared
        };
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<OrRequestTypeStep>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }
}
