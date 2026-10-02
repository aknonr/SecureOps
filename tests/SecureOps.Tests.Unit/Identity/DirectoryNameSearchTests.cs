using FluentAssertions;
using SecureOps.Infrastructure.Identity;

namespace SecureOps.Tests.Unit.Identity;

public sealed class DirectoryNameSearchTests
{
    private static DirectoryNameQuery Query(string text) =>
        DirectoryNameQuery.TryCreate(text, out string? error) ?? throw new InvalidOperationException(error);

    [Theory]
    [InlineData("ab", "NameQueryTooShort")]
    [InlineData("  a  b ", "NameQueryTooShort")]
    [InlineData("a.-'", "NameQueryTooShort")]
    [InlineData("ali*", "NameQueryCharacters")]
    [InlineData("ali)(cn=*", "NameQueryCharacters")]
    [InlineData("ali\\2a", "NameQueryCharacters")]
    [InlineData("syn.user1", "NameQueryCharacters")]
    [InlineData("bir iki üç dört beş", "NameQueryTooManyWords")]
    public void UnacceptableQueries_AreRejectedBeforeAnyProviderCall(string input, string code)
    {
        DirectoryNameQuery.TryCreate(input, out string? error).Should().BeNull();
        error.Should().Be(code);
    }

    [Fact]
    public void LongQueries_AreRejected_AndWhitespaceIsNormalized()
    {
        DirectoryNameQuery.TryCreate(new string('a', DirectoryNameQuery.MaximumLength + 1), out string? error).Should().BeNull();
        error.Should().Be("NameQueryTooLong");

        DirectoryNameQuery query = Query("  Ayşe \t  Yılmaz ");
        query.Text.Should().Be("Ayşe Yılmaz");
        query.Tokens.Should().Equal("Ayşe", "Yılmaz");
        Query("İsm").Tokens.Should().ContainSingle("three letters are the minimum");
        Query("O'Brien-Çelik").Text.Should().Be("O'Brien-Çelik");
    }

    [Fact]
    public void Escape_FollowsRfc4515()
    {
        DirectoryNameFilter.Escape("a*b(c)\\d\0").Should().Be(@"a\2ab\28c\29\5cd\00");
        DirectoryNameFilter.Escape("Ayşe O'Brien").Should().Be("Ayşe O'Brien");
    }

    [Fact]
    public void Filter_AsksForTurkishSpellings_AndOnlyATrailingWildcard()
    {
        string single = DirectoryNameFilter.Build(Query("ismail"));
        single.Should().StartWith("(&(objectCategory=person)(objectClass=user)(|");
        single.Should().Contain("(displayName=İsmail*)").And.Contain("(displayName=Ismail*)").And.Contain("(givenName=İSMAİL*)")
            .And.Contain("(displayName=ismail*)");
        single.Replace("*)", string.Empty, StringComparison.Ordinal).Should().NotContain("*", "only the trailing wildcard is added");

        string full = DirectoryNameFilter.Build(Query("ayşe yılmaz"));
        full.Should().Contain("(&(givenName=Ayşe*)(sn=Yılmaz*))").And.Contain("(displayName=Ayşe Yılmaz*)");
        full.Should().NotContain("(givenName=ayşe yılmaz*)", "a multi-word query does not search given names with the whole text");
    }

    [Theory]
    [InlineData("İSMAİL", "ismail")]
    [InlineData("Ismail", "ismail")]
    [InlineData("IŞIK", "isik")]
    [InlineData("ışık", "isik")]
    [InlineData("Şükrü  Öztürk", "sukru ozturk")]
    [InlineData("Çağrı", "cagri")]
    public void Fold_EquatesTurkishCaseAndAccents(string value, string key) => DirectoryNameMatching.Fold(value).Should().Be(key);

    [Theory]
    [InlineData("ismail", "syn.ismail.isik")]
    [InlineData("İSM", "syn.ismail.isik")]
    [InlineData("ismail işık", "syn.ismail.isik")]
    [InlineData("sukru", "syn.sukru.ozturk")]
    [InlineData("çağ gün", "syn.cagri.gunes")]
    public async Task MockDirectory_MatchesFirstOrFullName_InsensitiveToTurkishSpelling(string input, string account)
    {
        DirectoryNameSearchResult result = await new MockDirectoryNameSearchProvider().SearchAsync(Query(input), DirectoryNameQuery.MaximumResults, CancellationToken.None);

        result.Candidates.Should().ContainSingle().Which.SamAccountName.Should().Be(account);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task SameNamedPeople_AreBothReturned_InADeterministicOrder()
    {
        DirectoryNameSearchResult result = await new MockDirectoryNameSearchProvider().SearchAsync(Query("Ayşe Yılmaz"), DirectoryNameQuery.MaximumResults,
            CancellationToken.None);

        result.Candidates.Select(c => c.SamAccountName).Should().Equal("syn.ayse.yilmaz", "syn.ayse.yilmaz2");
        result.Candidates.Select(c => c.Department).Should().Equal("SYN Altyapı", "SYN Uygulama");
    }

    [Fact]
    public async Task Results_AreBoundedAndTruncationIsReported()
    {
        DirectoryNameCandidate[] many = [.. Enumerable.Range(1, 15).Select(i => new DirectoryNameCandidate($"Deniz Sentetik{i:00}", "Deniz", $"Sentetik{i:00}",
            $"syn.deniz{i:00}", null))];

        DirectoryNameSearchResult result = await new MockDirectoryNameSearchProvider(many).SearchAsync(Query("deniz"), DirectoryNameQuery.MaximumResults,
            CancellationToken.None);

        result.Candidates.Should().HaveCount(DirectoryNameQuery.MaximumResults);
        result.Truncated.Should().BeTrue();
        DirectoryNameMatching.Matches(Query("ali"), new DirectoryNameCandidate("Veli Ali", "Veli", "Ali", "syn.veli", null))
            .Should().BeFalse("a first-name query matches the start of the given or display name, not any substring");
    }
}
