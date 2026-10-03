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
    [InlineData("NameQueryTooShort", "en az 3 harf")]
    [InlineData("NameQueryCharacters", "joker karakter kabul edilmez")]
    [InlineData("identityLookup", "kimlik sorgulama yetkisi")]
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

    [Fact]
    public void DirectoryNameSearch_IsGated_ExplainsItsLimits_AndKeepsExactLookup()
    {
        string panel = Ui("Shared", "Components", "ServiceAccounts", "SaDirectoryNameSearch.razor");
        string list = Ui("Pages", "ServiceAccounts", "ServiceAccountList.razor");

        list.Should().Contain("@if (Can(Capabilities.IdentityLookup))").And.Contain("HttpMethod.Post, \"/directory/name-search\"");
        panel.Should().Contain("Seçim yetki vermez, hesap sahipliğini onaylamaz ve dizinde değişiklik yapmaz");
        panel.Should().Contain("href=\"identity-lookup\"", "exact account lookup stays available");
        panel.Should().Contain("context.ServiceAccountId is { } id").And.Contain("Kapsamınızda kayıt yok");
        panel.Should().NotContain("UserPrincipalName").And.NotContain("Mail").And.NotContain("Manager");
        UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = "ServiceAccountDirectoryUnavailable" }).Title.Should().Be("Dizine şu an ulaşılamıyor");
    }

    [Fact]
    public void ImportPage_ChecksScopeFirst_ExplainsHowScopeIsGranted_AndGuidesTheOperator()
    {
        string page = Ui("Pages", "ServiceAccounts", "ServiceAccountImports.razor");
        string code = Ui("Pages", "ServiceAccounts", "ServiceAccountImports.razor.cs");
        string setup = Ui("Shared", "Components", "ServiceAccounts", "SaScopeSetup.razor");
        string guide = Ui("Shared", "Components", "ServiceAccounts", "SaImportGuide.razor");

        code.IndexOf("\"/me\"", StringComparison.Ordinal).Should().BeLessThan(code.IndexOf("\"/imports\"", StringComparison.Ordinal),
            "scope is read before the import history so a missing scope is explained instead of shown as an access error");
        code.Should().Contain("_me?.ScopeKind is \"All\" or \"Organization\"");
        page.Should().Contain("<SaScopeSetup").And.Contain("<SaImportGuide").And.Contain("Önizleme için eksik");
        page.Should().NotContain("@number. @title", "the ordered list already numbers the steps");
        page.Should().Contain("role=\"radiogroup\"").And.Contain("Neden soruluyor");
        setup.Should().Contain("Kendinize kapsam veremezsiniz").And.Contain("Tüm kurum").And.Contain("href=\"service-accounts/admin\"");
        guide.Should().Contain("ServiceAccountImportProfiles.LegacyWorkbook").And.Contain("ServiceAccountImportProfiles.CoordinationList")
            .And.Contain("ServiceAccountImportProfiles.DbaHandover");
        UiProblem missingScope = new(UiProblemKind.Forbidden, "ServiceAccountAccessDenied", "t", "d", [], false, false, null, null, null) { Fields = ["scope"] };
        ServiceAccountProblems.FieldMessage(missingScope).Should().Contain("başka bir modül yöneticisi");
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
