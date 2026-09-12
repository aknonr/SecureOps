namespace SecureOps.Ui.Services;

/// <summary>A read-only instruction; targets are optional and never activated by the tour.</summary>
public sealed record ResourceGuideStep(string Target, string Title, string Description, string Href, string LinkLabel);

/// <summary>Task guidance scoped to the current screen and server-reported management capability.</summary>
public static class ResourceGuideSteps
{
    /// <summary>Builds guidance without granting authority or inventing unavailable controls.</summary>
    public static IReadOnlyList<ResourceGuideStep> For(string surface, bool canManage)
    {
        List<ResourceGuideStep> steps = surface switch
        {
            "announcements" =>
            [
                new("announcement-new", "Yeni taslak", "Yeni duyuru ile başlayın veya listeden kayıtlı taslağınızı açın.", "", ""),
                new("announcement-details", "Bilgiler ve alıcılar", "Konuyu, çalışma zamanını ve alıcıları girin. İsteğe bağlı ayrıntılar alttadır.", "", ""),
                new("announcement-save", "Kaydedin", "Kaydet ile taslağınızı saklayın. Eksik alanları daha sonra tamamlayabilirsiniz.", "", ""),
                new("announcement-preview", "Önizleyin", "Değişiklikleri kaydettikten sonra Önizle ile son kayıtlı içeriği kontrol edin.", "", ""),
                new("announcement-download", "Maili indirin", "Maili indir, .eml dosyasını bilgisayarınıza indirir; kimseye mail göndermez.", "", "")
            ],
            "inuse-list" =>
            [
                new("inuse-filter", "Kayıtları bulun", "Tümü, bana atanan ve atanmamış görünümleri kayıtlı veriyi kullanır.", "", ""),
                new("inuse-refresh", "Kaynak durumunu kontrol edin", "Son başarılı okumayı ve eksik kapsam uyarısını kontrol edin. Yenileme yalnızca açık komutla başlar.", "", ""),
                new("inuse-list", "İncelemeye geçin", "Kaydı açarak sorumlu atamasını, kaynak kanıtını ve sunucu cevaplarını inceleyin.", "", "")
            ],
            "inuse-review" =>
            [
                new("inuse-owner", "Sorumluları ayırın", "İlgili talebi bildiren, her sunucunun RFC kaydından gelir; sahiplik değildir. WASAS inceleyicisi isteğe bağlıdır.", "", ""),
                new("inuse-servers", "Her sunucuyu doğrulayın", "Bilinmeyen cevapları doğrulanmış saymayın. Toplu cevapta seçili sunucuları ve farklı ortamları ayrıca kontrol edin.", "", ""),
                new("inuse-report", "Taslağı kaydedin", "Önizleme kayıtlı veri sürümüne bağlıdır. Kaynak değişmişse kaydı yeniden okuyup inceleyin. Bu adımlar kaynak kaydı kapatmaz.", "", "")
            ],
            "personalize" =>
            [
                new("layout-fields", "Görünümü seçin", "Kart veya liste, yoğunluk ve sayfa boyutu kişisel tercihinizdir. Rehber seçimlerinizi değiştirmez.", "resources", ""),
                new("layout-shortcuts", "Kısayolları düzenleyin", "İşaretli kısayollar görünür. Oklarla sıralayın; yalnızca mevcut yetkinizdeki alanları sabitleyebilirsiniz.", "resources", ""),
                new("layout-save", "Kaydedin veya sıfırlayın", "Kaydet kişisel düzeninizi saklar. Düzeni sıfırla yalnızca bu tercihleri varsayılana döndürür; gruplar ve favoriler korunur.", "resources", "")
            ],
            "review" =>
            [
                new("review-summary", "Kaynağı anlayın", "Başlık ve açıklamayı kontrol edin. Jira oluşturulması, kaynak kaydın kapatıldığı anlamına gelmez.", "operational-records", ""),
                new("review-type", "Türü doğrulayın", "Yalnızca doğrulanmış iki tür için beyan seçin. Sunucu emekliliği gibi diğer talepleri zorlamayın; seçim onay değildir.", "operational-records", ""),
                new("review-evidence", "Engelleri çözümleyin", "Sunucunun bildirdiği eksik kanıtları ve uygunluk engellerini inceleyin. Rehber değerlendirme veya yayımlama başlatmaz.", "operational-records", ""),
                new("review-preview", "Önizleme ve sonraki adım", "Taslakta yalnızca desteklenen alanları kontrol edin. Yazma kapalıysa yayın yapılmaz. Belirsiz sonuçta yeniden oluşturmayın; mutabakat gerekir.", "operational-records", "")
            ],
            "groups" =>
            [
                new("new-group", "Kendi grubunuzu oluşturun", "Grup, yalnızca size ait sıralı bir bağlantı koleksiyonudur. Yeni grup ile bir ad belirleyin; bağlantıları Uygulama Bağlantıları sayfasından ekleyin.", "resources", "Bağlantı keşfet"),
                new("group-list", "Tercih ettiğiniz grubu seçin", "Soldan bir grup seçin. Varsayılan grup tercih ettiğiniz gruptur; sayfa açıldığında hiçbir sekme otomatik açılmaz.", "resources/sets", "Bağlantı Gruplarım"),
                new("group-order", "Sıralayın veya gruptan çıkarın", "Yukarı ve aşağı okları açılma sırasını değiştirir. Gruptan çıkar yalnızca seçtiğiniz bağlantıyı kaldırır; katalog kaydı etkilenmez.", "resources", "Gruba bağlantı eklemek için keşfet"),
                new("prepare-links", "Önce denetleyin, sonra açın", "Açmak için hazırla güncel erişimi denetler. Hazırlanan paneldeki açma düğmesine kendiniz basın. Açılmayan sekmeler için tekil Aç bağlantılarını kullanın.", "resources", "Uygulama Bağlantıları")
            ],
            "management" when canManage =>
            [
                new("management-tabs", "Bağlantıları ve kategorileri yönetin", "Bağlantılar görünümü uygulama adreslerini, Kategoriler görünümü düzeni ve görünürlük kapsamını yönetir. Bu alan ayrıca atanmış yönetim yetkisi gerektirir.", "resources", "Kullanıcı görünümüne dön"),
                new("new-entry", "Bir kayıt yayımlayın", "Önce bir kategori oluşturun. Bağlantı için anlaşılır bir ad, HTTPS adresi ve kısa amaç girin. Erişim notlarına parola veya kimlik bilgisi yazmayın.", "admin/resources", "Bağlantı Yönetimi"),
                new("archive-filter", "Arşivi ve değişiklikleri kontrol edin", "Arşivlenenler normal listelerde görünmez, kişisel seçimler korunur. Bir düzenleme çakışırsa taslağınız açık kalır; güncel kaydı incelemeden yeniden kaydedilmez.", "resources/sets", "Kişisel gruplarım")
            ],
            _ =>
            [
                new("link-actions", "Bağlantıları seçin", "Kutularla seçin. Bu sayfadakileri seç yalnızca yüklü sayfayı kapsar; filtre veya sayfa değişirse seçim temizlenir.", "resources", "Uygulama Bağlantıları"),
                new("selection-actions", "Bağlantıları açın", "Açmak için hazırla güncel erişimi denetler. Ardından açma düğmesine basın. Tarayıcı sekmeleri engellerse tekil Aç bağlantılarını kullanın; rehber hiçbir bağlantıyı açmaz.", "resources", "Uygulama Bağlantıları"),
                new("save-personal-group", "Kişisel grubunuza kaydedin", "Seçilenleri grubuma kaydet ile mevcut grubu seçin veya yeni bir ad verin. Yönetici olmanız gerekmez; grup erişim yetkisini değiştirmez.", "resources", "Uygulama Bağlantıları"),
                new("groups-link", "Grubunuzu yeniden açın", "Kişisel bağlantı gruplarım sayfasında grubunuzu seçin, gerekirse sıralayın ve Açmak için hazırla ile devam edin. Hiçbir sekme otomatik açılmaz.", "resources/sets", "Kişisel bağlantı gruplarım")
            ]
        };
        if (canManage && surface != "management")
        {
            steps.Add(new("management-link", "Bağlantı Yönetimi", "Yetkinizle ortak bağlantı ve kategorileri düzenleyebilirsiniz. Kişisel gruplar ve favoriler ise her zaman yalnızca sahiplerine aittir.", "admin/resources", "Bağlantı Yönetimi'ne git"));
        }
        return steps;
    }
}
