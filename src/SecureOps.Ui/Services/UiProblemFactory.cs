using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Ui.Services;

/// <summary>
/// Translates API failures into <see cref="UiProblem"/> values with Turkish operator-facing copy.
/// </summary>
/// <remarks>
/// This is the single place error wording lives, so every screen explains the same backend condition
/// the same way. Codes come from <see cref="OperationalErrorCodes"/>; the mapping is keyed on the
/// stable code rather than the HTTP status, because one status covers several distinct operator
/// situations (a 409 may mean "someone else claimed this" or "the source record changed").
/// <para>
/// Unknown codes fall back to a status-derived classification instead of surfacing raw API text, so a
/// backend code added later degrades to a safe generic message rather than leaking internals.
/// </para>
/// </remarks>
public static class UiProblemFactory
{
    private const string RefreshStep = "Kaydı yenileyip güncel durumu görün.";
    private const string RetryStep = "Birkaç saniye bekleyip tekrar deneyin.";
    private const string ContactAdminStep = "Sorun sürerse platform yöneticinize başvurun.";
    private const string ReferenceStep = "Destek talebinde aşağıdaki referans numarasını paylaşın.";

    /// <summary>
    /// Builds a problem from a parsed API error body.
    /// </summary>
    /// <param name="statusCode">HTTP status code of the response.</param>
    /// <param name="payload">Parsed error body, when the response carried one.</param>
    /// <returns>Operator-facing problem description.</returns>
    public static UiProblem FromResponse(int statusCode, ProblemDetailsPayload? payload)
    {
        string? code = payload?.EffectiveCode;
        string? correlationId = payload?.EffectiveCorrelationId;
        string? stage = payload?.Stage;

        UiProblem mapped = code is null
            ? FromStatus(statusCode)
            : FromCode(code, statusCode);

        // WorkflowConflict is returned both for "another workflow step is running" and for
        // "the outcome of a Jira create is unknown". Only the stage separates them, and they
        // demand opposite responses — wait and retry, versus stop and reconcile by hand. Getting
        // this wrong turns a duplicate-issue risk into a retry button.
        if (string.Equals(stage, ReconciliationStage, StringComparison.Ordinal))
        {
            mapped = ReconciliationRequired(code ?? OperationalErrorCodes.WorkflowConflict);
        }
        else if (string.Equals(stage, "application-mapping", StringComparison.Ordinal))
        {
            mapped = mapped with
            {
                Title = "Uygulama Kurulumu eşlemesi eksik",
                Explanation = "Bu talep türünün Jira etiket eşlemesi doğrulanmadı. SunucuTalep ile yayımlanamaz.",
                NextSteps = ["Süreç sahibinden Uygulama Kurulumu için onaylı alan ve etiket eşlemesini isteyin."]
            };
        }

        // The API's own retryable flag wins when present: it reflects server-side knowledge of whether
        // the durable workflow can safely accept the same command again.
        bool retryable = !string.Equals(stage, ReconciliationStage, StringComparison.Ordinal)
            && (payload?.Retryable ?? mapped.Retryable);

        return mapped with
        {
            Retryable = retryable,
            CorrelationId = correlationId,
            Stage = stage,
            StatusCode = statusCode,
            Fields = payload?.Fields ?? []
        };
    }

    /// <summary>
    /// Builds a problem for a transport-layer failure where no response was received.
    /// </summary>
    /// <returns>Operator-facing problem description.</returns>
    public static UiProblem NetworkFailure() => new(
        UiProblemKind.Network,
        "ApiUnreachable",
        "Servise ulaşılamıyor",
        "SecureOps servisine şu anda bağlanılamıyor. Bu genellikle geçici bir ağ veya servis kesintisidir.",
        [RetryStep, ContactAdminStep],
        Retryable: true,
        RequiresRefresh: false,
        CorrelationId: null,
        Stage: null,
        StatusCode: null);

    /// <summary>
    /// Builds a problem for a client-side timeout.
    /// </summary>
    /// <returns>Operator-facing problem description.</returns>
    public static UiProblem TimedOut() => new(
        UiProblemKind.Timeout,
        "RequestTimeout",
        "İstek zaman aşımına uğradı",
        "Servis beklenen sürede yanıt vermedi. İşlem tamamlanmamış olabilir.",
        [RetryStep, "İşlem tekrarlanmadan önce güncel durumu kontrol edin.", ContactAdminStep],
        Retryable: true,
        RequiresRefresh: true,
        CorrelationId: null,
        Stage: null,
        StatusCode: null);

    /// <summary>
    /// ProblemDetails <c>stage</c> the API uses when a Jira create outcome is unresolved.
    /// </summary>
    internal const string ReconciliationStage = "jira-reconciliation";

    /// <summary>Preserves support metadata while explaining an unacknowledged publication.</summary>
    public static UiProblem UncertainPublication(UiProblem problem) => ReconciliationRequired(problem.Code) with
    {
        CorrelationId = problem.CorrelationId,
        StatusCode = problem.StatusCode,
        Stage = ReconciliationStage
    };

    /// <summary>
    /// The unknown-outcome state, which must never be presented as an ordinary retryable failure.
    /// </summary>
    /// <param name="code">Stable API code, carried through for support.</param>
    /// <returns>Operator-facing problem description.</returns>
    /// <remarks>
    /// Deliberately not <see cref="UiProblemKind.Conflict"/>'s usual wording. A conflict means
    /// someone else got there first and the operator can look and try again. This means SecureOps
    /// does not know whether a Jira issue was created, so acting again could produce a duplicate.
    /// <c>Retryable</c> is false and stays false: the API also reports it false, and the two agree.
    /// </remarks>
    private static UiProblem ReconciliationRequired(string code) => Build(
        UiProblemKind.Conflict, code,
        "Jira sonucu doğrulanmalı",
        "Bu kayıt için bir Jira oluşturma denemesi başlatıldı, ancak sonucu doğrulanamadı. "
        + "Jira kaydı oluşmuş olabilir de olmayabilir de.",
        ["Jira'da bu operasyonel kayda ait bir kayıt olup olmadığını elle kontrol edin.",
         "Kontrol sonucunu destek referansıyla platform yöneticisine bildirin. Yeniden başlatma için doğrulanmış mutabakat gerekir.",
         "Doğrulama yapılmadan yeni bir Jira kaydı oluşturmayın; mükerrer kayıt riski vardır."],
        retryable: false, requiresRefresh: true);

    private static UiProblem FromCode(string code, int statusCode) => code switch
    {
        "AnnouncementInvalid" or "AnnouncementIncomplete" => Build(UiProblemKind.Validation, code,
            "Duyuru alanlarını kontrol edin", "Eksik veya geçersiz alanları düzeltin. Düzenlemeleriniz korunuyor.", [], false, false),
        "AnnouncementConflict" => Build(UiProblemKind.Conflict, code,
            "Taslağın daha yeni bir kaydı var", "Düzenlemeleriniz korunuyor. Güncel kayıtla karşılaştırıp devam edin.", [], false, true),
        "AnnouncementAssetChanged" or "AnnouncementAssetMissing" => Build(UiProblemKind.Conflict, code,
            "Görsel kullanılamıyor", "Görseli yeniden seçip kaydedin. Sorun sürerse yöneticinize bildirin.", [], false, false),
        "AnnouncementsDisabled" or "AnnouncementConfigurationUnavailable" => Build(UiProblemKind.NotConfigured, code,
            "Duyurular kullanıma açık değil", "Yöneticiniz modül ayarlarını kontrol etmelidir.", [], false, false),
        "InUseConflict" => Build(UiProblemKind.Conflict, code, "In Use veri sürümü değişti",
            "Bu taslak veya önizleme artık güncel değil. İşlem tekrarlanmadı.",
            ["Kayıtlı veriyi yeniden okuyun, cevapları kontrol edip tekrar kaydedin."], retryable: true, requiresRefresh: true),
        "InUseInvalid" => Build(UiProblemKind.Validation, code, "In Use bilgilerini kontrol edin",
            "Sunucu, cevap veya atama bilgisi geçersiz. Girilen cevaplar korunur.",
            ["Sunucu cevaplarını ve atama gerekçesini kontrol edin; taslak için kanıt notu zorunlu değildir."], retryable: false, requiresRefresh: false),
        "InUseIncomplete" => Build(UiProblemKind.Validation, code, "Rapor henüz hazır değil",
            "Her doğrulanmış sunucu için üç soruyu yanıtlayın. Bilinmeyen cevaplar taslakta korunabilir.",
            ["Sunucu seçiciden eksik cevapları kontrol edin."], retryable: false, requiresRefresh: false),
        "InUseAssigneeUnavailable" or "InUseAssignmentRequired" => Build(UiProblemKind.Forbidden, code, "In Use ataması gerekli",
            "İnceleme yalnızca onaylı ve bu kayda atanmış uygulama kullanıcısı tarafından kaydedilebilir.",
            ["Yetkili koordinatörden güncel inceleyici atamasını kontrol etmesini isteyin."], retryable: false, requiresRefresh: true),
        // ---- Application access -------------------------------------------------------------
        OperationalErrorCodes.AccessPending => Build(
            UiProblemKind.AccessPending, code,
            "Erişiminiz onay bekliyor",
            "Kimliğiniz doğrulandı, ancak SecureOps uygulama erişiminiz henüz onaylanmadı.",
            ["Onay tamamlandığında bu ekranı yenileyin.", "Aciliyet varsa yetkili yöneticinize başvurun."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.AccessDisabled => Build(
            UiProblemKind.AccessDisabled, code,
            "Erişiminiz devre dışı",
            "SecureOps uygulama erişiminiz devre dışı bırakılmış durumda.",
            ["Erişiminizin yeniden açılması için platform yöneticinize başvurun."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.AccessDenied => Build(
            UiProblemKind.Forbidden, code,
            "Bu işlem için yetkiniz yok",
            "Hesabınız etkin, ancak bu işlem için gereken yetki tanımlı değil.",
            ["Erişimim sayfasından mevcut yetkilerinizi görebilirsiniz.", "İhtiyacınız varsa yöneticinizden yetki talep edin."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.AccessValidationFailed => Build(
            UiProblemKind.Validation, code,
            "Girilen bilgiler kabul edilmedi",
            "Gerekçe veya seçilen roller sunucunun beklediği biçimde değil.",
            ["Gerekçenin boş olmadığından ve en az bir geçerli rol seçtiğinizden emin olun.",
             "Bilgileri düzeltip işlemi yeniden gönderin."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.AccessRequestAlreadyDecided => Build(
            UiProblemKind.Conflict, code,
            "Talep zaten sonuçlandırılmış",
            "Bu erişim talebi başka bir yönetici tarafından onaylanmış veya reddedilmiş. Kararlar geri alınamaz.",
            ["Listeyi yenileyip talebin güncel durumunu görün.",
             "Farklı bir karar gerekiyorsa kullanıcının erişimini kullanıcı ayrıntısından yönetin."],
            retryable: false, requiresRefresh: true),

        // Marked retryable by the API, and that is accurate — but only after re-reading. Re-sending
        // the same expectedVersion would fail identically, so the UI must reload authoritative state
        // and make the administrator look at it before acting again.
        OperationalErrorCodes.AccessConcurrencyConflict => Build(
            UiProblemKind.Conflict, code,
            "Kayıt siz bakarken değişti",
            "Başka bir yönetici bu kaydı siz işlem yaparken güncelledi. Değişikliğiniz uygulanmadı.",
            ["Güncel durum yeniden yüklendi; gözden geçirin.",
             "İşlem hâlâ gerekliyse güncel bilgilerle yeniden uygulayın."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.AccessUserInvalidState => Build(
            UiProblemKind.Conflict, code,
            "Kullanıcının durumu bu işleme uygun değil",
            "Rol ataması yalnızca onaylı kullanıcılar için yapılabilir; kapatılmış veya henüz onaylanmamış bir hesapta uygulanamaz.",
            ["Kullanıcının güncel durumunu ayrıntı sayfasından kontrol edin.",
             "Onay bekleyen bir kullanıcı için önce erişim talebini onaylayın."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.AccessSelfApprovalDenied => Build(
            UiProblemKind.Forbidden, code,
            "Kendi talebinizi onaylayamazsınız",
            "Görevler ayrılığı gereği bir erişim talebini talep sahibi onaylayamaz.",
            ["Talebi başka bir yetkili yöneticinin onaylaması gerekir."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.AccessRequestInvalidState => Build(
            UiProblemKind.Conflict, code,
            "Talep durumu değişmiş",
            "Bu erişim talebi artık beklemede değil; başka bir yönetici tarafından sonuçlandırılmış olabilir.",
            [RefreshStep],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.AccessRecordNotFound => Build(
            UiProblemKind.NotFound, code,
            "Kayıt bulunamadı",
            "İlgili kullanıcı veya erişim talebi bulunamadı.",
            ["Listeye dönüp güncel kayıtları görüntüleyin."],
            retryable: false, requiresRefresh: true),

        // ---- Identity lookup ----------------------------------------------------------------
        OperationalErrorCodes.InvalidIdentityInput => Build(
            UiProblemKind.Validation, code,
            "Girilen hesap kabul edilmedi",
            "Hesap değeri, tek ve tam bir hesap için beklenen biçime uymuyor.",
            ["Tek bir hesap girin; boşluk, virgül veya joker karakter kullanmayın.", "DOMAIN\\hesap biçimi kabul edilir."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.IdentityNotFound => Build(
            UiProblemKind.NotFound, code,
            "Eşleşen kimlik bulunamadı",
            "Sorgulanan hesap için dizinde tam eşleşen bir kayıt yok.",
            ["Hesap yazımını kontrol edin.", "Hesap farklı bir alan adında olabilir."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.IdentityProviderUnavailable => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Active Directory hizmetine şu anda ulaşılamıyor",
            "Hesap sorgusu için dizin sağlayıcısına bağlanılamadı. Sorgunuz çalıştırılmadı.",
            [RetryStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        OperationalErrorCodes.IdentityProviderTimeout => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Dizin servisi zaman aşımına uğradı",
            "Kimlik sağlayıcısı beklenen sürede yanıt vermedi.",
            [RetryStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        OperationalErrorCodes.IdentityProviderBadResponse => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Dizin servisi beklenmeyen yanıt verdi",
            "Kimlik sağlayıcısından gelen yanıt işlenemedi. Sonuç güvenilir olmadığı için gösterilmiyor.",
            [RetryStep, ReferenceStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        // ---- Directory Explorer ---------------------------------------------------------------
        OperationalErrorCodes.DirectoryInvalidInput => Build(
            UiProblemKind.Validation, code,
            "Girilen değer kabul edilmedi",
            "Dizin sorguları yalnızca tek ve tam bir hesap veya grup kabul eder.",
            ["Tek bir hesap ya da grup adı girin; joker karakter, LDAP filtresi veya liste kullanmayın.",
             "DOMAIN\\ad biçimi kabul edilir."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.DirectoryPrincipalNotFound => Build(
            UiProblemKind.NotFound, code,
            "Hesap dizinde bulunamadı",
            "Sorgulanan hesap için dizinde tam eşleşen bir kayıt yok.",
            ["Hesap yazımını kontrol edin.", "Hesap farklı bir alan adında olabilir."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.DirectoryGroupNotFound => Build(
            UiProblemKind.NotFound, code,
            "Grup dizinde bulunamadı",
            "Sorgulanan grup için dizinde tam eşleşen bir kayıt yok.",
            ["Grup adının yazımını kontrol edin.", "Grup adı yerine sAMAccountName deneyebilirsiniz."],
            retryable: false, requiresRefresh: false),

        // Not a failure of the query: the server answered, and told the UI how far it got. The
        // screens must present that as a bounded result rather than as an empty or negative one.
        OperationalErrorCodes.DirectoryQueryLimitExceeded => Build(
            UiProblemKind.Validation, code,
            "Sorgu sınırı aşıldı",
            "Bu sorgu, dizin üzerinde izin verilen tarama sınırını aşıyor. Sonuç eksik kalacağı için "
            + "gösterilmedi.",
            ["Daha dar bir grup veya hesap ile deneyin.",
             "Sonuç gerçekten bu kadar büyükse dizin ekibiyle birlikte değerlendirin."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.DirectoryProviderUnavailable => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Active Directory hizmetine şu anda ulaşılamıyor",
            "Dizin sağlayıcısına bağlanılamadı. Sorgunuz çalıştırılmadı.",
            [RetryStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        // Distinct from unavailable on purpose. The directory answered other calls; this one ran
        // out of time. Retrying a narrower query is often the right move, and telling an operator
        // "AD is down" when it is not sends them to the wrong team.
        OperationalErrorCodes.DirectoryProviderTimeout => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Active Directory sorgusu süre sınırı içinde tamamlanamadı",
            "Sorgu, sunucu tarafındaki süre sınırına takıldı. Bu, dizin hizmetinin çalışmadığı "
            + "anlamına gelmez; sorgu bu grup için beklenenden uzun sürmüş olabilir.",
            ["Daha dar bir grup veya hesap ile tekrar deneyin.",
             RetryStep,
             ContactAdminStep],
            retryable: true, requiresRefresh: false),

        // A result, not a failure: the server walked as far as its bounds allowed and said so. The
        // one thing that must never follow is a "no membership" conclusion.
        OperationalErrorCodes.DirectoryTraversalPartial => Build(
            UiProblemKind.Validation, code,
            "Üyelik analizi kısmi tamamlandı",
            "Analiz, sunucu tarafındaki güvenlik ve başarım sınırları içinde kısmen tamamlandı. "
            + "Elde edilen kanıt eksiktir.",
            ["Kısmi sonuç, üyelik yok kararı için kullanılmamalıdır.",
             "Daha dar bir grup ile analiz etmeyi deneyin.",
             ContactAdminStep],
            retryable: false, requiresRefresh: false),

        // ---- Application sessions ---------------------------------------------------------------
        OperationalErrorCodes.SessionExpired => Build(
            UiProblemKind.SessionExpired, code,
            "Oturumunuz sona erdi",
            "SecureOps oturumunuz boşta kalma veya azami süre nedeniyle sunucu tarafında sonlandırıldı.",
            ["Yeniden oturum açın; kaldığınız sayfaya döneceksiniz."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.SessionRevoked => Build(
            UiProblemKind.SessionExpired, code,
            "Oturumunuz sonlandırıldı",
            "Bu oturum bir yönetici tarafından sonlandırıldı ya da erişim tanımınız değişti.",
            ["Yeniden oturum açabilirsiniz.", "Erişiminiz kapatıldıysa platform yöneticinize başvurun."],
            retryable: false, requiresRefresh: false),

        // Already ended is the common case here, and it is not an error the administrator caused:
        // the session may have expired between the list being rendered and the button being pressed.
        OperationalErrorCodes.SessionNotFound => Build(
            UiProblemKind.Conflict, code,
            "Oturum artık mevcut değil",
            "Bu oturum zaten sonlanmış olabilir; listeyi görüntülediğinizden bu yana süresi dolmuş "
            + "veya kullanıcı çıkış yapmış olabilir.",
            ["Listeyi yenileyip güncel oturumları görün."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.SessionValidationFailed => Build(
            UiProblemKind.Validation, code,
            "Oturum isteği kabul edilmedi",
            "Gönderilen oturum kimliği veya gerekçe sunucunun beklediği biçimde değil.",
            ["Gerekçenin boş olmadığından emin olun.", "Listeyi yenileyip işlemi tekrarlayın."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.SessionStoreUnavailable => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Oturum kayıt deposuna ulaşılamıyor",
            "Oturum bilgileri okunamadığı için işlem güvenli şekilde durduruldu.",
            [RetryStep, ReferenceStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        // ---- Cross-cutting ------------------------------------------------------------------
        OperationalErrorCodes.RateLimitExceeded => Build(
            UiProblemKind.RateLimited, code,
            "Çok fazla istek gönderildi",
            "Kısa sürede izin verilenden fazla istek yapıldı. Bu sınır, dizin ve kaynak sistemleri korumak içindir.",
            ["Kısa bir süre bekleyip tekrar deneyin."],
            retryable: true, requiresRefresh: false),

        OperationalErrorCodes.AuditStoreUnavailable => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Denetim kaydı alınamadı",
            "Denetim kaydı yazılamadığı için işlem güvenli şekilde durduruldu. SecureOps, kayıt altına alınamayan işlemi tamamlamaz.",
            [RetryStep, ReferenceStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        OperationalErrorCodes.InvalidIdempotencyKey => Build(
            UiProblemKind.Validation, code,
            "İşlem anahtarı geçersiz",
            "İşlemin tekrarlanmasını önleyen anahtar kabul edilmedi.",
            ["Sayfayı yenileyip işlemi yeniden başlatın.", ContactAdminStep],
            retryable: false, requiresRefresh: true),

        // ---- Operational record source ------------------------------------------------------
        OperationalErrorCodes.OperationalSourceUnavailable => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Kaynak sistem yanıt vermiyor",
            "Operasyonel kayıtların alındığı kaynak sisteme ulaşılamıyor. Liste güncellenemedi.",
            [RetryStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        OperationalErrorCodes.OperationalSourceAuthenticationFailed => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Kaynak sistem kimlik doğrulaması başarısız",
            "SecureOps kaynak sisteme bağlanamadı. Bu bir yapılandırma sorunudur, sizin yetkinizle ilgili değildir.",
            [ReferenceStep, ContactAdminStep],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.OperationalRecordQueryFailed => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Kayıtlar getirilemedi",
            "Operasyonel kayıt sorgusu tamamlanamadı.",
            [RetryStep, ReferenceStep],
            retryable: true, requiresRefresh: true),

        OperationalErrorCodes.OperationalRecordNotFound => Build(
            UiProblemKind.NotFound, code,
            "Operasyonel kayıt bulunamadı",
            "Bu kayıt artık mevcut değil veya görüntüleme yetkiniz kapsamında değil.",
            ["Kayıt listesine dönün."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.OperationalRecordInvalidState => Build(
            UiProblemKind.Conflict, code,
            "Kayıt bu işlem için uygun durumda değil",
            "Kaydın mevcut iş akışı durumu bu işleme izin vermiyor.",
            [RefreshStep],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.OperationalRecordAlreadyClaimed => Build(
            UiProblemKind.Conflict, code,
            "Kayıt başka bir operatörde",
            "Bu kayıt üzerinde şu anda başka bir operatör çalışıyor. Aynı kaydın iki kez işlenmesini önlemek için işlem durduruldu.",
            ["Kaydı yenileyip güncel sahiplik durumunu görün.", "Devralmanız gerekiyorsa ilgili operatörle görüşün."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.OperationalRecordChanged => Build(
            UiProblemKind.Conflict, code,
            "Kaynak kayıt değişmiş",
            "Kaynak kayıt önizlemeden sonra değişti. Jira kaydı oluşturulmadı. Kaydı yeniden inceleyin.",
            [RefreshStep, "Güncel içeriği doğruladıktan sonra işlemi tekrarlayın."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.OperationalRecordNoLongerOpen => Build(
            UiProblemKind.Conflict, code,
            "Kaynak kayıt kapanmış",
            "Kaynak sistemdeki kayıt artık açık değil; kapalı bir kayıt için Jira oluşturulmaz.",
            [RefreshStep, "Kayıt yeniden açıldıysa işlemi tekrar başlatın."],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.OperationalRecordCloseFailed => Build(
            UiProblemKind.Conflict, code,
            "Kaynak kayıt tamamlanamadı",
            "Jira kaydı oluşturuldu ancak kaynak kayıt tamamlanamadı. Jira tekrar oluşturulmadan "
                + "kaynak tamamlama işlemi yeniden denenebilir.",
            ["Kaynak tamamlama işlemini yeniden deneyin.", ReferenceStep, ContactAdminStep],
            retryable: true, requiresRefresh: true),

        OperationalErrorCodes.OperationalRecordCommentUpdateFailed => Build(
            UiProblemKind.Conflict, code,
            "Kaynak kayda not eklenemedi",
            "Jira tarafı tamamlandı, ancak kaynak kayda açıklama yazılamadı.",
            ["Mutabakat için işlemi yeniden deneyin.", ReferenceStep],
            retryable: true, requiresRefresh: true),

        // ---- Requester resolution ------------------------------------------------------------
        OperationalErrorCodes.RequesterResolutionFailed => Build(
            UiProblemKind.Conflict, code,
            "Talep sahibi çözümlenemedi",
            "Kayıttaki talep sahibi dizinde eşleştirilemedi. Jira kaydı doğru kişiye bağlanamayacağı için işlem durduruldu.",
            ["Kaynak kayıttaki talep sahibi bilgisini kontrol edin.", ContactAdminStep],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.RequesterResolutionAmbiguous => Build(
            UiProblemKind.Conflict, code,
            "Talep sahibi için birden fazla eşleşme var",
            "Dizinde birden fazla tam eşleşme bulundu. Yanlış kişiye kayıt açılmaması için işlem durduruldu.",
            ["Talep sahibini kaynak sistemde netleştirin.", ContactAdminStep],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.OperatorReporterResolutionFailed => Build(
            UiProblemKind.Conflict, code,
            "Jira raporlayıcısı doğrulanamadı",
            "İşlemi yapan kullanıcı Jira üzerinde doğrulanamadığı için kayıt oluşturulmadı.",
            ["Kurumsal oturum hesabınızın Jira kullanıcısıyla eşleştiğini doğrulayın.", ContactAdminStep],
            retryable: false, requiresRefresh: true),

        // ---- Jira -----------------------------------------------------------------------------
        OperationalErrorCodes.JiraUnavailable => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Jira yanıt vermiyor",
            "Jira servisine ulaşılamıyor. Kayıt oluşturulmadı.",
            [RetryStep, ContactAdminStep],
            retryable: true, requiresRefresh: false),

        OperationalErrorCodes.JiraUnauthorized => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Jira entegrasyon yetkisi reddedildi",
            "SecureOps'un Jira entegrasyon kimliği kabul edilmedi. Bu bir yapılandırma sorunudur, sizin yetkinizle ilgili değildir.",
            [ReferenceStep, ContactAdminStep],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.JiraValidationFailed => Build(
            UiProblemKind.Validation, code,
            "Jira alanları kabul edilmedi",
            "Jira, önerilen kayıt alanlarını reddetti. Eşleme ile Jira proje yapılandırması uyuşmuyor olabilir.",
            ["Önizlemedeki alanları kontrol edin.", ReferenceStep, ContactAdminStep],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.JiraReporterRejected => Build(
            UiProblemKind.Validation, code,
            "Jira raporlayıcıyı kabul etmedi",
            "Doğrulanan kullanıcı raporlayıcı olarak Jira tarafından reddedildi. Entegrasyon hesabına raporlayıcıyı değiştirme yetkisi gerekebilir.",
            [ReferenceStep, ContactAdminStep],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.JiraCreateFailed => Build(
            UiProblemKind.Conflict, code,
            "Jira kaydı oluşturulamadı",
            "Jira kaydı oluşturma işlemi tamamlanamadı. Kaydın oluşup oluşmadığı doğrulanmalıdır.",
            [RefreshStep, "Yalnızca sunucu güncel durumda yeniden denemeye izin veriyorsa devam edin.", ReferenceStep],
            retryable: true, requiresRefresh: true),

        OperationalErrorCodes.JiraAlreadyCreated => Build(
            UiProblemKind.Conflict, code,
            "Bu kayıt için Jira zaten oluşturulmuş",
            "Bu kayıt için daha önce işlem başlatıldığı için ikinci bir Jira kaydı oluşturulmadı.",
            [RefreshStep, "Mevcut Jira kaydını inceleyin."],
            retryable: false, requiresRefresh: true),

        // ---- Durable workflow ------------------------------------------------------------------
        OperationalErrorCodes.WorkflowConflict => Build(
            UiProblemKind.Conflict, code,
            "İş akışı başka bir işlemde",
            "Bu kayıt üzerinde başka bir iş akışı işlemi sürüyor.",
            [RefreshStep, RetryStep],
            retryable: true, requiresRefresh: true),

        OperationalErrorCodes.WorkflowAlreadyCompleted => Build(
            UiProblemKind.Conflict, code,
            "İş akışı zaten tamamlanmış",
            "Bu iş akışı daha önce tamamlandı; tekrar çalıştırılmasına gerek yok.",
            [RefreshStep],
            retryable: false, requiresRefresh: true),

        OperationalErrorCodes.WorkflowAlreadyInProgress => Build(
            UiProblemKind.Conflict, code,
            "Aynı işlem hâlihazırda çalışıyor",
            "Aynı kapsamda bir komut zaten yürütülüyor. Mükerrer çalıştırma engellendi.",
            ["İşlem tamamlanana kadar bekleyin.", RefreshStep],
            retryable: true, requiresRefresh: true),

        // ---- Resource catalogue and personal shift sets -------------------------------------------
        // NotFound and "not visible to you" are deliberately indistinguishable in the contract, so
        // the wording must not speculate about which one happened — saying "you lack permission"
        // would leak the existence of a restricted entry.
        ResourceErrors.NotFound => Build(
            UiProblemKind.NotFound, code,
            "Bağlantı bulunamadı",
            "Bu kayıt bulunamadı veya görüntüleme kapsamınızda değil. Arşivlenmiş ya da kaldırılmış olabilir.",
            ["Listeye dönüp güncel kayıtları görüntüleyin."],
            retryable: false, requiresRefresh: true),

        ResourceErrors.Invalid => Build(
            UiProblemKind.Validation, code,
            "Girilen bilgiler kabul edilmedi",
            "Gönderilen alanlar kurallara uymuyor. Adres yalnızca HTTPS olabilir ve alan sınırları aşılamaz.",
            ["Alanları kontrol edip tekrar kaydedin."],
            retryable: false, requiresRefresh: false),

        ResourceErrors.Limit => Build(
            UiProblemKind.Validation, code,
            "Kapasite sınırına ulaşıldı",
            "Bu liste için izin verilen en fazla kayıt sayısına ulaşıldı.",
            ["Yeni kayıt eklemeden önce kullanılmayan kayıtları çıkarın."],
            retryable: false, requiresRefresh: true),

        // Retryable, but only after re-reading: the whole point of the version guard is that the
        // second attempt must be based on somebody else's saved state, not on the stale form.
        ResourceErrors.Conflict => Build(
            UiProblemKind.Conflict, code,
            "Kayıt siz düzenlerken değişti",
            "Bu kayıt başka bir yerden güncellendi. Değişikliğin üzerine yazılmaması için işlem durduruldu.",
            [RefreshStep, "Güncel hâli inceleyip değişikliğinizi tekrar uygulayın."],
            retryable: true, requiresRefresh: true),

        // ---- Real-data read-only integration ----------------------------------------------------
        // Not a fault. The environment is configured to read real Turuncu Hat and Jira data while
        // refusing every external write, so the server refused exactly as intended. No retry step is
        // offered: repeating the same forbidden write is the one thing that cannot help.
        OperationalErrorCodes.ExternalWritesDisabled => Build(
            UiProblemKind.Conflict, code,
            "Yazma işlemi bu modda kapalı",
            "Bu TEST modunda dış sistemlere yazma işlemleri kapalıdır.",
            ["Kaydı inceleyebilir ve Jira taslağını önizleyebilirsiniz.", ReferenceStep],
            retryable: false, requiresRefresh: true),

        // ---- Management reporting --------------------------------------------------------------
        // Two 503s that mean opposite things, which is why they are mapped explicitly instead of
        // falling through to the generic "servis yanıt vermiyor". Reporting persistence has simply
        // not been enabled yet in this environment; reporting it as an outage sends an administrator
        // to investigate a fault that does not exist, and it will never clear by retrying.
        OperationalErrorCodes.ReportingPersistenceNotConfigured => Build(
            UiProblemKind.NotConfigured, code,
            "Yönetim raporlaması henüz etkin değil",
            "Bu ortamda doğrulanmış raporlama verisi henüz kullanılamıyor. Kullanım ve operasyon metrikleri gösterilemiyor.",
            ["Raporlamanın etkinleştirilmesi için platform yöneticinize başvurun."],
            retryable: false, requiresRefresh: false),

        OperationalErrorCodes.ReportingUnavailable => Build(
            UiProblemKind.UpstreamUnavailable, code,
            "Raporlama servisi şu anda yanıt vermiyor",
            "Raporlama verisi geçici olarak okunamadı. Bu bir yapılandırma eksikliği değil; "
                + "tekrar denenebilir.",
            [RetryStep, ReferenceStep, ContactAdminStep],
            retryable: true, requiresRefresh: true),

        OperationalErrorCodes.ReportingValidationFailed => Build(
            UiProblemKind.Validation, code,
            "Rapor aralığı kabul edilmedi",
            "Seçilen tarih aralığı sunucu kurallarına uymuyor.",
            ["Farklı bir dönem seçip tekrar deneyin."],
            retryable: false, requiresRefresh: false),

        _ => FromStatus(statusCode) with { Code = code }
    };

    private static UiProblem FromStatus(int statusCode) => statusCode switch
    {
        401 => Build(
            UiProblemKind.SessionExpired, "Unauthenticated",
            "Oturumunuz sonlanmış",
            "Güvenlik nedeniyle oturumunuz sonlandırıldı.",
            ["Yeniden oturum açın; kaldığınız sayfaya döneceksiniz."],
            retryable: false, requiresRefresh: false),

        403 => Build(
            UiProblemKind.Forbidden, OperationalErrorCodes.AccessDenied,
            "Bu işlem için yetkiniz yok",
            "Bu işlem için gereken yetki hesabınızda tanımlı değil.",
            ["Erişimim sayfasından mevcut yetkilerinizi görebilirsiniz."],
            retryable: false, requiresRefresh: false),

        404 => Build(
            UiProblemKind.NotFound, "NotFound",
            "Kayıt bulunamadı",
            "İstenen kayıt bulunamadı veya görüntüleme yetkiniz kapsamında değil.",
            ["Listeye dönüp güncel kayıtları görüntüleyin."],
            retryable: false, requiresRefresh: true),

        400 or 422 => Build(
            UiProblemKind.Validation, "InvalidRequest",
            "Girilen bilgiler kabul edilmedi",
            "Gönderilen bilgiler doğrulama kurallarına uymuyor.",
            ["Alanları kontrol edip tekrar gönderin."],
            retryable: false, requiresRefresh: false),

        409 => Build(
            UiProblemKind.Conflict, "Conflict",
            "Durum değişmiş",
            "Kaydın durumu siz görüntülerken değişti. İşlem güvenli şekilde durduruldu.",
            [RefreshStep],
            retryable: false, requiresRefresh: true),

        429 => Build(
            UiProblemKind.RateLimited, OperationalErrorCodes.RateLimitExceeded,
            "Çok fazla istek gönderildi",
            "Kısa sürede izin verilenden fazla istek yapıldı.",
            ["Kısa bir süre bekleyip tekrar deneyin."],
            retryable: true, requiresRefresh: false),

        >= 500 => Build(
            UiProblemKind.UpstreamUnavailable, "ServiceUnavailable",
            "Servis şu anda yanıt veremiyor",
            "İşlem sunucu tarafında tamamlanamadı.",
            [RetryStep, ReferenceStep, ContactAdminStep],
            retryable: true, requiresRefresh: true),

        // The last resort. It says the one thing that is certainly true and nothing else: an
        // unclassified failure has no safe detail to offer, and the support reference is what turns
        // "it did not work" into something an administrator can actually escalate.
        _ => Build(
            UiProblemKind.Unexpected, "UnexpectedError",
            "İşlem tamamlanamadı.",
            "Beklenmeyen bir durum oluştu ve işlem tamamlanmadı.",
            [RetryStep, ReferenceStep, ContactAdminStep],
            retryable: true, requiresRefresh: true)
    };

    private static UiProblem Build(
        UiProblemKind kind,
        string code,
        string title,
        string explanation,
        IReadOnlyList<string> nextSteps,
        bool retryable,
        bool requiresRefresh) =>
        new(kind, code, title, explanation, nextSteps, retryable, requiresRefresh, null, null, null);
}
