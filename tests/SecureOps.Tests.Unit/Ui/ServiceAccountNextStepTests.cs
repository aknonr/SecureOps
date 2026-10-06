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
using SecureOps.Ui.Services.ServiceAccounts;
using SecureOps.Ui.Shared.Components.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The next-step card (UI only, synthetic data): derived from the loaded detail, honest about missing data, and a step the
/// caller may not take reads as waiting for someone else; an empty result never claims completion.
/// </summary>
public sealed class ServiceAccountNextStepTests
{
    private static readonly Guid _account = Guid.Parse("5a1e0000-0000-4000-8000-000000000011");
    private static readonly AccountPermissions _responsible = new(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible);
    private static readonly AccountPermissions _viewer = new(false, false, false, false, false, false);

    [Fact]
    public void NoUsageAndNoScan_IsUnknownNeverNotUsed()
    {
        IReadOnlyList<SaNextStep> steps = ServiceAccountNextStep.Compute(Detail(_responsible, owner: true));

        SaNextStep step = steps.Should().ContainSingle(s => s.Code == "usage-unknown").Subject;
        step.Kind.Should().Be(SaNextStepKind.Unknown);
        step.Reason.Should().Contain("kullanılmadığı anlamına gelmez");
    }

    [Fact]
    public void MissingOwner_ActsWhenAllowed_WaitsWhenViewer()
    {
        ServiceAccountNextStep.Compute(Detail(_responsible, owner: false)).First().Should().Match<SaNextStep>(s => s.Code == "ownership-missing" && s.Kind == SaNextStepKind.Act);
        ServiceAccountNextStep.Compute(Detail(_viewer, owner: false)).First().Should().Match<SaNextStep>(s => s.Code == "ownership-missing-wait" && s.Kind == SaNextStepKind.Wait);
    }

    [Fact]
    public void ProposedOwnership_OutranksEverythingElse()
    {
        OwnershipView proposal = new(Guid.NewGuid(), new SaRef(Guid.NewGuid(), "SYN Ekip"), null, "Proposed", "Manual", null, null, null, DateTimeOffset.UnixEpoch, null, null, "AAAAAAAAAAA=");
        AccountDetail detail = Detail(_responsible, owner: false) with { Ownership = [proposal] };

        ServiceAccountNextStep.Compute(detail).First().Code.Should().Be("ownership-decide");
    }

    [Fact]
    public void PendingScanDecisionAndPerformedAction_AreCountedFromServerTotals()
    {
        // The detail carries only the first page of items (PR #12); the pending count is the server's total, not the page.
        UsageScanView scan = Scan(Item("Former"), Item("Expected"));
        AccountDetail detail = Detail(_responsible, owner: true) with
        {
            UsageScans = [scan],
            UsageScanTotal = 7,
            UsageScanPending = 64,
            Actions = [Action("Performed"), Action("Verified")]
        };

        IReadOnlyList<SaNextStep> steps = ServiceAccountNextStep.Compute(detail);

        steps.Single(s => s.Code == "scan-decide").Reason.Should().StartWith("64 eşleşme");
        steps.Single(s => s.Code == "actions-verify").Reason.Should().StartWith("1 işlem");
        steps.Should().NotContain(s => s.Code == "usage-unknown", "a scan exists, so usage is not unknown");
    }

    [Fact]
    public void NoPendingTotal_NoScanStep_EvenWhenAPageItemLooksUndecided()
    {
        AccountDetail detail = Detail(_responsible, owner: true, usages: true) with { UsageScans = [Scan(Item("Former"))], UsageScanTotal = 1, UsageScanPending = 0 };

        ServiceAccountNextStep.Compute(detail).Should().NotContain(s => s.Code == "scan-decide");
    }

    [Fact]
    public void Viewer_SeesPendingWorkAsWaiting()
    {
        AccountDetail detail = Detail(_viewer, owner: true) with { UsageScans = [Scan(Item("Former"))], UsageScanTotal = 1, UsageScanPending = 1 };

        ServiceAccountNextStep.Compute(detail).Where(s => s.Code == "scan-decide").Should().OnlyContain(s => s.Kind == SaNextStepKind.Wait);
    }

    [Fact]
    public void UnplannedRule_PointsToUsageTab()
    {
        RuleEvaluationView rule = new("v1", "Gmsa", "gMSA", "Unplanned", "Kurala aykırı", [], null, null, false);
        AccountDetail detail = Detail(_responsible, owner: true, usages: true) with { Rule = rule };

        ServiceAccountNextStep.Compute(detail).Single(s => s.Code == "rule-unplanned").Tab.Should().Be(SaTab.Usage);
    }

    [Fact]
    public async Task EmptyList_SaysOnlyThatNothingIsKnownPending()
    {
        string html = await RenderAsync(Detail(_responsible, owner: true, usages: true));

        html.Should().Contain("Sıradaki adım").And.Contain("bekleyen bir adım görünmüyor").And.Contain("kapandığı veya doğrulandığı anlamına gelmez");
    }

    [Fact]
    public async Task Card_ShowsKindWithTextAndAnOpenButtonPerStep()
    {
        string html = await RenderAsync(Detail(_responsible, owner: false));

        html.Should().Contain("Sahip ekibi belirleyin").And.Contain("Sizden bekleniyor").And.Contain("Kullanımı belirleyin").And.Contain("Bilgi eksik");
        html.Should().Contain("+1 bekleyen daha").And.Contain("Sahiplik sekmesi");
    }

    [Fact]
    public void Css_GivesCardButtonsAVisibleKeyboardFocusRing()
    {
        string css = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaNextStepCard.razor.css"));
        css.Should().MatchRegex(@"\.so-panel ::deep \.mud-button-root:focus-visible\s*\{[^}]*outline:\s*2px solid");
    }

    private static AccountDetail Detail(AccountPermissions permissions, bool owner, bool usages = false)
    {
        SaRef? team = owner ? new SaRef(Guid.NewGuid(), "SYN Ekip") : null;
        AccountSummaryView summary = new(_account, "svc_synapp", "SYN", null, "Confirmed", null, team, null, null, "Active", null, null, null, 0, null, [], null, "AAAAAAAAAAA=");
        UsageView[] list = usages
            ? [new UsageView(Guid.NewGuid(), "IisAppPool", "IIS uygulama havuzu", null, null, "SYN-APP01", "SynPool", null, null, null, false, null, DateTimeOffset.UnixEpoch, "AAAAAAAAAAA=")]
            : [];
        return new AccountDetail(summary, [], [], [], [], [], [], [], [], [], [], [], permissions, list, null, [], 0);
    }

    private static ActionView Action(string result) =>
        new(Guid.NewGuid(), _account, null, "Review", "İnceleme", result, result, "Interim", null, null, "Day", null, null, null, null, null, null, false, false, null, null, [], null, "AAAAAAAAAAA=");

    private static UsageScanItemView Item(string role) =>
        new(Guid.NewGuid(), "SYN-APP01", role, "IisAppPool", "IIS uygulama havuzu", "SynPool", "SYN\\svc", null, null, "IisAppPool", null, null, null, null);

    private static UsageScanView Scan(params UsageScanItemView[] items) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Discovery", "SYN\\svc", null, "scan.json", new string('a', 64), "Combined", DateTimeOffset.UnixEpoch, null, null, "Sentetik beyan",
            "Sentetik kullanıcı", DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch, new UsageScanCoverageView(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), null, [], items);

    private static async Task<string> RenderAsync(AccountDetail detail)
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<SaNextStepCard>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { ["Detail"] = detail }))).ToHtmlString());
        return WebUtility.HtmlDecode(Regex.Replace(html, " b-[a-z0-9]{10}", string.Empty));
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
