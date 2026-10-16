using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// TH-13: MudBlazor 6 <c>MudSelect</c> renders no combobox/listbox/option roles and a <c>label[for]</c> that names nothing, so
/// <c>wwwroot/js/so-select-a11y.js</c> adds them for every select. A script cannot run here, so these tests pin the two sides it
/// depends on: the markup MudSelect really renders (a MudBlazor upgrade that changes it fails here, not in front of a screen
/// reader) and the script's contract. The behaviour itself is exercised in a browser by <c>tests/browser/select-a11y.cjs</c>.
/// </summary>
public sealed class SelectAccessibilityTests
{
    [Fact]
    public async Task MudSelect_RendersTheMarkupTheBridgeAnnotates()
    {
        string html = await RenderSelectAsync(new() { ["Label"] = "İnceleyici" });

        html.Should().MatchRegex("class=\"mud-input-control(?: [^\"]*)? mud-select\"", "the bridge walks .mud-input-control.mud-select");
        html.Should().MatchRegex("<label class=\"mud-input-label[^\"]*\"[^>]*>İnceleyici</label>", "the visible label is the accessible name");
        // Before items register MudSelect renders the read-only text input and a hidden value div; once they have, the input becomes
        // type=hidden and the div takes tabindex=0. The bridge handles both, so only the parts they share are pinned.
        html.Should().MatchRegex("<input class=\"mud-input-slot[^\"]*mud-select-input\"", "the select input is where page attributes land");
        html.Should().MatchRegex("<div class=\"mud-input-slot[^\"]*mud-select-input\"", "the value div is the other keyboard target shape");
        html.Should().MatchRegex("id=\"popover-[0-9a-f-]{36}\"", "the popover placeholder shares its GUID with popovercontent-<guid>");
        // MudBlazor 9 already puts role=combobox, aria-haspopup, aria-expanded and aria-label on the select's inputs; the bridge keeps
        // exactly one of them (the keyboard target) and adds the listbox/option semantics, which the closed list does not render.
        html.Should().Contain("role=\"combobox\"", "MudBlazor 9 emits the combobox role itself");
        html.Should().NotContain("role=\"option\"");
    }

    [Fact]
    public async Task MudSelect_PassesAnAriaLabelToItsInput_WhichTheBridgeCopiesToTheCombobox()
    {
        string html = await RenderSelectAsync(new() { ["Label"] = "Kategori", ["UserAttributes"] = new Dictionary<string, object> { ["aria-label"] = "Kategori" } });

        html.Should().MatchRegex("<input[^>]*aria-label=\"Kategori\"");
    }

    [Fact]
    public void Bridge_AddsTheComboboxListboxAndOptionSemantics_WithoutTakingOverKeys()
    {
        string script = Source("wwwroot", "js", "so-select-a11y.js");

        script.Should().Contain("'role', 'combobox'").And.Contain("'role', 'listbox'").And.Contain("'role', 'option'");
        script.Should().Contain("aria-expanded").And.Contain("aria-controls").And.Contain("aria-activedescendant")
            .And.Contain("aria-selected").And.Contain("aria-labelledby").And.Contain("aria-haspopup");
        script.Should().NotContain("keydown", "MudBlazor owns Enter, Space, arrows and Escape");
        script.Should().NotContain("preventDefault").And.NotContain("addEventListener");
        script.Should().NotContain("'role', 'button'", "a select trigger is a combobox, not a button");
    }

    [Fact]
    public void Host_LoadsTheBridge_AndNoPageSpecificSelectPatchRemains()
    {
        string host = Source("Pages", "_Host.cshtml");

        host.Should().Contain("<script src=\"~/js/so-select-a11y.js\" asp-append-version=\"true\"></script>");
        host.Should().NotContain("setAttribute('role', 'button')", "select roles live in one place");
        host.Should().NotContain("div.mud-input-slot[tabindex=\"0\"]");
    }

    [Fact]
    public void EveryMudSelect_HasAVisibleLabelOrAnAriaLabel()
    {
        // The bridge names a select from its label or aria-label; a select with neither would stay anonymous.
        string uiRoot = UiRoot();
        List<string> unnamed = [];
        foreach (string file in Directory.EnumerateFiles(uiRoot, "*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match tag in Regex.Matches(File.ReadAllText(file), @"<MudSelect\b(?:[^>]|(?<==)>)*>", RegexOptions.Singleline))
            {
                if (!Regex.IsMatch(tag.Value, @"\b(Label|aria-label)\s*="))
                {
                    unnamed.Add(Path.GetRelativePath(uiRoot, file) + ": " + Regex.Replace(tag.Value, @"\s+", " "));
                }
            }
        }

        unnamed.Should().BeEmpty();
    }

    [Fact]
    public void AssignmentDialog_RendersTheReviewerSelectOnlyAfterTheOptionsHaveLoaded()
    {
        // MudSelect resolves its text when its items register. Drawn before the reviewer search returned, a select whose value was
        // the current reviewer showed that reviewer's raw id in a text box instead of the name.
        string dialog = Source("Shared", "Components", "InUseAssignmentDialog.razor");

        dialog.Should().Contain("@if (_loaded)");
        dialog.Should().MatchRegex(@"await SearchAsync\(""""\);\s*_loaded = true;");
    }

    private static async Task<string> RenderSelectAsync(Dictionary<string, object?> parameters)
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new StaticNavigation());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        parameters["Value"] = "a";
        parameters["ChildContent"] = (RenderFragment)(builder =>
        {
            foreach ((string value, string text) in new[] { ("", "Atanmamış"), ("a", "Birinci"), ("b", "İkinci") })
            {
                builder.OpenComponent<MudSelectItem<string>>(0);
                builder.AddAttribute(1, "Value", value);
                builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner => inner.AddContent(0, text)));
                builder.CloseComponent();
            }
        });
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MudSelect<string>>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/");
    }

    private static string Source(params string[] path) => File.ReadAllText(Path.Combine([UiRoot(), .. path]));

    private static string UiRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), "src", "SecureOps.Ui");
    }
}
