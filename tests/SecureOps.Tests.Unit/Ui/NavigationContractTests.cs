using FluentAssertions;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins where the operator report is reachable from, and that capability gating survives.
/// </summary>
/// <remarks>
/// Asserted against the markup because the shell has no component-rendering harness in this
/// solution. It is a narrow contract — which destinations the primary navigation offers — and it is
/// exactly the thing a later edit would undo without noticing.
/// <para>
/// The backend route and capability are deliberately untouched. This is a navigation decision, not
/// a removal: the report still exists, reached from the Yönetim Panosu where the coverage and
/// limitation context that stops per-actor counts reading as a ranking already sits.
/// </para>
/// </remarks>
public sealed class NavigationContractTests
{
    [Fact]
    public void PrimaryNavigation_DoesNotOfferTheOperatorReportAsATopLevelDestination()
    {
        NavMenu().Should().NotContain("reporting/operators");
    }

    [Fact]
    public void ManagementDashboard_KeepsTheOperatorReportAsADrillDown()
    {
        // Removing it from navigation must not orphan it.
        Ui("Pages", "Dashboard.razor").Should().Contain("reporting/operators");
    }

    [Fact]
    public void OperatorReportRoute_StillExists()
    {
        Ui("Pages", "OperatorReport.razor").Should().Contain("@page \"/reporting/operators\"");
    }

    [Fact]
    public void OperatorReport_RemainsCapabilityGated()
    {
        Ui("Pages", "OperatorReport.razor").Should().Contain("Capabilities.ManagementReportingView");
    }

    [Fact]
    public void OperatorReport_KeepsItsAntiRankingStatement()
    {
        // The screen orders rows by operation count, so it has to say plainly that the ordering is
        // not a scoreboard. The disclaimer is the anti-ranking semantics; losing it while keeping
        // the sort is the regression worth pinning.
        string page = Ui("Pages", "OperatorReport.razor");

        page.Should().Contain("başarı sıralaması")
            .And.Contain("<strong>değildir</strong>")
            .And.Contain("çabayı, işin zorluğunu, çalışma süresini");
    }

    [Fact]
    public void NavigationEntries_RemainDrivenByCapabilitiesRatherThanRoleNames()
    {
        string menu = NavMenu();

        menu.Should().Contain("Capabilities.OperationalRecordsView")
            .And.Contain("Capabilities.ManagementReportingView")
            .And.Contain("Capabilities.AccessManageUsers");
    }

    [Fact]
    public void NavigationGroups_FollowShiftWork_AndKeepAdministrationFolded()
    {
        string menu = NavMenu();

        int shift = menu.IndexOf(">Vardiya işleri<", StringComparison.Ordinal);
        int identity = menu.IndexOf(">Kimlik ve hesaplar<", StringComparison.Ordinal);
        int links = menu.IndexOf(">Bağlantılar<", StringComparison.Ordinal);
        int reports = menu.IndexOf(">Raporlar<", StringComparison.Ordinal);
        shift.Should().BePositive();
        identity.Should().BeGreaterThan(shift);
        links.Should().BeGreaterThan(identity);
        reports.Should().BeGreaterThan(links);

        menu.Should().Contain("<MudNavGroup Title=\"Yönetim\"").And.Contain("@bind-Expanded=\"_adminExpanded\"");
        menu.Should().NotContain("SONRAKİ FAZLAR").And.Contain("href=\"audit-compliance\"").And.Contain("href=\"diagnostics-readonly\"");
        menu.Should().Contain("ServiceAccountCapabilities.View");
    }

    [Fact]
    public void TerminalApiSession_ForcesOnlyItsBrowserCircuitThroughReauthentication()
    {
        string layout = Ui("Shared", "MainLayout.razor");

        layout.Should().Contain("ApiSessions.ReauthenticationRequired += OnReauthenticationRequired")
            .And.Contain("string.Equals(browserSessionKey, ApiSession.BrowserSessionKey, StringComparison.Ordinal)")
            .And.Contain("session-expired?returnUrl=")
            .And.Contain("forceLoad: true")
            .And.Contain("ApiSessions.ReauthenticationRequired -= OnReauthenticationRequired");
    }

    private static string NavMenu() => Ui("Shared", "NavMenu.razor");

    private static string Ui(params string[] relativePath) =>
        File.ReadAllText(Path.Combine(
            [FindRepositoryRoot(), "src", "SecureOps.Ui", .. relativePath]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
