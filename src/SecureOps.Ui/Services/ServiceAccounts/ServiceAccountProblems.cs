namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>Operator wording for the Service Accounts API's stable codes (called from <see cref="UiProblemFactory"/>).</summary>
public static class ServiceAccountProblems
{
    private static readonly string[] _refresh = ["Sayfayı yenileyip güncel değerleri inceleyin; girdiğiniz bilgiler formda korunur."];

    /// <summary>Maps a module code, or returns null so the caller falls back to the status mapping.</summary>
    public static UiProblem? FromCode(string code) => code switch
    {
        "ServiceAccountNotFound" => Build(UiProblemKind.NotFound, code, "Kayıt bulunamadı",
            "Kayıt yok veya erişim kapsamınız dışında.", ["Listeye dönün."], false, true),
        "ServiceAccountAccessDenied" => Build(UiProblemKind.Forbidden, code, "Bu işlem için yetkiniz veya kapsamınız yok",
            "Servis hesapları için yetki ve kurum/ekip kapsamı ayrı ayrı gerekir. Kişi veya ekip kaydı yetki vermez.",
            ["Erişimim sayfasından yetkilerinizi, modül yöneticisinden kapsamınızı kontrol edin."], false, false),
        "ServiceAccountValidationFailed" => Build(UiProblemKind.Validation, code, "Bilgileri kontrol edin",
            "Girilen bilgi kurallara uymuyor. İşaretli alanı düzeltip tekrar gönderin.", [], false, false),
        "ServiceAccountConcurrencyConflict" => Build(UiProblemKind.Conflict, code, "Kayıt siz düzenlerken değişti",
            "Değişiklik uygulanmadı. Güncel değerler yüklendi.", _refresh, false, true),
        "ServiceAccountsNotConfigured" => Build(UiProblemKind.NotConfigured, code, "Servis hesapları modülü etkin değil",
            "Bu ortamda modülün veritabanı bağlantısı yapılandırılmadı.", ["Yöneticinize bildirin."], false, false),
        "ServiceAccountDirectoryUnavailable" => Build(UiProblemKind.UpstreamUnavailable, code, "Dizine şu an ulaşılamıyor",
            "Ad araması tamamlanmadı; dizinde değişiklik yapılmadı.", ["Kısa süre sonra tekrar deneyin veya tam hesap sorgusunu kullanın."], true, false),
        "ServiceAccountPersistenceUnavailable" => Build(UiProblemKind.UpstreamUnavailable, code, "Kayıt deposuna ulaşılamıyor",
            "İşlem tamamlanmadı; kısmi kayıt oluşmadı.", ["Kısa süre sonra tekrar deneyin."], true, false),
        "ServiceAccountImportFileRejected" => Build(UiProblemKind.Validation, code, "Dosya kabul edilmedi",
            "Dosya türü, boyutu veya içeriği güvenli aktarım kurallarına uymuyor (makro, dış bağlantı, bozuk arşiv veya sınır aşımı).",
            ["Dosyayı .xlsx, .csv veya .json olarak yeniden kaydedip deneyin."], false, false),
        "ServiceAccountImportPreviewStale" => Build(UiProblemKind.Conflict, code, "Önizleme güncel değil",
            "Önizlemeden sonra veri değişti; hiçbir satır yazılmadı.", ["Önizlemeyi yenileyin, farkları inceleyip yeniden onaylayın."], false, true),
        "ServiceAccountImportDecisionsRequired" => Build(UiProblemKind.Validation, code, "Karar bekleyen satırlar var",
            "Çakışan veya belirsiz satırlar için karar vermeden aktarım yapılamaz.", ["\"Karar gerekenler\" filtresini açın."], false, false),
        "ServiceAccountImportAlreadyCommitted" => Build(UiProblemKind.Conflict, code, "Bu dosya zaten aktarıldı",
            "Aynı dosya, dönem ve kapsam daha önce aktarıldı; tekrar kayıt oluşturulmadı.", ["Aktarım geçmişindeki sonucu açın."], false, false),
        "ServiceAccountIdempotencyKeyRequired" => Build(UiProblemKind.Validation, code, "İşlem anahtarı eksik",
            "Aktarım onayı tekrar gönderimi güvenli kılan anahtarla yapılmalıdır.", ["Sayfayı yenileyip onayı yeniden verin."], false, true),
        _ => null
    };

    /// <summary>Turkish explanation for the rejected field or business rule the API names (null when not specific).</summary>
    public static string? FieldMessage(UiProblem? problem) => problem?.Fields.FirstOrDefault() switch
    {
        "ActualDateRequired" => "Doğrulama için işlemin gerçek tarihi gerekir; önce işlem tarihini tamamlayın.",
        "ActualDateInFuture" => "Gerçekleşme tarihi ileri bir gün olamaz.",
        "VerificationBeforeAction" => "Doğrulama tarihi işlem tarihinden önce olamaz.",
        "VerifierRequired" => "Doğrulayan kişi seçilmeli.",
        "EvidenceRequired" => "Doğrulama için kanıt notu veya kanıt dosyası gerekir.",
        "OrRequiredForDeletion" => "Silme kapanışı OR numarası olmadan doğrulanamaz.",
        "ActionNotPerformed" => "Yalnız gerçekleşmiş işlem doğrulanabilir.",
        "ActionVoided" => "İptal edilmiş kayıt değiştirilemez.",
        "NoMatchingPerformedAction" => "Talebi tamamlandı olarak kapatmak için aynı türde gerçekleşmiş işlem gerekir.",
        "VerifiedClosureRequired" => "Silme veya gMSA ile devir talebi doğrulanmış hesap kapanışı olmadan tamamlanamaz.",
        "CloseReasonRequired" => "Gerek kalmadı veya iptal için gerekçe yazın.",
        "RequestAlreadyClosed" => "Talep zaten kapalı.",
        "PlanEndBeforeStart" => "Plan bitişi başlangıçtan önce olamaz.",
        "OwnershipNotConfirmed" => "Sahiplik teyidi talebi, teyitli sahip ekip veya kişi olmadan kapatılamaz.",
        "ClosureKindNotAllowed" => "Hesap kapanışı yalnız silme veya gMSA dönüşümü için kaydedilebilir; inceleme veya sahiplik teyidi hesabı kapatmaz.",
        "expectedVersion" => "Kayıt değişmiş; güncel sürüm yüklendi.",
        "reason" => "Değişiklik veya temizleme için gerekçe gerekli.",
        "teamId" => "Sahip ekibi değiştirmek kurum düzeyinde kapsam gerektirir.",
        "coverage" => "Tam liste beyanı yalnız koordinasyon listesi veya eski çalışma kitabı aktarımında yapılabilir.",
        "coverageOrganizationIds" => "Tam liste için kapsamınızdaki kurum(lar)ı seçin; kısmi veya bilinmeyen kapsamda kurum seçilmez.",
        "sourceReportDate" => "Kaynak rapor tarihi gerekli (tam liste beyanında zorunlu) ve ileri bir tarih olamaz.",
        "period" => "Dönem geçersiz: tarih aralığında bitiş başlangıçtan önce olamaz ve aralık en çok 366 gün olabilir.",
        "tooManyRows" => "Seçim 5.000 satırdan fazla; filtreyi daraltıp tekrar aktarın.",
        "requestId" => "Bu hesapta yalnız ekibinize atanmış açık talep üzerinden işlem bildirebilirsiniz.",
        "targetTeamId" => "Talebi başka ekibe aktarmak hesaptan sorumlu ekibin kararıdır.",
        "ownerId" => "Kanıtı yalnız ekibinize atanmış talebe veya ona bağlı işleme ekleyebilirsiniz.",
        "NameQueryTooShort" => "Ad aramasında en az 3 harf gerekir.",
        "NameQueryCharacters" => "Ad aramasında yalnız harf, boşluk, kesme işareti, tire ve nokta kullanılabilir; joker karakter kabul edilmez.",
        "NameQueryTooLong" => "Ad araması en çok 64 karakter olabilir.",
        "NameQueryTooManyWords" => "Ad araması en çok 4 kelime olabilir.",
        "NameQueryTooComplex" => "Ad aramasını daha kısa bir ad veya soyad ile daraltın.",
        "identityLookup" => "Dizinde ad araması için kimlik sorgulama yetkisi gerekir.",
        "kind" => "Kullanım türünü listeden seçin.",
        "databaseEngine" => "Veritabanı motoru yalnız veritabanı kullanımında seçilir.",
        "needVerified" => "İhtiyaç doğrulaması yalnız Windows servisi kullanımında işaretlenir.",
        "role" => "Bu ekip bu rolde zaten var ya da başka bir yürütücü ekip tanımlı; önce mevcut olanı kaldırın.",
        "scope" => "Bu işlem kurum düzeyinde veri kapsamı ister. Yetkileriniz (Erişimim) tek başına yetmez: başka bir modül yöneticisi "
            + "Modül yönetimi → Kapsam yetkileri'nden size \"Tüm kurum\" veya ilgili kurumu vermelidir. Kendinize kapsam veremezsiniz.",
        "selfGrant" => "Kendinize kapsam veremezsiniz. Bu koruma bilerek var: kapsamı sizden bağımsız bir modül yöneticisi vermelidir.",
        "corporateIdentity" => "Bu kurumsal kimlikle onaylı bir uygulama kullanıcısı bulunamadı. Kimliği DOMAIN\\kullanıcı veya UPN olarak birebir yazın; kişinin önce uygulamaya erişimi onaylanmış olmalı.",
        "bootstrapClosed" => "Tek seferlik ilk kurulum kullanılamaz: bu modülde daha önce kapsam verilmiş. Kapsamı başka bir modül yöneticisi vermelidir.",
        "bootstrapSchema" => "Tek seferlik ilk kurulum için gereken veritabanı güncellemesi (SA-003) henüz uygulanmadı.",
        "duplicate" => "Bu kullanıcının bu kapsamda zaten etkin bir yetkisi var.",
        "scopeKind" => "Kapsam türünü seçin; kurum için kurum, ekip için ekip seçilmelidir. Gerekçe zorunludur.",
        null => null,
        { } field => "Kontrol edilecek alan: " + field
    };

    private static UiProblem Build(UiProblemKind kind, string code, string title, string explanation, IReadOnlyList<string> steps, bool retryable,
        bool refresh) => new(kind, code, title, explanation, steps, retryable, refresh, null, null, null);
}
