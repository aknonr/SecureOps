using FluentAssertions;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>Regressions for the UI defects found in the TH-12 acceptance run (OR → Jira and In Use screens).</summary>
public sealed class Th12UiAcceptanceTests
{
    [Theory]
    [InlineData("JiraCreateDialog.razor")]
    [InlineData("JiraRetryDialog.razor")]
    public void JiraDialogTitle_HasTheIdTheHostBridgeNamesTheDialogFrom(string file)
    {
        // _Host.cshtml sets aria-labelledby from this element's id; without an id the dialog had no accessible name.
        Source("Shared", "Components", file).Should().Contain("<span id=\"@_titleId\" data-sdm-dialog-title>");
    }

    [Theory]
    [InlineData("Pages", "OperationalRecordDetail.razor.css")]
    [InlineData("Pages", "OperationalRecords.razor.css")]
    [InlineData("Pages", "InUse.razor.css")]
    [InlineData("Shared/Components", "JiraCreateDialog.razor.css")]
    [InlineData("Shared/Components", "JiraRetryDialog.razor.css")]
    public void MudButtons_OnTheseScreens_ShowAKeyboardFocusRing(string folder, string file) =>
        Source([.. folder.Split('/'), file]).Should().MatchRegex(@"::deep[^{]*\.mud-button-root[^{]*:focus-visible\s*\{[^}]*outline:\s*2px solid var\(--so-accent\)");

    [Fact]
    public void InUseCompletionButton_IsDescribedByTheServerReasonWhenClosed()
    {
        string page = Source("Pages", "InUse.razor");
        page.Should().Contain("id=\"inuse-gate-reason\"")
            .And.Contain("<strong>Onay düğmesi kapalı.</strong> @execution.Readiness.Explanation")
            .And.Contain("aria-describedby=\"@(execution.Readiness.Available ? null : \"inuse-gate-reason\")\"");
    }

    private static string Source(params string[] relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        string root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        return File.ReadAllText(Path.Combine([root, "src", "SecureOps.Ui", .. relative]));
    }
}
