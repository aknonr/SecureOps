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
            "groups" =>
            [
                new("new-group", "Kendi grubunuzu oluşturun", "Grup, yalnızca size ait sıralı bir bağlantı koleksiyonudur. Yeni grup ile bir ad belirleyin; bağlantıları Uygulama Bağlantıları sayfasından ekleyin.", "resources", "Bağlantı keşfet"),
                new("group-list", "Tercih ettiğiniz grubu seçin", "Soldan bir grup seçin. Varsayılan grup tercih ettiğiniz gruptur; sayfa açıldığında hiçbir sekme otomatik açılmaz.", "resources/sets", "Bağlantı Gruplarım"),
                new("group-order", "Sıralayın veya gruptan çıkarın", "Yukarı ve aşağı okları açılma sırasını değiştirir. Gruptan çıkar yalnızca seçtiğiniz bağlantıyı kaldırır; katalog kaydı etkilenmez.", "resources", "Gruba bağlantı eklemek için keşfet"),
                new("prepare-links", "Önce hazırlayın, sonra açın", "Bağlantıları hazırla güncel erişimi kontrol eder. Uygun bağlantıları seçtikten sonra Bağlantıları aç düğmesine basın. Açılmayan sekmeler için tekil Aç bağlantılarını kullanın.", "resources", "Uygulama Bağlantıları")
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
                new("favourites", "Sık kullandıklarınızı favorileyin", "Favori, tek bir bağlantıyı hızlı bulmanızı sağlar. Favorilere ekle veya Favorilerden çıkar ile seçiminizi değiştirin; Favorilerim görünümünde tekrar bulun.", "resources", "Uygulama Bağlantıları"),
                new("link-actions", "Bağlantıyı bir gruba ekleyin", "Gruba ekle ile kişisel grubunuzu seçin. Henüz grubunuz yoksa aynı pencereden Yeni grup oluşturabilirsiniz.", "resources/sets", "Bağlantı Gruplarım"),
                new("groups-link", "Bağlantıları birlikte açın", "Bağlantı Gruplarım sayfasında sırayı düzenleyin ve güncel bağlantıları hazırlayın. Varsayılan grup bir tercihtir, otomatik açma değildir.", "resources/sets", "Gruplarımı düzenle")
            ]
        };
        if (canManage && surface != "management")
        {
            steps.Add(new("management-link", "Bağlantı Yönetimi", "Yetkinizle ortak bağlantı ve kategorileri düzenleyebilirsiniz. Kişisel gruplar ve favoriler ise her zaman yalnızca sahiplerine aittir.", "admin/resources", "Bağlantı Yönetimi'ne git"));
        }
        return steps;
    }
}
