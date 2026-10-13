using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using K = SecureOps.Domain.ServiceAccounts.EventMailHintRules;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// OLAY-2b: <see cref="EventMailHints"/> over a synthetic corpus. Every text below is invented (placeholder accounts such as
/// <c>svc-ornek-01</c>, placeholder numbers such as <c>OCO-00000</c>); no real mail, name or number is used.
/// </summary>
public sealed class EventMailHintsTests
{
    private static readonly DateOnly _today = new(2026, 10, 13);

    private static MailHintResult Run(string text) => EventMailHints.Analyze(text, _today)!;

    public static TheoryData<string, string> EventCorpus => new()
    {
        { "Merhaba, svc-ornek-01 hesabının parolası değiştirildi, OCO-00000 ile yapıldı.", K.PasswordChanged },
        { "Şifre yenilendi.", K.PasswordChanged },
        { "ŞİFRE GÜNCELLENDİ", K.PasswordChanged },
        { "Hesabin sifresi degistirildi", K.PasswordChanged },
        { "The password for the account was changed yesterday.", K.PasswordChanged },
        { "Password has been rotated.", K.PasswordChanged },
        { "Hesap silindi.", K.AccountDeleted },
        { "Hesabı sildik, artık yok.", K.AccountDeleted },
        { "svc-ornek-01 kaldırıldı", K.AccountDeleted },
        { "The account was deleted from the directory.", K.AccountDeleted },
        { "Hesap decommissioned.", K.AccountDeleted },
        { "Hesap gMSA'ya geçti.", K.GmsaConverted },
        { "GMSA'ya çevrildi", K.GmsaConverted },
        { "Geçiş tamamlandı, gmsa kullanılıyor ve servis gMSA ye geçirildi", K.GmsaConverted },
        { "The account was migrated to gMSA.", K.GmsaConverted },
        { "Hesabın sahibi değişti.", K.OwnerChanged },
        { "Sorumlu devredildi, yeni sahip başka ekip.", K.OwnerChanged },
        { "The owner changed after the reorganization.", K.OwnerChanged },
        { "Servis yeni sunucuya taşındı.", K.ServiceMoved },
        { "Servisi taşıdık", K.ServiceMoved },
        { "The service was moved to another host.", K.ServiceMoved }
    };

    [Theory]
    [MemberData(nameof(EventCorpus))]
    public void EventPhrases_AreSuggested_InTurkishAndEnglish(string text, string kind)
    {
        MailHintResult result = Run(text);
        result.Events.Should().Contain(e => e.EventKind == kind && e.Reason.StartsWith("Matched:", StringComparison.Ordinal), text);
    }

    [Theory]
    [InlineData("Parola değişmedi, bekliyoruz.")]
    [InlineData("Şifre yarın yenilenecek.")]
    [InlineData("The password has not been changed yet.")]
    [InlineData("Hesap silinmedi.")]
    [InlineData("Hesap henüz silinecek.")]
    [InlineData("Selamlar, tablo ekte. İyi çalışmalar.")]
    [InlineData("or 5 minutes later we will check")]
    public void NegatedFutureOrUnrelatedText_SuggestsNothing(string text)
    {
        Run(text).Events.Should().BeEmpty(text);
    }

    [Fact]
    public void EveryTurkishLetterCaseAndAccentVariant_Matches()
    {
        foreach (string text in new[] { "PAROLA DEĞİŞTİ", "parola degisti", "Parola Değişti", "paRola DEgiStI", "parola değişti".Normalize(NormalizationForm.FormD), "PAROLA DEĞIŞTI" })
        {
            Run(text).Events.Should().Contain(e => e.EventKind == K.PasswordChanged, text);
        }
    }

    [Fact]
    public void ConflictingPhrases_SuggestEveryKind_NoneIsSelected()
    {
        MailHintResult result = Run("Hesap gMSA'ya geçti ve eski hesap silindi. Parola da değişti.");
        result.Events.Select(e => e.EventKind).Should().Equal(K.PasswordChanged, K.AccountDeleted, K.GmsaConverted);
    }

    [Theory]
    [InlineData("OCO-00000 ile yapıldı", "OCO", "OCO-00000")]
    [InlineData("oco-12345 numaralı değişiklik", "OCO", "OCO-12345")]
    [InlineData("OCO 12345 kaydı", "OCO", "OCO-12345")]
    [InlineData("OCO12345.", "OCO", "OCO-12345")]
    [InlineData("OR-0000000 onayı", "OR", "OR-0000000")]
    [InlineData("(or_77) bakınız", "OR", "OR-77")]
    [InlineData("OCO-2026/0042 sonrası", "OCO", "OCO-2026/0042")]
    public void RecordNumbers_AreFoundAndTyped_ByThePrefixWritten(string text, string type, string number)
    {
        MailHintResult result = Run(text);
        result.References.Should().ContainSingle().Which.Should().BeEquivalentTo(new HintedReference(type, number, "record-" + type.ToLowerInvariant()));
        ChangePlanRules.OcoNumber(result.References[0].Number).Should().Be(number, "the module's own record number rule accepts what is suggested");
    }

    [Theory]
    [InlineData("or 12345 olabilir")]
    [InlineData("12345 numaralı kayıt")]
    [InlineData("ORTAK 5")]
    [InlineData("socorro-5")]
    [InlineData("OCO yok")]
    [InlineData("OCO-")]
    public void NumbersWithoutAPrefix_OrWords_AreNotSuggested(string text)
    {
        Run(text).References.Should().BeEmpty(text);
    }

    [Fact]
    public void RecordNumbers_AreDeduplicated_CappedAndLongOnesRejected()
    {
        Run("OCO-1 oco-1 OCO 1 ve OR-1").References.Select(r => r.Number).Should().Equal("OCO-1", "OR-1");
        string many = string.Join(' ', Enumerable.Range(1, 40).Select(i => "OCO-" + i));
        Run(many).References.Should().HaveCount(K.MaxResults);
        Run("OCO-" + new string('7', 80)).References.Should().BeEmpty("longer than the module's 64-character record number");
    }

    [Theory]
    [InlineData("İşlem 01.10.2026 tarihinde yapıldı", 2026, 10, 1, "Explicit")]
    [InlineData("1/10/2026", 2026, 10, 1, "Explicit")]
    [InlineData("tarih: 2026-10-05", 2026, 10, 5, "Explicit")]
    [InlineData("1 Ekim 2026 günü", 2026, 10, 1, "Explicit")]
    [InlineData("3 Şubat 2026", 2026, 2, 3, "Explicit")]
    [InlineData("12 Ağustos 2026", 2026, 8, 12, "Explicit")]
    [InlineData("21 EYLÜL 2026", 2026, 9, 21, "Explicit")]
    [InlineData("1 October 2026", 2026, 10, 1, "Explicit")]
    [InlineData("October 1, 2026", 2026, 10, 1, "Explicit")]
    [InlineData("1 Ekim'de yapıldı", 2026, 10, 1, "AssumedYear")]
    [InlineData("5 Mart", 2026, 3, 5, "AssumedYear")]
    public void Dates_AreFound_InEveryFormat(string text, int year, int month, int day, string kind)
    {
        HintedDate date = Run(text).Dates.Should().ContainSingle().Subject;
        date.Value.Should().Be(new DateOnly(year, month, day));
        date.Kind.Should().Be(kind);
    }

    [Theory]
    [InlineData("31.02.2026")]
    [InlineData("13.13.2026")]
    [InlineData("15 Kasım 2026")]
    [InlineData("20 Aralık")]
    [InlineData("01.10.1999")]
    [InlineData("1234501.10.2026")]
    [InlineData("1 Servis 2026")]
    public void ImpossibleFutureOrUnreadableDates_AreNotSuggested(string text)
    {
        Run(text).Dates.Should().BeEmpty(text);
    }

    [Fact]
    public void SeveralDates_AreAllListed_InOrderOfAppearance_NoneChosen()
    {
        MailHintResult result = Run("Önce 2 Ekim 2026, sonra 05.10.2026 ve 2026-10-07; 3 Ekim'de de baktık. Yine 05.10.2026.");
        result.Dates.Select(d => d.Value).Should().Equal(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 3));
        result.Dates.Select(d => d.Kind).Should().Equal("Explicit", "Explicit", "Explicit", "AssumedYear");
    }

    [Fact]
    public void ADateInsideThePhrase_DoesNotBreakTheSentenceWindow()
    {
        Run("Parola 01.10.2026 tarihinde değişti.").Events.Should().Contain(e => e.EventKind == K.PasswordChanged);
    }

    [Theory]
    [InlineData("Parola: Abc12345!")]
    [InlineData("şifre = Zx9#kLm2")]
    [InlineData("Password: hunter22")]
    [InlineData("pwd:T0p-S3cret")]
    [InlineData("kullanıcı tokeni Qw3rTy9UiOpAsDfGh1 yapıştırıldı")]
    public void PasswordLikeContent_RaisesAWarning_AndIsNeverReturned(string text)
    {
        MailHintResult result = Run(text);
        result.Warnings.Should().Contain(EventMailHints.PasswordLikeContent);
        JsonSerializer.Serialize(result).Should().NotContain("Abc12345").And.NotContain("Zx9").And.NotContain("hunter22")
            .And.NotContain("S3cret").And.NotContain("Qw3rTy9");
    }

    [Theory]
    [InlineData("Parola: değişti, yeni parola PAM üzerinde.")]
    [InlineData("parola değişti.")]
    [InlineData("OCO-00000 için https://example.invalid/Path/Abc12345Def67890Ghi")]
    [InlineData("Hesap sahibi: ekip@example.invalid")]
    public void OrdinarySentences_DoNotRaisePasswordWarning(string text)
    {
        Run(text).Warnings.Should().NotContain(EventMailHints.PasswordLikeContent, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Merhaba, teşekkürler. İyi günler.")]
    public void NothingFound_IsReportedHonestly_WithEmptySuggestions(string text)
    {
        MailHintResult result = Run(text);
        result.Events.Should().BeEmpty();
        result.References.Should().BeEmpty();
        result.Dates.Should().BeEmpty();
        result.Warnings.Should().Equal(EventMailHints.NothingFound);
        result.RuleSet.Should().Be("olay-1");
    }

    [Fact]
    public void People_Emails_Phones_AndSignatures_AreNeverSearchedOrReturned()
    {
        const string mail = "Merhaba,\nAyşe Örnek <ayse.ornek@example.invalid> yazdı:\nTel: 0 555 000 00 00\nParola değişti, OCO-00000, 01.10.2026.\nSaygılarımla,\nAyşe Örnek\nSistem Yöneticisi";
        string json = JsonSerializer.Serialize(Run(mail));
        json.Should().NotContain("Ayşe").And.NotContain("Örnek").And.NotContain("example.invalid").And.NotContain("555").And.NotContain("Saygı").And.NotContain("Yönetici");
        json.Should().Contain("PasswordChanged").And.Contain("OCO-00000");
    }

    [Fact]
    public void TheAnswer_NeverQuotesTheText()
    {
        const string marker = "KOPYALANMAMALI-ozel-ifade";
        string json = JsonSerializer.Serialize(Run($"{marker} parola değişti OCO-00000 {marker}"));
        json.Should().NotContain("KOPYALANMAMALI").And.NotContain(marker);
    }

    [Fact]
    public void TooLongOrMissingText_IsRefusedBeforeMatching()
    {
        EventMailHints.Analyze(new string('a', K.MaxTextLength + 1), _today).Should().BeNull();
        EventMailHints.Analyze(null, _today).Should().BeNull();
        EventMailHints.Analyze(new string('a', K.MaxTextLength), _today).Should().NotBeNull("the limit is inclusive");
    }

    [Theory]
    [MemberData(nameof(HostileTexts))]
    public void HostileInput_FinishesQuickly_WithinTheLimitAndNeverThrows(string name, string text)
    {
        var watch = Stopwatch.StartNew();
        Action act = () => EventMailHints.Analyze(text, _today);
        act.Should().NotThrow(name);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), name);
    }

    public static TheoryData<string, string> HostileTexts => new()
    {
        { "a-repeats", new string('a', K.MaxTextLength) },
        { "parola-repeats", string.Concat(Enumerable.Repeat("parola ", 2_900))[..K.MaxTextLength] },
        { "parola-then-no-verb", "parola " + new string('x', K.MaxTextLength - 7) },
        { "digits-dots", string.Concat(Enumerable.Repeat("1.", 10_000)) },
        { "oco-digits", "oco-" + string.Concat(Enumerable.Repeat("1-", 9_990)) },
        { "or-chain", string.Concat(Enumerable.Repeat("or-", 6_600)) },
        { "spaces", new string(' ', K.MaxTextLength) },
        { "month-names", string.Concat(Enumerable.Repeat("1 ocak ", 2_900))[..K.MaxTextLength] },
        { "password-assignments", string.Concat(Enumerable.Repeat("pass:", 4_000)) },
        { "combining-marks", "a" + string.Concat(Enumerable.Repeat("́", 19_990)) },
        { "unpaired-surrogates", string.Concat(Enumerable.Repeat("\ud800", 20_000)) },
        { "backtracking-bait", string.Concat(Enumerable.Repeat("parola ", 400)) + "!" + string.Concat(Enumerable.Repeat("degis ", 400)) },
        { "nested-quantifier-bait", "(a+)+" + new string('a', 19_000) + "!" }
    };

    [Fact]
    public void Patterns_AreAllLinearTimeAndTimeLimited()
    {
        IEnumerable<Regex> all = K.EventRules.SelectMany(r => r.Action is null ? [r.Subject] : new[] { r.Subject, r.Action }).Concat(
        [
            K.NotHappened, K.RecordNumber, K.DayMonthYear, K.IsoDate, K.DayMonthName, K.MonthNameDay, K.PasswordAssignment
        ]);
        foreach (Regex regex in all)
        {
            regex.Options.HasFlag(RegexOptions.NonBacktracking).Should().BeTrue(regex.ToString());
            regex.MatchTimeout.Should().Be(K.MatchTimeout, regex.ToString());
        }

        K.EventRules.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        K.EventRules.Select(r => r.Kind).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Analysis_IsDeterministic()
    {
        const string mail = "Parola değişti 1 Ekim 2026 OCO-00000; hesap gMSA'ya geçti.";
        JsonSerializer.Serialize(Run(mail)).Should().Be(JsonSerializer.Serialize(Run(mail)));
    }
}
