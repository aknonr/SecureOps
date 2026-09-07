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
                new("prepare-links", "Önce denetleyin, sonra açın", "Üstteki Bağlantıları aç güncel erişimi denetler. Hazırlanan paneldeki yerel açma düğmesine kendiniz basın. Açılmayan sekmeler için tekil Aç bağlantılarını kullanın.", "resources", "Uygulama Bağlantıları")
            ],
            "management" when canManage =>
            [
                new("management-tabs", "Bağlantıları ve kategorileri yönetin", "Bağlantılar görünümü uygulama adreslerini, Kategoriler görünümü düzeni ve görünürlük kapsamını yönetir. Bu alan ayrıca atanmış yönetim yetkisi gerektirir.", "resources", "Kullanıcı görünümüne dön"),
                new("new-entry", "Bir kayıt yayımlayın", "Önce bir kategori oluşturun. Bağlantı için anlaşılır bir ad, HTTPS adresi ve kısa amaç girin. Erişim notlarına parola veya kimlik bilgisi yazmayın.", "admin/resources", "Bağlantı Yönetimi"),
                new("archive-filter", "Arşivi ve değişiklikleri kontrol edin", "Arşivlenenler normal listelerde görünmez, kişisel seçimler korunur. Bir düzenleme çakışırsa taslağınız açık kalır; güncel kaydı incelemeden yeniden kaydedilmez.", "resources/sets", "Kişisel gruplarım")
            ],
            _ =>
            [
                new("resource-search", "Bağlantıyı bulun ve açın", "Ad, amaç veya etiketle arayın. Kategori ve ortamla daraltın. Aç düğmesi hedefi yeni sekmede açmayı dener; hedef uygulamanın kendi oturumu gerekir.", "resources", "Uygulama Bağlantıları"),
                new("link-actions", "Görünür bağlantıları seçin", "Kutularla seçin. Bu sayfadakileri seç yalnızca yüklü sayfayı kapsar; filtre veya sayfa değişirse seçim temizlenir. Yıldız tekil favoridir; grup üyeliğinden bağımsızdır.", "resources", "Uygulama Bağlantıları"),
                new("selection-actions", "Seçiminizle işlem yapın", "Açmak için hazırla güncel erişimi denetler; ardından açma düğmesine kendiniz basın. Gruba ekle seçiminizi kişisel gruba kaydeder. Sekme açılması oturum açıldığını garanti etmez.", "resources/sets", "Bağlantı Gruplarım"),
                new("personalize", "Çalışma alanınızı düzenleyin", "Bu düğmeden görünüm, yoğunluk, sayfa boyutu ve kısayollarınızı düzenleyin. Açılan pencerede kişiselleştirme rehberini başlatabilirsiniz.", "resources", "Çalışma alanı")
            ]
        };
        if (canManage && surface != "management")
        {
            steps.Add(new("management-link", "Bağlantı Yönetimi", "Yetkinizle ortak bağlantı ve kategorileri düzenleyebilirsiniz. Kişisel gruplar ve favoriler ise her zaman yalnızca sahiplerine aittir.", "admin/resources", "Bağlantı Yönetimi'ne git"));
        }
        return steps;
    }
}
