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
        "Kapsamınızdaki bütün servis hesaplarını bulduğunuz ve açtığınız ana sayfa. Üstte size ve ekibinize düşen işler, altta filtrelenebilir liste vardır.",
        [
            ("Bul", "Hesap adı/SID, durum, kurum, ekip, domain veya vade ile filtreleyin; \"Yalnız ekibimin işleri\" ile kendi işlerinizi süzün. Yetkiniz varsa dizinde ada göre arama ile kişiden hesaba gidebilirsiniz."),
            ("Aç", "Hesaba tıklayın: sahibi, kullanım yerleri, açık talepler ve geçmişi tek sayfada görünür."),
            ("İşle", "Hesap sayfasında talep açın veya yapılan işlemi bildirin."),
            ("Takip et", "Geciken ve tarih bekleyen işler listede ve \"Ekibimin işleri\"nde öne çıkar; raporlara otomatik yansır.")
        ],
        "Kapsamınız dışındaki hesapları göstermez; " + _noServerChange);

    /// <summary>Account detail.</summary>
    public static Guide Detail { get; } = new("Servis hesabı",
        "Bir servis hesabının bütün takibi: kimin sorumluluğunda olduğu, nerede kullanıldığı, hangi işin beklendiği (parola değişikliği, gMSA dönüşümü, silme, inceleme) ve yapılanların kanıtı.",
        [
            ("Sahipliği kontrol et", "\"Sahiplik\" sekmesinde sorumlu ekip ve kişi; eksikse öneri veya teyit talebi."),
            ("Kullanım yerlerini gir", "\"Kullanım ve kural\" sekmesine veritabanı, IIS uygulama havuzu, Windows servisi gibi kullanım yerlerini ekleyin; bilgi bankası kuralı önerilen yolu (ör. gMSA) gerekçesiyle gösterir."),
            ("Taramayı bağla", "Sunucularda kendi yetkinizle çalıştırdığınız taramanın dosyasını \"Kullanım taraması\" sekmesine yükleyin; bulunan bileşenleri tek tek kullanım kaydına alın veya gerekçeyle kayda almayın. gMSA dönüşümünden sonra yapılan kontrol taraması kanıttır, doğrulama değildir."),
            ("Talep aç ve planla", "Beklenen işi talep olarak açın, muhatap ekibi ve plan tarihini girin."),
            ("İşlemi bildir", "İş sunucuda yapıldıktan sonra (sistem dışında, değişiklik kaydıyla) burada \"gerçekleşti\" olarak bildirin ve kanıt ekleyin."),
            ("Doğrula ve kapat", "Doğrulama yetkisi olan kişi kanıtı inceleyip onaylar; silme veya gMSA dönüşümü doğrulanınca hesap kapanır.")
        ],
        _noServerChange + " gMSA dönüşümünü sunucuda ekip yapar; sistem planı, kanıtı ve doğrulamayı tutar.");

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
        "Excel listelerini sisteme alır: ilk kurulumda mevcut takip çalışma kitabı, sonra her hafta gelen liste. Hiçbir satır siz onaylamadan yazılmaz.",
        [
            ("Dosya ve tür", "Aşağıdaki kartlardan doğru türü seçin, dosyayı ve rapor tarihini verin."),
            ("Önizleme", "Sistem dosyayı güvenli okuyucuyla açar ve ne değişeceğini sınıflar: yeni, güncellenen, aynı, çakışan, geçersiz."),
            ("Karar", "Çakışan satırlar için karar verin (ör. sahip ekip farklıysa hangisi geçerli)."),
            ("Onay", "Tek işlemde yazılır; aynı dosya ikinci kez kopya oluşturmaz."),
            ("Sonuç", "Raporlar ve hesap sayfaları güncellenir; aktarım geçmişte kalır.")
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
}
