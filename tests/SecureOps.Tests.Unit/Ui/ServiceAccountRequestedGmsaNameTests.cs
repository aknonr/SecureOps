using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Requested gMSA name (migration 031): one shared 15-character rule for the registration hint, the request/transition hint
/// and the server; the UI warns but never disables sending; before 031 the field says so honestly. Synthetic names only.
/// </summary>
public sealed class ServiceAccountRequestedGmsaNameTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("  gmsa_synapp$ ", "gmsa_synapp")]
    [InlineData("SYN\\gmsa_synapp$", "gmsa_synapp")]
    [InlineData("gmsa_synapp$@syn.example", "gmsa_synapp")]
    [InlineData("SYN\\gmsa_synapp@syn.example", "gmsa_synapp")]
    [InlineData("$$", "")]
    public void Bare_DropsDomainPrefix_UpnSuffix_AndTrailingDollar(string? name, string bare) => ServiceAccountGmsaName.Bare(name).Should().Be(bare);

    [Theory]
    [InlineData("syn_15_chars_ok", false)]
    [InlineData("SYN\\syn_15_chars_ok$", false)]
    [InlineData("syn_16_chars_bad", true)]
    [InlineData("SYN\\syn_16_chars_bad$", true)]
    public void ExceedsLimit_CountsOnlyTheBareName(string name, bool exceeds) => ServiceAccountGmsaName.ExceedsLimit(name).Should().Be(exceeds);

    [Fact]
    public void RegistrationHint_UsesTheSharedLimit() => ServiceAccountUiText.GmsaNameLimit.Should().Be(ServiceAccountGmsaName.Limit).And.Be(15);

    [Fact]
    public void RequestedNameHint_WarnsAboveFifteen_AndSaysTheServerRefuses()
    {
        ServiceAccountUiText.RequestedGmsaNameHint("SYN\\syn_15_chars_ok$").Should().BeNull();
        string hint = ServiceAccountUiText.RequestedGmsaNameHint("SYN\\gmsa_synapp_reports$")!;

        hint.Should().Contain("19 karakter").And.Contain("en çok 15 karakter").And.Contain("Sunucu bu adı kaydetmez");
        ServiceAccountUiText.RequestedGmsaNameHelper("SYN\\gmsa_synapp$").Should().StartWith("11/15 karakter");
    }

    [Theory]
    [InlineData("requestedGmsaName", "en çok 15 karakter")]
    [InlineData("gmsaNameColumnsMissing", "031")]
    [InlineData("requestedGmsaNameTypeConflict", "önce adı gerekçeyle temizleyin")]
    public void ServerRefusals_AreExplainedInTurkish(string field, string expected)
    {
        UiProblem problem = UiProblemFactory.FromResponse(400, new ProblemDetailsPayload { Code = "ServiceAccountValidationFailed", Fields = [field] });

        ServiceAccountProblems.FieldMessage(problem).Should().Contain(expected);
    }

    [Fact]
    public void Forms_UseTheSharedField_KeepSendingEnabled_AndHonourTheSchemaFlag()
    {
        string components = Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts");
        string field = File.ReadAllText(Path.Combine(components, "SaGmsaNameField.razor"));
        string requests = File.ReadAllText(Path.Combine(components, "SaRequestsPanel.razor"));
        string transitions = File.ReadAllText(Path.Combine(components, "SaHandoverPanel.razor"));

        field.Should().Contain("ServiceAccountUiText.RequestedGmsaNameHint(Value)").And.Contain("role=\"status\"").And.Contain("Immediate=\"true\"")
            .And.Contain("veritabanı güncellemesi 031 uygulanmamış").And.NotContain("Disabled=");
        requests.Should().Contain("<SaGmsaNameField @bind-Value=\"_create.RequestedGmsaName\" Available=\"@Detail.RequestedGmsaNameAvailable\"")
            .And.Contain("<SaGmsaNameField @bind-Value=\"_edit.RequestedGmsaName\"").And.Contain("clear.Add(\"requestedGmsaName\")")
            .And.Contain("<MudButton OnClick=\"CreateAsync\" Disabled=\"Busy\"");
        transitions.Should().Contain("<SaGmsaNameField @bind-Value=\"_gmsaName\" Available=\"@Detail.RequestedGmsaNameAvailable\"")
            .And.Contain("Disabled=\"Busy\" Variant=\"Variant.Outlined\" Color=\"Color.Primary\" Class=\"mt-2\">İzlemeyi güncelle");
    }

    [Fact]
    public void Report_ListsRequestedNames_WithTheirCountedLength()
    {
        var account = Guid.NewGuid();
        ReportFacts facts = new([new AccountFact(account, "SYN\\svc_synapp", null, null)],
            [new RequestFact(Guid.NewGuid(), account, ServiceAccountActionType.GmsaConversion, ServiceAccountRequestStatus.Open, null, null, null, null, "k",
                RequestedGmsaName: "SYN\\gmsa_synapp$")],
            [], [], [], [new TransitionFact(account, GmsaSuitability.Review, false, "gmsa_synapp2$")], [], new Dictionary<Guid, string>(), null, true);

        ServiceAccountReport report = ServiceAccountMetrics.Compute(facts, new DateOnly(2026, 10, 5), DateTimeOffset.UtcNow, "Sentetik");

        report.GmsaNames.Should().HaveCount(2);
        report.GmsaNames![0].Should().Be(new GmsaNameLine("SYN\\svc_synapp", "gMSA geçiş izlemesi", "gmsa_synapp2$", 12, "Uygunluk: İnceleniyor"));
        report.GmsaNames[1].Should().Be(new GmsaNameLine("SYN\\svc_synapp", "gMSA geçişi talebi", "SYN\\gmsa_synapp$", 11, "Açık talep"));
        ReportDocument.From(report, Guid.NewGuid(), new string('a', 64), null, DateTimeOffset.UtcNow, "Sentetik").Sections
            .Should().ContainSingle(s => s.Title == "İstenen gMSA adları").Which.Rows.Should().HaveCount(2);

        ServiceAccountMetrics.Compute(facts with { RequestedGmsaNames = false }, new DateOnly(2026, 10, 5), DateTimeOffset.UtcNow, "Sentetik")
            .GmsaNames.Should().BeNull("before migration 031 the list is unknown, not empty");
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
