namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// Operator guide texts for the Service Accounts pages (rendered by SaPageGuide). Kept in one place so the wording stays
/// consistent and honest: what each page is for, the order of work, and what the page never does.
/// </summary>
public static class ServiceAccountGuides
{
    /// <summary>One page or tab guide.</summary>
    /// <param name="Title">Page or tab name.</param>
    /// <param name="Purpose">What it is for.</param>
    /// <param name="Steps">Flow in order.</param>
    /// <param name="Never">What it never does.</param>
    public sealed record Guide(string Title, string Purpose, IReadOnlyList<(string Title, string Text)> Steps, string Never);

    private const string _noServerChange = "Sunucuda, Active Directory'de veya PAM'de değişiklik yapmaz; parola okumaz.";

    /// <summary>Account list.</summary>
    public static Guide List { get; } = new("Servis Hesapları",
        "Kapsamınızdaki servis hesaplarını bulup açtığınız ana sayfa. Ekrandaki alanları tek tek görmek için \"Adım adım göster\"e basın.",
        [
            ("Gör", "Üstte size ve ekibinize düşen işler."),
            ("Bul", "Süzgeç seçip Listele'ye basın (SID aranmaz); özet düğmeleri listeyi hemen açar."),
            ("Aç", "Hesaba tıklayın; iş hesap sayfasında yapılır.")
        ],
        "Kapsamınız dışındaki hesapları göstermez; " + _noServerChange);

    /// <summary>Account detail.</summary>
    public static Guide Detail { get; } = new("Servis hesabı",
        "Bir servis hesabının bütün takibi: sorumlusu, kullanım yeri, beklenen iş ve kanıtı. Alanları tek tek görmek için \"Adım adım göster\"e basın.",
        [
            ("Bak", "Özet ve Sıradaki adım ne beklendiğini söyler."),
            ("Bildir", "Bir gelişme olduysa \"Bir gelişme mi oldu?\"ya basın."),
            ("İşle", "Talep ve işlem bildirimi İşler sekmesinde."),
            ("Doğrula", "Doğrulama yetkisi olan kişi kanıtla onaylar.")
        ],
        _noServerChange + " gMSA dönüşümünü sunucuda ekip yapar; sistem planı, kanıtı ve doğrulamayı tutar.");

    /// <summary>
    /// "Sizin için" line of the account guide (EVENT-GUIDANCE-DESIGN.md §9), from the server's permissions on this account. Short, in
    /// the owner's words; the event sentence changes when the event flow is not installed (migration 036).
    /// </summary>
    public static string DetailRole(SecureOps.Shared.Contracts.ServiceAccounts.AccountPermissions permissions, bool eventsAvailable)
    {
        const string verify = "Bildirilen işlemler doğrulama bekler; doğrulamak için tarih, doğrulayan ve kanıt gerekir. \"Bir gelişme mi oldu?\" bildirim kaydeder, doğrulamaz.";
        if (permissions.Work)
        {
            string work = eventsAvailable
                ? "Hesapta bir şey olduysa \"Bir gelişme mi oldu?\"ya basın, ne olduğunu seçin. Ürün hangi kayıtların yapılacağını gösterir; siz onaylayınca hepsi birlikte kaydedilir. Kaydedilen şey \"kişi bildirdi\"dir; doğrulama ayrıdır."
                : "Hesapta bir şey olduysa yazışmayı, işlem bildirimini ve talebi İşler ve Yazışmalar sekmelerinden kaydedin; \"Bir gelişme mi oldu?\" bu ortamda henüz açık değil. Kaydedilen şey \"kişi bildirdi\"dir; doğrulama ayrıdır.";
            return permissions.Verify ? work + " " + verify : work;
        }

        if (permissions.CanWorkAnyRequest)
        {
            return "Size atanmış talep için gelişmeyi bildirebilirsiniz. Hesabın sahipliğini ve diğer talepleri değiştiremezsiniz.";
        }

        return permissions.Verify ? verify : "Bu hesapta kayıt yapamazsınız; gelişmeleri sorumlu ekibe iletin.";
    }

    /// <summary>Team work.</summary>
    public static Guide Work { get; } = new("Ekibimin işleri",
        "Size veya ekibinize atanmış açık talepler, yaklaşan ve geciken planlar ile hatırlatmalar.",
        [
            ("Gör", "Takibinizdeki işler muhatap ekip ve tarihe göre listelenir."),
            ("Aç", "İşe tıklayıp hesap sayfasında planı güncelleyin veya işlemi bildirin."),
            ("Hatırlat", "Koordinatör taslakları hazırlanır; kopyalayıp e-postanızdan gönderirsiniz.")
        ],
        "E-posta göndermez; " + _noServerChange);

    /// <summary>Imports.</summary>
    public static Guide Imports { get; } = new("İçe aktarım",
        "Excel listelerini sisteme alır. Hiçbir satır siz onaylamadan yazılmaz. Alanları tek tek görmek için \"Adım adım göster\"e basın.",
        [
            ("Dosya", "Türü, dosyayı ve rapor tarihini verin."),
            ("Önizleme", "Ne değişeceğini yazmadan görün."),
            ("Karar", "Çakışan satırlar için karar verin."),
            ("Onay", "Tek işlemde yazılır; aynı dosya kopya oluşturmaz.")
        ],
        "Excel'deki formül veya makroları çalıştırmaz; listede olmayan hesabı silmez veya kapatmaz.");

    /// <summary>Reports.</summary>
    public static Guide Reports { get; } = new("Raporlar",
        "Yönetime ve ekiplere giden haftalık/aylık rapor. Canlı rapor her açılışta güncel veriden hesaplanır; gönderilecek rapor \"nüsha\" olarak dondurulur.",
        [
            ("Canlı raporu incele", "Dönemi ve kapsamı seçin; direktörlük görünümü, gMSA hunisi, trend ve risk adayları görünür."),
            ("Nüsha kaydet", "Göndereceğiniz anı kaydedin; nüsha sonradan değişmez, kim oluşturduğu yazılır."),
            ("Excel / PDF indir", "Excel'in ilk sayfası yönetici özetidir (gösterge kutuları, gMSA, ekipler, yaklaşan planlar); detay sayfalar arkadan gelir."),
            ("Karşılaştır", "İki nüshayı karşılaştırıp haftalık değişimi görün.")
        ],
        "Kişi bazlı performans sıralaması yapmaz; " + _noServerChange);

    /// <summary>Admin: scope grants.</summary>
    public static Guide AdminScope { get; } = new("Kapsam yetkileri",
        "Kimin hangi kurumların servis hesaplarını göreceğini belirler. Uygulamaya giriş ve işlem yetkisi ayrıdır (Erişim Talepleri / Kullanıcılar); burası yalnız görünürlük alanıdır.",
        [
            ("Kişiyi seç", "Listede yalnız uygulamaya onaylanmış kullanıcılar vardır."),
            ("Kapsamı seç", "Tüm kurum, bir kurum (alt kurumlarıyla) veya tek ekip."),
            ("Gerekçe yaz", "Neden verildiği denetim kaydına yazılır."),
            ("Gerekirse geri al", "Geri alınan yetki geçmişte kalır, silinmez.")
        ],
        "Kendinize kapsam veremezsiniz (yalnız modülün ilk kurulumunda, bir kez); kapsam uygulamaya giriş yetkisi vermez.");

    /// <summary>Admin: organizations and teams.</summary>
    public static Guide AdminOrganizations { get; } = new("Kurum ve ekipler",
        "Raporlardaki direktörlük ve ekip adlarının sözlüğü. İçe aktarımda Excel'deki \"Organizasyon\" ve \"Ekip\" değerlerinden otomatik oluşur (geçici); burada adı düzeltilir, üst kuruma bağlanır ve onaylanır.",
        [
            ("Kontrol et", "İçe aktarımdan gelen \"Geçici\" ekipleri gözden geçirin."),
            ("Düzelt", "Yazım farklarını düzeltin, ekibi doğru kuruma bağlayın."),
            ("Kullan", "Kapsam verirken ve raporlarda bu adlar kullanılır.")
        ],
        "Ekip veya kurum kaydı kimseye yetki vermez.");

    /// <summary>Admin: gMSA routing.</summary>
    public static Guide AdminRouting { get; } = new("gMSA yönlendirme",
        "1 Ekim 2026 kararının ayarı: hangi ekiplerin \"SQL ekibi\" olduğu ve gMSA işini hangi ekibin yürüttüğü (WASAS). Ayar varsa DBA listesi aktarılırken SQL ekibi hesapları için yürütücü ekibe otomatik gMSA değerlendirme talebi açılır.",
        [
            ("SQL ekiplerini işaretle", "Veritabanı ekiplerini \"SQL ekibi\" olarak ekleyin."),
            ("Yürütücüyü seç", "gMSA işini yapan tek ekibi (WASAS) \"yürütücü\" yapın."),
            ("İçe aktar", "DBA listesi önizlemesinde \"gMSA yönlendirme\" satırları görünür; onayla talepler açılır.")
        ],
        "Sahipliği, uygunluğu veya devir kabulünü değiştirmez; erişim vermez.");

    /// <summary>Admin: people.</summary>
    public static Guide AdminPeople { get; } = new("Kişiler",
        "Hesap sorumlusu olarak geçen kişilerin standart kaydı. Excel'deki farklı yazımlar (ör. Türkçe karakterli/karaktersiz) tek kişide birleştirilir; böylece raporlarda kişi sayısı doğru çıkar.",
        [
            ("Ara", "Kişiyi adıyla bulun."),
            ("Doğrula", "Kişinin kurumsal kimliğini kanıtıyla kaydedin."),
            ("Birleştir", "Farklı yazımları takma ad olarak ekleyin.")
        ],
        "Kişi kaydı uygulamaya erişim veya yetki vermez; dizinde arama yapmaz.");

    /// <summary>gMSA conversion plans (list).</summary>
    public static Guide Plans { get; } = new("Değişiklik planları",
        "gMSA'ya geçiş planları: hesaplar, son taramadaki bileşenler, onay ve kontrol listesi. Değişikliği siz sunucuda kendi yetkinizle yaparsınız. Alanları tek tek görmek için \"Adım adım göster\"e basın.",
        [
            ("Hazırla", "Hesapları ve hedef gMSA adlarını seçin."),
            ("Önizle ve onaylat", "Başka bir doğrulama yetkilisi onaylar."),
            ("Uygula ve doğrula", "İşaretleyin, sonra kontrol taramasıyla doğrulayın.")
        ],
        "Yalnız tüm hesapları kapsamınızda olan planları listeler. " + _noServerChange + " Parola alanı yoktur; parola değişikliği PAM'ın işidir.");

    /// <summary>One gMSA conversion plan.</summary>
    public static Guide PlanDetail { get; } = new("Değişiklik planı",
        "Planın hangi adımda olduğunu, neyin eksik olduğunu ve kimin ne yapabileceğini gösterir. Alanları tek tek görmek için \"Adım adım göster\"e basın.",
        [
            ("Plan ve önizleme", "Hesaplar, hedef adlar ve son taramadaki bileşenler."),
            ("Onay", "Planı hazırlamayan bir doğrulama yetkilisi onaylar."),
            ("Kontrol listesi", "Yapıldı işareti beyandır, doğrulama değildir."),
            ("Doğrulama", "Kontrol taraması ve hesap sayfasında ayrıca yapılır.")
        ],
        _noServerChange + " Planı kapatmak bağlı talebi kapatmaz.");
}
