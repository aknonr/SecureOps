using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// On-screen tours of the Service Accounts pages (docs/service-accounts/GUIDED-TOUR-DESIGN.md §4). Each step names the
/// <c>data-tour</c> area it points at, says in one sentence what the area is and in one why it matters. Whether a step is shown comes
/// only from the server's answer (capabilities in the access snapshot, or the record's own permissions); the tour grants nothing. The
/// report page has no tour (parallel report work).
/// </summary>
public static class ServiceAccountTours
{
    /// <summary>Account list (<c>/service-accounts</c>).</summary>
    /// <param name="work">ServiceAccounts.Work: selecting accounts for one mail or scan record.</param>
    /// <param name="assign">ServiceAccounts.Assign: registering a new account.</param>
    /// <param name="identityLookup">Identity.Lookup: directory name search.</param>
    public static IReadOnlyList<SoTourStep> List(bool work, bool assign, bool identityLookup) =>
    [
        new("sa-work-summary", "Takibinizdeki işler",
            "Size ve ekibinize düşen açık, geciken ve karar bekleyen işler burada sayılır; düğmeler listeyi o süzgeçle açar.",
            "Bir şeyi değiştirmeden önce sizi bekleyen işi görmeniz için en üstte durur."),
        new("sa-directory-search", "Dizinde ada göre ara",
            "Bir kişiyi dizinde adıyla bulur ve kapsamınızdaki servis hesabına bağlıysa oraya götürür.",
            "Aşağıdaki hesap araması kişi aramaz; ikisi ayrı aramalardır.", identityLookup),
        new("sa-filters", "Süzgeçler",
            "Hesap adının bir parçası, durum, kurum, ekip, domain veya vade seçip Listele'ye basın.",
            "Süzgeçler Listele'ye basana kadar uygulanmaz; böylece birkaç süzgeci birlikte ayarlayabilirsiniz."),
        new("sa-create", "Yeni servis hesabı kaydı",
            "Listede olmayan bir hesabı kurum ve adıyla kayda alırsınız.",
            "Takip yalnız kayıtlı hesaplar için yapılır; kayıt dizinde veya sunucuda hiçbir şeyi değiştirmez.", assign),
        new("sa-results", "Hesap listesi",
            "Hesabın adına tıklayınca sahibi, kullanım yerleri, açık işleri ve geçmişi tek sayfada açılır.",
            "Talep, işlem bildirimi ve doğrulama hesap sayfasında yapılır."),
        new("sa-select", "Birden çok hesap seçme",
            "Satırların başındaki kutuları işaretleyip tek e-posta kaydını veya tek tarama dosyasını hepsine bağlayın.",
            "Aynı yazışmayı veya taramayı hesap hesap tekrar girmemeniz için.", work),
        new("sa-module-links", "Diğer bölümler",
            "Ekibimin işleri, içe aktarım, değişiklik planları ve raporlar buradan açılır; yalnız yetkiniz olanlar görünür.",
            "Hatırlatmalar, Excel aktarımı ve gMSA planları ayrı sayfalardadır."),
    ];

    /// <summary>Account detail (<c>/service-accounts/{id}</c>).</summary>
    /// <param name="permissions">The server's permissions on this account.</param>
    /// <param name="eventsAvailable">Whether the event flow is installed (migration 036).</param>
    public static IReadOnlyList<SoTourStep> Detail(AccountPermissions permissions, bool eventsAvailable)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return
        [
            new("sa-summary", "Özet",
                "Hesabın rapor kurumu, sahip ekibi ve kişisi, açık işi ve kaynakta son görülmesi burada.",
                "Bir şey kaydetmeden önce kimin sorumlu olduğunu ve neyin beklendiğini görmeniz için."),
            new("sa-event", "Bir gelişme mi oldu?",
                "Mail, telefon veya toplantıda öğrendiğiniz gelişmeyi buradan seçip bildirirsiniz.",
                "Ürün hangi kayıtların yazılacağını önce gösterir, siz onaylayınca hepsi birlikte kaydedilir; kaydedilen \"kişi bildirdi\"dir, doğrulama değildir.",
                eventsAvailable && (permissions.Work || permissions.CanWorkAnyRequest)),
            new("sa-next", "Sıradaki adım",
                "Bu hesapta bekleyen işlerin en önemlisi en üstte; düğmesi ilgili sekmeyi açar.",
                "Nereden başlayacağınızı aramadan bulmanız için."),
            new("sa-tab-work", "İşler: talep ve plan",
                "Açık talepler, muhatap ekip, plan tarihi ve bildirilen işlemler bu sekmede.",
                "Beklenen her iş (parola değişikliği, gMSA dönüşümü, silme) bir talep olarak takip edilir ve raporlara buradan yansır."),
            new("sa-tab-work", "Doğrulama",
                "Bildirilen işlemi aynı sekmede tarih, doğrulayan ve kanıtla doğrularsınız.",
                "Hesap ancak doğrulanmış silme veya gMSA dönüşümüyle kapanır; bildirim tek başına yetmez.", permissions.Verify),
            new("sa-tab-usage", "Hangi sunucularda?",
                "\"Kullanım ve kural\" sekmesi hesabın hangi sunucuda, hangi bileşende kullanıldığını ve kuralın önerisini gösterir.",
                "Parola değişikliği veya gMSA geçişinden önce neyin etkileneceğini bilmek için; \"bulunamadı\" kullanılmıyor demek değildir."),
            new("sa-tab-history", "Kaynak ve geçmiş",
                "Hesabın hangi listeden geldiği ve kimin neyi ne zaman kaydettiği burada.",
                "Bir bilginin nereden geldiğini sorduğunuzda kanıt bu kayıttır."),
        ];
    }

    /// <summary>Imports (<c>/service-accounts/imports</c>); the page itself requires ServiceAccounts.Import.</summary>
    public static IReadOnlyList<SoTourStep> Imports { get; } =
    [
        new("sa-import-steps", "Aktarım adımları",
            "Dört adım: dosya, önizleme, karar, onay; bulunduğunuz adım işaretlidir.",
            "Siz onaylamadan hiçbir satır yazılmaz; hangi adımda olduğunuzu buradan görürsünüz."),
        new("sa-import-kinds", "Dosya türü",
            "Elinizdeki dosyaya uyan kartı seçin; her kart hangi dosya için olduğunu söyler.",
            "Her tür kendi sütunlarıyla okunur; yanlış tür yanlış eşleme demektir."),
        new("sa-import-source", "Dosya ve rapor tarihi",
            "Dosyayı, kaynak rapor tarihini ve bu tarihi nereden bildiğinizi girin.",
            "Hesabın son bilgisi bu tarihe göre sıralanır; yükleme günü kullanılmaz."),
        new("sa-import-stage", "Önizleme oluştur",
            "Dosya güvenli okuyucuyla açılır ve satırlar yeni, güncellenen, aynı, çakışan veya geçersiz olarak sınıflanır.",
            "Hiçbir şey yazılmadan neyin değişeceğini görürsünüz."),
        new("sa-import-rows", "Satırlar ve kararlar",
            "Çakışan satırlar için hangi bilginin geçerli olduğuna karar verin.",
            "Ürün çakışmayı kendisi çözmez; karar sizindir ve kayda geçer."),
        new("sa-import-commit", "Aktarımı onayla",
            "Kararlar tamamlanınca bütün satırlar tek işlemde yazılır.",
            "Aynı dosya ikinci kez kopya oluşturmaz; listede olmayan hesap silinmez veya kapanmaz."),
        new("sa-import-history", "Geçmiş aktarımlar",
            "Önceki aktarımlar tarih, tür ve sonuçlarıyla burada; Aç ile ayrıntısına bakılır.",
            "Bir bilginin hangi dosyadan geldiğini buradan izlersiniz."),
    ];

    /// <summary>Change plans list (<c>/service-accounts/plans</c>).</summary>
    /// <param name="work">ServiceAccounts.Work: preparing a new plan.</param>
    public static IReadOnlyList<SoTourStep> Plans(bool work) =>
    [
        new("sa-plan-create", "Yeni plan hazırla",
            "1–20 hesap seçip her biri için hedef gMSA adını yazın ve taslak olarak kaydedin.",
            "Plan, değişiklikten önce neyin etkileneceğini ve kimin onayladığını tek yerde tutar.", work),
        new("sa-plans-filter", "Planlar ve süzgeç",
            "Durum seçip Listele'ye basın; yalnız bütün hesapları kapsamınızda olan planlar görünür.",
            "Bir planı göremiyorsanız hesaplarından biri kapsamınız dışında olabilir; bu, plan yok demek değildir."),
        new("sa-plans-list", "Plana gir",
            "Planın adına tıklayınca adımları, önizlemesi, onayı ve kontrol listesi açılır.",
            "Önizleme, onay ve işaretleme plan sayfasında yapılır."),
    ];

    /// <summary>One change plan (<c>/service-accounts/plans/{id}</c>); every command is still decided by the server.</summary>
    public static IReadOnlyList<SoTourStep> PlanDetail { get; } =
    [
        new("sa-plan-steps", "Planın adımı",
            "Planın hangi adımda olduğunu ve sıradakini gösterir.",
            "Her adımın sonucu sunucunun kaydıdır; ekran sunucunun yapmadığı bir şeyi göstermez."),
        new("sa-plan-plan", "Hesaplar ve hedef adlar",
            "Plandaki hesaplar ve her birinin hedef gMSA adı.",
            "Önizlemeden sonra değiştirirseniz plan taslağa döner ve önizleme geçersiz olur."),
        new("sa-plan-preview", "Önizleme",
            "Her hesabın son taramasındaki bileşenler satır satır listelenir.",
            "\"Bulunamadı\" kullanılmıyor demek değildir; \"bilgi yok\" taramanın o hesabı kapsamadığını söyler."),
        new("sa-plan-approval", "Onay",
            "Planı hazırlamayan bir doğrulama yetkilisi bu önizlemeyi OCO numarası ve bakım penceresiyle onaylar.",
            "Onay belirli bir önizleme sürümüne bağlıdır; plan değişirse onay da geçersiz olur."),
        new("sa-plan-checklist", "Kontrol listesi",
            "Değişikliği sunucuda ekip yapar; burada her bileşen için yapıldı işareti ve kanıt kaydedilir.",
            "İşaret kişinin beyanıdır, doğrulama değildir; kim işaretlediği yazılır."),
        new("sa-plan-verification", "Doğrulama",
            "Değişiklikten sonra gMSA kontrol taraması ve hesap sayfasındaki doğrulama ayrıca yapılır.",
            "Planın kapanması hesabı doğrulanmış saymaz ve bağlı talebi kapatmaz."),
    ];
}
