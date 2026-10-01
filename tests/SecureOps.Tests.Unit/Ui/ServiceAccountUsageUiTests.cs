using FluentAssertions;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>Pins the knowledge-base rule UI: Turkish field guidance, capability-gated commands and honest wording.</summary>
public sealed class ServiceAccountUsageUiTests
{
    [Theory]
    [InlineData("kind", "Kullanım türünü")]
    [InlineData("databaseEngine", "Veritabanı motoru")]
    [InlineData("needVerified", "Windows servisi")]
    [InlineData("role", "yürütücü ekip")]
    public void UsageAndTeamRoleFields_AreExplainedInTurkish(string field, string expected)
    {
        UiProblem problem = UiProblemFactory.FromResponse(400, new ProblemDetailsPayload { Code = "ServiceAccountValidationFailed", Fields = [field] });

        ServiceAccountProblems.FieldMessage(problem).Should().Contain(expected);
    }

    [Fact]
    public void UsagePanel_GatesCommands_AndNeverClaimsScanningOrSuitability()
    {
        string panel = Ui("Shared", "Components", "ServiceAccounts", "SaUsagePanel.razor");

        panel.Should().Contain("@if (Detail.Permissions.Verify)", "an exception is a verifier decision");
        panel.Should().Contain("@if (Detail.Permissions.Work)");
        panel.Should().Contain("sistem sunuculara bağlanıp otomatik tarama yapmaz");
        panel.Should().Contain("Öneri, gMSA uygunluğu, sahiplik veya kapanış değildir");
        panel.Should().NotContain("HttpMethod.Delete");
        Ui("Pages", "ServiceAccounts", "ServiceAccountDetail.razor").Should().Contain("<SaUsagePanel");
    }

    [Fact]
    public void ReportView_ShowsVersionTwoSections_AndExplainsVersionOneSnapshots()
    {
        string view = Ui("Shared", "Components", "ServiceAccounts", "SaReportView.razor");

        view.Should().Contain("Direktörlük görünümü").And.Contain("gMSA hunisi").And.Contain("Risk adayları").And.Contain("Bilgi bankası kuralları");
        view.Should().Contain("Kişi bazlı sayı veya sıralama yoktur");
        view.Should().Contain("bölümleri bu sürümde yoktur");
        Ui("Pages", "ServiceAccounts", "ServiceAccountReports.razor").Should().Contain("Nüsha karşılaştırma");
        Ui("Pages", "ServiceAccounts", "ServiceAccountAdmin.razor").Should().Contain("gMSA yönlendirme ayarı").And.Contain("Bu ayar erişim vermez");
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
}
