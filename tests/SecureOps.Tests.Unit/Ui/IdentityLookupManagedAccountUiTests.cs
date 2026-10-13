using FluentAssertions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// G-26 UI follow-up: the identity lookup accepts a gMSA/MSA name (one trailing <c>$</c>) and the result states the directory
/// object class from the server's <c>accountTypeEvidence</c>, worded as what the object is, never as how it is used.
/// </summary>
public sealed class IdentityLookupManagedAccountUiTests
{
    [Theory]
    [InlineData("GroupManagedServiceAccount", "Grup yönetilen servis hesabı nesnesi (gMSA)")]
    [InlineData("ManagedServiceAccount", "Yönetilen servis hesabı nesnesi (MSA)")]
    [InlineData("User", "Standart kullanıcı nesnesi")]
    public void EvidenceLabel_NamesTheObjectClass(string evidence, string label) =>
        DirectoryView.AccountTypeEvidenceLabel(evidence).Should().Be(label);

    [Fact]
    public void IdentityResult_ShowsTheObjectClassFromServerEvidence()
    {
        string page = File.ReadAllText(Path.Combine(Root(), "src", "SecureOps.Ui", "Pages", "DirectoryUser.razor"));

        page.Should().Contain("DirectoryView.AccountTypeEvidenceLabel(user.AccountTypeEvidence)",
            "the overview states the directory object class the server reported");
        page.Should().Contain("<SoField Label=\"Dizin nesne türü\">");
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
