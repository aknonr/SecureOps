namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// Plain-language text for the codes the import preview returns (<c>Code</c> or <c>Code:argument</c>). The server decides what is a
/// warning or an error; this only words it. An unknown code is shown as received so nothing the server said is hidden.
/// </summary>
public static class ServiceAccountImportText
{
    /// <summary>Date formats the server reads (mirrors the parser; the server decides).</summary>
    public const string AcceptedDates = "2026-09-02, 2026-09-02 10:15:30, 02.09.2026 veya 02.09.2026 10:15";

    /// <summary>A sentence for a warning code.</summary>
    public static string Warning(string code) => Describe(code);

    /// <summary>A sentence for an error code.</summary>
    public static string Error(string code) => Describe(code);

    private static string Describe(string code)
    {
        (string name, string? arg) = Split(code);
        string field = arg is null ? "bir sütun" : $"“{arg}” sütunu";
        return name switch
        {
            "ObservationTextInDateColumn" => $"{Cap(field)} tarih beklenirken metin içeriyor; metin not olarak saklandı, tarih uydurulmadı. Kabul edilen biçimler: {AcceptedDates}.",
            "InvalidDate" => $"{Cap(field)} okunamadı. Kabul edilen tarih biçimleri: {AcceptedDates}.",
            "TimeWithoutZoneKeptAsDate" => $"{Cap(field)} saat içeriyor ancak saat dilimi belli değil; yalnız gün alındı.",
            "FormulaValue" => $"{Cap(field)} formül içeriyor; hesaplanmış değer alındı, formül çalıştırılmadı.",
            "AccountNameMissing" => "Hesap adı boş.",
            "ArchiveSheetNotImported" => $"“{arg}” arşiv sayfası içe aktarılmadı.",
            "ArchiveBlockIgnored" => $"Arşiv sayfasındaki satır yok sayıldı ({arg}).",
            "LegacySheetHeaderMissing" => $"“{arg}” sayfasında beklenen başlık satırı bulunamadı.",
            "PackageSectionMissing" => $"Pakette “{arg}” bölümü yok.",
            "AmbiguousPersonLeftEmpty" => $"{Cap(field)}: aynı ada uyan birden çok kişi var, kişi boş bırakıldı.",
            "BatchSpellingVariant" => "Aynı dosyada bu ad farklı yazımlarla geçiyor.",
            "CoverageContradicted" => "Beyan edilen kapsam ile dosyadaki satırlar çelişiyor.",
            "CoverageNotComplete" => "Beyan edilen kapsam tamamlanmamış görünüyor.",
            "GmsaRoutedToExecutor" => "gMSA işi uygulayıcıya yönlendirildi.",
            "HandoverTargetTeamNotDeclared" => "Devir için hedef ekip belirtilmemiş.",
            "LinkedAccountNotLinked" => $"{Cap(field)}: ilişkili hesap sistemdeki bir kayda bağlanamadı.",
            "OlderThanLatestObservation" => "Bu gözlem, kayıtlı en son gözlemden eski; son gözlemin yerine geçmez.",
            "SimilarGmsaRequestExists" => "Benzer bir gMSA talebi zaten var.",
            "SourceAcceptanceDateIsNotAcceptanceEvidence" => "Kaynaktaki onay tarihi tek başına onay kanıtı sayılmaz.",
            "UndatedActionCountedSeparately" => "Tarihsiz işlem ayrı sayıldı.",
            "VerificationBeforeActionKeptAsNote" => "İşlemden önceki doğrulama not olarak saklandı.",
            _ => code
        };
    }

    private static (string Name, string? Argument) Split(string code)
    {
        int colon = code.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? (code, null) : (code[..colon], code[(colon + 1)..]);
    }

    private static string Cap(string text) => text.Length == 0 ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + text[1..];
}
