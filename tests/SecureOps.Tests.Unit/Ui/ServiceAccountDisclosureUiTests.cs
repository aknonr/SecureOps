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
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Keyboard access to the module's collapsible forms (review 2026-10-05). MudBlazor 6.16 renders an expansion panel header as a
/// plain <c>div</c> with a click handler and no tab stop, so a keyboard user could not open "Kullanım ekle", "Bulgu ekle" and
/// the others. Every such section is now a native <c>details</c>/<c>summary</c> disclosure: a tab stop that Enter and Space
/// toggle without script.
/// </summary>
public sealed class ServiceAccountDisclosureUiTests
{
    private static readonly Guid _account = Guid.Parse("5a1e0000-0000-4000-8000-0000000000d1");
    private static readonly AccountPermissions _work = new(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible);

    public static TheoryData<Type, string> DetailPanels => new()
    {
        { typeof(SaUsagePanel), "Kullanım ekle" },
        { typeof(SaFindingsPanel), "Bulgu ekle" },
        { typeof(SaHandoverPanel), "Devir bildir" },
        { typeof(SaOwnershipPanel), "Sahiplik öner veya teyit et" },
        { typeof(SaRequestsPanel), "Yeni iş (talep) aç" },
        { typeof(SaActionsPanel), "İşlem bildir" }
    };

    [Theory]
    [MemberData(nameof(DetailPanels))]
    public async Task AccountPanelForms_OpenFromANativeKeyboardDisclosure(Type panel, string title)
    {
        string html = await RenderAsync(panel, new Dictionary<string, object?> { ["Detail"] = Detail(), ["Busy"] = false, ["Revision"] = 0 });

        AssertDisclosure(html, title);
    }

    [Fact]
    public async Task EvidenceAndAccountEditForms_OpenFromANativeKeyboardDisclosure()
    {
        AssertDisclosure(await RenderAsync(typeof(SaEvidencePanel), new Dictionary<string, object?> { ["Detail"] = Detail(), ["Busy"] = false, ["Revision"] = 0 }),
            "Kanıt dosyası ekle");
        Dictionary<string, object?> edit = new() { ["Summary"] = Detail().Summary, ["Busy"] = false, ["Revision"] = 0 };
        AssertDisclosure(await RenderAsync(typeof(SaAccountEditForm), edit), "Hesap bilgilerini düzenle");
    }

    [Fact]
    public async Task Disclosure_IsANativeDetailsElement_ClosedUnlessAsked()
    {
        RenderFragment content = b => b.AddContent(0, "Sentetik içerik");
        string closed = await RenderAsync(typeof(SaDisclosure), new Dictionary<string, object?> { ["Title"] = "Sentetik form", ["ChildContent"] = content });
        AssertDisclosure(closed, "Sentetik form");
        closed.Should().Contain("Sentetik içerik").And.NotMatchRegex("<details[^>]* open");

        string open = await RenderAsync(typeof(SaDisclosure), new Dictionary<string, object?>
        {
            ["Title"] = "Sentetik form",
            ["Open"] = true,
            ["Class"] = "mt-3",
            ["ChildContent"] = content
        });
        open.Should().MatchRegex("<details[^>]* open").And.MatchRegex(@"<details class=""sa-disclosure mt-3""");
    }

    [Fact]
    public void Disclosure_GivesSummaryAndBodyButtonsAVisibleKeyboardFocusRing()
    {
        // Windows 2026-10-05: MudBlazor text buttons drew no focus indicator; both the summary and the buttons of an opened form need one.
        string css = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaDisclosure.razor.css"));
        css.Should().MatchRegex(@"\.sa-disclosure > summary:focus-visible\s*\{[^}]*outline:\s*2px solid")
            .And.MatchRegex(@"\.sa-disclosure-body ::deep \.mud-button-root:focus-visible\s*\{[^}]*outline:\s*2px solid");
    }

    [Fact]
    public void NoServiceAccountsPage_UsesAMudBlazorExpansionPanel()
    {
        string ui = Path.Combine(Root(), "src", "SecureOps.Ui");
        string[] files = [.. Directory.GetFiles(Path.Combine(ui, "Pages", "ServiceAccounts"), "*.razor"),
            .. Directory.GetFiles(Path.Combine(ui, "Shared", "Components", "ServiceAccounts"), "*.razor")];

        files.Should().NotBeEmpty();
        files.Where(f => File.ReadAllText(f).Contains("MudExpansionPanel", StringComparison.Ordinal)).Select(Path.GetFileName)
            .Should().BeEmpty("MudBlazor 6.16 panel headers take no keyboard focus; use SaDisclosure (native details/summary)");
    }

    /// <summary>The title is the text of a native summary inside a details element, and no MudBlazor panel header is rendered.</summary>
    private static void AssertDisclosure(string html, string title)
    {
        html.Should().NotContain("mud-expand-panel");
        Regex.IsMatch(html, $@"<details[^>]*>\s*<summary[^>]*>\s*{Regex.Escape(title)}\s*</summary>").Should().BeTrue($"\"{title}\" must be a native summary");
    }

    private static AccountDetail Detail()
    {
        AccountSummaryView summary = new(_account, "svc_synapp", "SYN", null, "Provisional", null, null, null, null, "Active", null, null, null, 1, null, [],
            null, "AAAAAAAAAAA=");
        RequestView request = new(Guid.NewGuid(), _account, "svc_synapp", "GmsaHandover", "gMSA ile devir", "Open", null, new SaRef(Guid.NewGuid(), "SYN GMSA"),
            null, null, null, null, null, null, null, null, null, false, true, [], null, null, "AAAAAAAAAAA=");
        return new AccountDetail(summary, [], [request], [], [], [], [], [], [], [], [], [], _work, [], null, [], 0);
    }

    private static async Task<string> RenderAsync(Type component, Dictionary<string, object?> parameters)
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new SyntheticNavigation());
        registrations.AddSingleton(new ServiceAccountApiClient(new HttpClient(new NoCalls()) { BaseAddress = new Uri("http://localhost:1/") },
            Substitute.For<IApiSessionContext>()));
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync(component, ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("http://localhost/", "http://localhost/service-accounts/" + _account);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private sealed class NoCalls : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Rendering a collapsed form must not call the API.");
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
