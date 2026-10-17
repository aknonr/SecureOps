using FluentAssertions;
using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class RequestTypeSuggesterTests
{
    [Theory]
    [InlineData("Uygulama kurulumu", "Sunucuya izleme ajanı kurulması rica olunur.", OperationalRecordClassification.SoftwareInstallation)]
    [InlineData("YAZILIM YÜKLENMESİ", "", OperationalRecordClassification.SoftwareInstallation)]
    [InlineData("Yeni sunucu talebi", "Test ortamı için sanal sunucu ihtiyacı", OperationalRecordClassification.ServerRequest)]
    [InlineData("Yeni sunucu kurulumu", "", OperationalRecordClassification.ServerRequest)]
    [InlineData("Sunucu iadesi", "Kullanılmayan sunucunun emekliye ayrılması", OperationalRecordClassification.ServerRetirement)]
    public void ExplicitWordsForOneType_SuggestThatType(string title, string description, OperationalRecordClassification expected)
    {
        RequestTypeSuggestion? suggestion = RequestTypeSuggester.Suggest(title, description);

        suggestion.Should().NotBeNull();
        suggestion!.Type.Should().Be(expected);
        suggestion.RuleVersion.Should().Be(RequestTypeSuggester.RuleVersion);
        suggestion.Terms.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Terms_AreTheSourceWordsAsWritten_InReadingOrder()
    {
        RequestTypeSuggestion? suggestion = RequestTypeSuggester.Suggest("Uygulama Kurulumu", "kurulumu ve yüklenmesi");

        suggestion!.Terms.Should().Equal("Kurulumu", "yüklenmesi");
    }

    [Theory]
    [InlineData("Disk alanı artırımı", "Log klasörü doldu.")]
    [InlineData("", "")]
    [InlineData(null, null)]
    // Words for two types: the operator chooses, nothing is guessed.
    [InlineData("Sunucu iadesi ve yeni yazılım kurulumu", "")]
    [InlineData("Yeni sunucu talebi", "Eski sunucunun iadesi")]
    public void NoOrConflictingWords_SuggestNothing(string? title, string? description) =>
        RequestTypeSuggester.Suggest(title, description).Should().BeNull();

    [Fact]
    public void WordsMustStartAtAWordBoundary() =>
        // "yeniden sunucu" is not "yeni sunucu"; "ön_kurulum" style fragments inside a word do not count.
        RequestTypeSuggester.Suggest("Sunucu yeniden başlatma", "yeniden sunucuya bağlanma, xkurulum").Should().BeNull();

    [Fact]
    public void Suggestion_IsNotPartOfSdmEvaluationInput()
    {
        // ADR-0018: the evaluator never reads prose. The suggestion must stay outside its input.
        typeof(SdmEvaluationInput).GetProperties().Select(p => p.PropertyType)
            .Should().NotContain(typeof(RequestTypeSuggestion));
    }
}
