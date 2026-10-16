using System.Text.RegularExpressions;
using FluentAssertions;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The Service Accounts tours (GUIDED-TOUR-DESIGN.md): every step points at an area the page really renders, steps follow the
/// server's permissions, the guides are short, the report page has no tour, and nothing is stored in the browser.
/// </summary>
public sealed partial class ServiceAccountGuidedTourUiTests
{
    private static readonly AccountPermissions _everything = new(true, true, true, true, true, true, ServiceAccountAccessBasis.Responsible);

    public static TheoryData<string, string[]> Pages() => new()
    {
        { "ServiceAccountList", [.. ServiceAccountTours.List(true, true, true).Select(s => s.Target)] },
        { "ServiceAccountDetail", [.. ServiceAccountTours.Detail(_everything, true).Select(s => s.Target)] },
        { "ServiceAccountImports", [.. ServiceAccountTours.Imports.Select(s => s.Target)] },
        { "ServiceAccountPlans", [.. ServiceAccountTours.Plans(true).Select(s => s.Target)] },
        { "ServiceAccountPlanDetail", [.. ServiceAccountTours.PlanDetail.Select(s => s.Target)] },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTourTarget_IsRenderedByItsPage(string page, string[] targets)
    {
        string source = Ui("Pages", "ServiceAccounts", page + ".razor");
        HashSet<string> rendered = [.. TourAttribute().Matches(source).Select(m => m.Groups["name"].Value)];

        source.Should().Contain("Tour=", "the page hands its tour to SaPageGuide");
        foreach (string target in targets)
        {
            rendered.Should().Contain(target, $"{page} must render data-tour=\"{target}\"");
        }
    }

    [Fact]
    public void TabTitle_CarriesTheTourTarget_AndThePageGuideHostsTheTour()
    {
        Ui("Shared", "Components", "ServiceAccounts", "SaTabTitle.razor").Should().Contain("data-tour=\"@Tour\"");
        Ui("Shared", "Components", "ServiceAccounts", "SaPageGuide.razor").Should().Contain("<SoGuidedTour Id=\"@TourId\"")
            .And.Contain("@if (Tour is { Count: > 0 })");
    }

    [Fact]
    public void ReportPage_HasNoTour()
    {
        Ui("Pages", "ServiceAccounts", "ServiceAccountReports.razor").Should().NotContain("Tour=").And.NotContain("data-tour");
    }

    [Fact]
    public void Steps_FollowTheServersPermissions()
    {
        new GuidedTourState(ServiceAccountTours.List(false, false, false)).Steps.Select(s => s.Target).Should().NotContain(["sa-create", "sa-select", "sa-directory-search"]);
        new GuidedTourState(ServiceAccountTours.List(false, false, false)).Steps.Should().HaveCount(4);
        new GuidedTourState(ServiceAccountTours.List(true, true, true)).Steps.Should().HaveCount(7);

        AccountPermissions reader = new(false, false, false, false, false, false);
        new GuidedTourState(ServiceAccountTours.Detail(reader, true)).Steps.Select(s => s.Title).Should()
            .NotContain(["Bir gelişme mi oldu?", "Doğrulama"], "a reader may neither report nor verify");

        AccountPermissions participant = reader with { Basis = ServiceAccountAccessBasis.Participant, ParticipantRequestIds = [Guid.NewGuid()] };
        new GuidedTourState(ServiceAccountTours.Detail(participant, true)).Steps.Select(s => s.Title).Should().Contain("Bir gelişme mi oldu?")
            .And.NotContain("Doğrulama");
        new GuidedTourState(ServiceAccountTours.Detail(_everything, false)).Steps.Select(s => s.Title).Should()
            .NotContain("Bir gelişme mi oldu?", "the event flow is not installed (036)").And.Contain("Doğrulama");
        new GuidedTourState(ServiceAccountTours.Plans(false)).Steps.Select(s => s.Target).Should().NotContain("sa-plan-create");
    }

    [Fact]
    public void EveryStep_SaysWhatAndWhy_InOneSentenceEach_AndToursStayShort()
    {
        IReadOnlyList<SoTourStep>[] tours =
        [
            ServiceAccountTours.List(true, true, true), ServiceAccountTours.Detail(_everything, true), ServiceAccountTours.Imports,
            ServiceAccountTours.Plans(true), ServiceAccountTours.PlanDetail,
        ];
        foreach (IReadOnlyList<SoTourStep> tour in tours)
        {
            tour.Should().HaveCountGreaterThanOrEqualTo(3).And.HaveCountLessThanOrEqualTo(7);
            foreach (SoTourStep step in tour)
            {
                foreach (string sentence in new[] { step.What, step.Why })
                {
                    sentence.Should().EndWith(".").And.NotContain(". ", $"\"{step.Title}\" keeps one sentence");
                }
            }
        }
    }

    [Fact]
    public void ToursPagesGuides_AreShortened_ToThreeOrFourItems()
    {
        foreach (ServiceAccountGuides.Guide guide in new[] { ServiceAccountGuides.List, ServiceAccountGuides.Detail, ServiceAccountGuides.Imports,
            ServiceAccountGuides.Plans, ServiceAccountGuides.PlanDetail })
        {
            guide.Steps.Should().HaveCountGreaterThanOrEqualTo(3).And.HaveCountLessThanOrEqualTo(4);
            guide.Steps.Should().OnlyContain(s => s.Text.Length <= 80, "the detail lives in the tour");
        }
    }

    [Fact]
    public void Tour_StoresNothing_RespectsReducedMotion_AndDocksOnNarrowScreens()
    {
        string module = Ui("wwwroot", "js", "so-guided-tour.js");
        string component = Ui("Shared", "Components", "SoGuidedTour.razor");
        string css = Ui("Shared", "Components", "SoGuidedTour.razor.css");

        foreach (string source in new[] { module, component })
        {
            source.Should().NotContain("localStorage").And.NotContain("sessionStorage").And.NotContain("indexedDB").And.NotContain("document.cookie");
        }

        module.Should().Contain("prefers-reduced-motion: reduce").And.Contain("'instant'").And.Contain("(max-width: 700px)");
        css.Should().Contain("@media (prefers-reduced-motion: no-preference)").And.Contain("@media (max-width: 700px)")
            .And.Contain("pointer-events: none").And.Contain("forced-colors");
        css.Should().NotContain("transition", "the frame follows the area without trailing");
        Regex.Matches(css, "animation:").Should().ContainSingle("movement exists only for people who did not ask for less of it");
        css.IndexOf("animation:", StringComparison.Ordinal).Should().BeGreaterThan(css.IndexOf("prefers-reduced-motion: no-preference", StringComparison.Ordinal));
    }

    private static string Ui(params string[] path) => File.ReadAllText(Path.Combine([Root(), "src", "SecureOps.Ui", .. path]));

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("(?:data-tour|Tour)=\"(?<name>sa-[a-z-]+)\"")]
    private static partial Regex TourAttribute();
}
