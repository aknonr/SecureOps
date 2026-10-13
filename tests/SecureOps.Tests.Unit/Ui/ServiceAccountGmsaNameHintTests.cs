using FluentAssertions;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The 15-character gMSA name hint on account registration (review 2026-10-05): advice only, never a block; the server and
/// Active Directory decide whether a name is valid. Synthetic names only.
/// </summary>
public sealed class ServiceAccountGmsaNameHintTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("svc_synapp")]
    [InlineData("syn_15_chars_ok")]
    [InlineData("gmsa_15_chars_x$")]
    [InlineData("SYN\\syn_15_chars_ok")]
    [InlineData("syn_15_chars_ok@syn.example")]
    public void NoHint_UpToFifteenCharacters(string? name) => ServiceAccountUiText.GmsaNameHint(name).Should().BeNull();

    [Fact]
    public void LongGmsaName_SaysItExceedsTheGmsaLimit_WithoutBlocking()
    {
        string hint = ServiceAccountUiText.GmsaNameHint(" SYN\\gmsa_synapp_reports$ ")!;

        hint.Should().Contain("gMSA adı").And.Contain("19 karakter").And.Contain("en çok 15 karakter").And.Contain("sondaki $ hariç")
            .And.Contain("Kayıt engellenmez");
    }

    [Fact]
    public void LongServiceAccountName_AdvisesAShorterGmsaName_ForAConversion()
    {
        string hint = ServiceAccountUiText.GmsaNameHint("svc_synapp_reporting")!;

        hint.Should().Contain("20 karakter").And.Contain("gMSA'ya dönüştürülecekse").And.Contain("en çok 15 karakter").And.Contain("Kayıt engellenmez");
        hint.Should().NotContain("geçersiz", "a user account name of 16-20 characters is valid; only a gMSA name is limited to 15");
    }

    [Fact]
    public void RegistrationForm_ShowsTheHintWhileTyping_AndKeepsSubmitEnabled()
    {
        string form = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Shared", "Components", "ServiceAccounts", "SaCreateAccountForm.razor"));

        form.Should().Contain("ServiceAccountUiText.GmsaNameHint(_model.AccountName)").And.Contain("Immediate=\"true\"").And.Contain("role=\"status\"");
        form.Should().Contain("Disabled=\"Busy\"", "the hint never disables registration; the server decides");
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
