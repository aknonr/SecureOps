namespace SecureOps.Ui.Services;

/// <summary>One step of the In Use flow shown in the page guide: a short name, what to do, and what to watch for.</summary>
public sealed record InUseGuideStep(string Title, string Do, string Watch);

/// <summary>
/// Wording of the In Use "Nasıl kullanılır?" guide and its on-screen tours (SoGuidedTour). The flow follows
/// docs/inuse-v2-walkthrough-tr.md. Whether a tour step is shown comes only from the server's answer (capabilities); the guide
/// grants nothing and, for the last step, says what the server reports about the completion gate instead of promising it.
/// </summary>
public static class InUseTours
{
    /// <summary>What the page never does, in one sentence.</summary>
    public const string Never = "Hedef sunuculara dokunmaz, genel OR'u kapatmaz, kaynak kaydını silmez. Tur hiçbir şeye basmaz ve kayıt yapmaz.";

    /// <summary>One line on what the page is for.</summary>
    public const string Purpose = "Talep eden OR'daki sunucuları inceler, eksik cevapları tamamlar, Excel raporunu kontrol eder ve rapor talebe eklenip WASAS adımı onaylanabildiğinde bunu yapar.";

    /// <summary>The four steps of the flow. <paramref name="completeGate"/> is the server's own explanation when the completion gate is closed, null when it is open, and "" when it is not known on this screen.</summary>
    public static IReadOnlyList<InUseGuideStep> Flow(string? completeGate) =>
    [
        new("Sunucuları incele",
            "OR'u açın. Bildireni, kaynak tarihini, her sunucunun ortamını ve servisini kontrol edin. İnceleyici atamak isteğe bağlıdır.",
            "İnceleyici atamak bildireni veya servis sahibini değiştirmez. Servis sahibi eksikse bildirenin adını yazmayın; yöneticinizden isteyin."),
        new("Eksikleri tamamla",
            "Her sunucu için internet çıkışı, internetten erişim ve mikrosegmentasyona Evet, Hayır veya Bilinmiyor deyin. Sonra Cevapları kaydet'e basın.",
            "Kanıtınız yoksa Bilinmiyor bırakın; olumsuz cevap sayılmaz. Kaydetmeden ilerleme kalıcı değildir."),
        new("Excel'i kontrol et",
            "Excel önizlemesinde sunucular ve NMS sayfalarına bakın. Hazırsa WASAS'a arşivle ve indir deyin.",
            "Eksik sahip, OS sürümü veya kontrol kanıtı uydurulmaz. Arşiv tek başına talebe eklenmiş sayılmaz."),
        new("Talebe ekle ve tamamla",
            "Raporu talebe ekleyip yalnız WASAS aktivitesini onaylayan adımdır. Genel OR açık kalabilir.",
            completeGate switch
            {
                null => "Sunucu şu an bu adıma izin veriyor. Başlatmadan önce gösterilen OR ve rapor sürümünü kontrol edin.",
                "" => "Bu ekranda durumu bilinmiyor. Kayıt sayfasında düğmenin yanında açık mı kapalı mı olduğu ve nedeni yazar.",
                _ => "Şu an kapalı. Sunucunun söylediği neden: " + completeGate
            })
    ];

    /// <summary>Tour of the list page (<c>/in-use</c>).</summary>
    /// <param name="canRefresh">InUse.Refresh: reading the source again.</param>
    public static IReadOnlyList<SoTourStep> List(bool canRefresh) =>
    [
        new("inuse-strip", "Durum şeridi",
            "Aktif, doğrulama bekleyen ve kapalı/tamamlanan kayıt sayıları. Sayıyı sunucu söyler; okunamazsa \"alınamadı\" yazar.",
            "Neyin sizi beklediğini listeyi gezmeden görmeniz için."),
        new("inuse-filters", "Kayıt bul",
            "OR numarası, sunucu veya bildiren adıyla arayın; görünümle yalnız size atananları ya da atanmamışları seçin.",
            "Kapanmış kayıtlar bu listeden düşer; onlar Kapalı / tamamlanan sayfasındadır."),
        new("inuse-refresh", "Kaynağı yenile",
            "Kaynaktan OR ve sunucu ilişkilerini yeniden okur. Son başarılı okuma zamanı yanında yazar.",
            "Okuma güvenlik kontrolü yapmaz ve kaydı kapatmaz; yalnız listeyi güncel tutar.", canRefresh),
        new("inuse-records", "Kayıt listesi",
            "Her satırda OR numarası, başlık, sunucu sayısı, durum ve sıradaki adım var. Ayrıntı bölümü bildireni ve sunucu eşlemesini açar.",
            "Satırdaki uyarı tek rozet ve tek cümledir; geri kalanı ayrıntıdadır."),
        new("inuse-next", "Sıradaki adım",
            "Satırın bir sonraki işi: eksik cevaplar, değişen kaynak ya da Excel önizlemesi. İncele ile kaydı açarsınız.",
            "Her satırda yalnız bir adım gösterilir; hepsini birden çözmeniz gerekmez."),
        new("inuse-closed", "Kapalı / tamamlanan",
            "Ürünün tamamlanmış ya da kapalı bildiği kayıtlar ayrı sayfada, ne olduğu ve kanıtı ile yazılır.",
            "Bilinmeyen durum \"tamam\" diye gösterilmez; ürün bilmiyorsa bilmiyor der.")
    ];

    /// <summary>Tour of one record (<c>/in-use/{id}</c>).</summary>
    /// <param name="canEdit">InUse.Review on a record that is not discarded.</param>
    /// <param name="canReport">InUse.Review.</param>
    /// <param name="gate">The completion step's own sentence for the tour: the server's reason when it is closed.</param>
    public static IReadOnlyList<SoTourStep> Record(bool canEdit, bool canReport, string? gate) =>
    [
        new("inuse-summary", "Kayıt özeti",
            "Atanan inceleyici, kaydedilen ilerleme, kaynak okuma zamanı ve bildiren burada.",
            "Neyin bitmiş, neyin eksik olduğunu bir bakışta görmeniz için."),
        new("inuse-servers", "1. Sunucuları incele",
            "OR'a bağlı sunucular, ortamları ve servisleri. Bir sunucunun adına basınca cevap alanına gidersiniz.",
            "Kaynak ilişkisi eksikse bu bölüm söyler; sunucu uydurulmaz."),
        new("inuse-answers", "2. Eksikleri tamamla",
            "Her sunucu için üç cevap: Evet, Hayır ya da Bilinmiyor. İlk eksik cevaba git düğmesi sizi oraya götürür.",
            "Kanıtınız yoksa Bilinmiyor bırakın; bu olumsuz cevap sayılmaz.", canEdit),
        new("inuse-save", "Cevapları kaydet",
            "Ekranın altındaki çubuk cevapları taslak olarak saklar.",
            "Kaydetmeden ilerleme kalıcı değildir; Excel önizleme de kayıtlı cevaplardan üretilir.", canEdit),
        new("inuse-report", "3. Excel'i kontrol et",
            "Önizleme ve WASAS arşivi burada. Arşivlenen dosya sonradan Rapor geçmişi sayfasından da indirilir.",
            "Arşiv tek başına talebe eklenmiş ya da onaylanmış sayılmaz.", canReport),
        new("inuse-complete", "4. Talebe ekle ve tamamla",
            "Raporu talebe ekleyip yalnız WASAS aktivitesini onaylayan adım. " + (gate is null ? "Sunucu şu an izin veriyor." : "Şu an kapalı: " + gate),
            "Düğmenin yanındaki kutu açık ya da kapalı olduğunu ve nedenini söyler; kapalıyken bu adımı yapamazsınız.", canEdit)
    ];
}
