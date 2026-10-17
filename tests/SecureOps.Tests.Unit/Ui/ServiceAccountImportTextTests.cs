using FluentAssertions;
using SecureOps.Ui.Services.ServiceAccounts;
using Xunit;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>Import preview codes are worded for people; an unknown code is shown as received (synthetic data).</summary>
public sealed class ServiceAccountImportTextTests
{
    [Fact]
    public void ObservationTextInDateColumn_NamesTheColumn_AndTheAcceptedDateFormats()
    {
        string text = ServiceAccountImportText.Warning("ObservationTextInDateColumn:PasswordLastSet");

        text.Should().Contain("“PasswordLastSet” sütunu").And.Contain("02.09.2026").And.NotContain("ObservationTextInDateColumn");
    }

    [Theory]
    [InlineData("InvalidDate:PasswordLastSet", "okunamadı")]
    [InlineData("AccountNameMissing", "Hesap adı boş")]
    [InlineData("ArchiveSheetNotImported:Eski", "içe aktarılmadı")]
    public void KnownCodes_BecomeSentences(string code, string expected) =>
        ServiceAccountImportText.Error(code).Should().Contain(expected).And.NotContain(code.Split(':')[0]);

    [Fact]
    public void UnknownCode_IsShownAsReceived() =>
        ServiceAccountImportText.Warning("SomethingNew:x").Should().Be("SomethingNew:x");
}
