# rc6.22 Sonrası In Use Yükseltmesi

## Sınır ve Ön Koşullar

Bu paket yerel inceleme, sunucu geçmişi ve kontrollü yürütme altyapısını günceller.
Kurumsal Turuncu Hat'a yükleme/kapatma bu teslimde kullanıma açılmaz. Normal
ortamlarda `InUseCompletion:Enabled=false` kalır. `Fixture` kurumsal sağlayıcı
değildir; sunucuya örnek veri, test rolü veya test yapılandırması taşınmaz.

rc6.22 ve mevcut arşivleri değiştirmeyin. Kurulu 001-021 ve Hangfire şema 9'u
yeniden çalıştırmayın. Bu yükseltmenin tek SQL farkı **022**'dir. DBA, mevcut
veritabanı kimliğini, 001-021 nesne tanımlarını ve yedek/geri dönüş noktasını
doğruladıktan sonra yalnız paket içindeki 022 geçişini uygular. Bu belge otomatik
uygulama veya yetki değişikliği izni değildir.

## Yapılandırma Farkı

| Bileşen / ayar | Yapılacak kontrol |
|---|---|
| API ve Worker / `InUseCompletion:Enabled` | `false`; bu teslim için açılmayacak |
| API ve Worker / `InUseCompletion:Provider` | Varsayılan `Disabled`; Fixture kullanılmayacak |
| API / `TuruncuHat:InUseAspectLookupEnabled` | Varsayılan `false`; semantik servis-unsuru yanıtı onaylanmadan açılmayacak |
| API / `InUsePolicy:Revision`, `InUsePolicy:Proposals` | Mevcut onaylı değerler korunur; sadece COUNTRY, Department, Sub_Department, Contact_email, ITMC_Event_Owner_Group, Device_Type izinlidir |
| API / `InUseReports:Directory` | Etkin özel arşiv dizini korunur; web kökü veya dağıtım klasörü olmaz |
| API / `TuruncuHat:BaseUrl`, Authorization, Username, Password, TenantId | Mevcut güvenli değerler korunur; bu belgeye veya kanıta yazılmaz |
| API ve Worker / SQL hedefi, Hangfire schema/queue | Gizli olmayan hedef parmak izleri ve kuyruk eşleşmesi kontrol edilir; `PrepareSchema=false` korunur |
| UI | Yeni In Use ayarı gerekmez; mevcut API adresi, kimlik aktarımı ve ayrı kalıcı ring korunur |

API IIS ortam değişkenleri JSON'u geçersiz kılabilir. Worker API `web.config`
dosyasını ve AppPool kimliğini devralmaz. Windows çalışma kimliği, kaynak servis
hesabı ve onaylayan kişi farklıdır. `OperationalRecords:ControlledTestWritesEnabled`
ve `SourceCloseEnabled` açılmaz; mevcut salt okunur güvenlik durumu korunur.
SMTP/OCO ayarları değiştirilmez. Branding için fark yoktur; onaylı altı dosya korunur.

## DBA ve Dosya Erişimi

022, `ops.InUseServerReviews`, `ops.InUseExecutions`, `ops.InUseExecutionEvents`
tablolarını ve koruyucu tetikleyicileri ekler. Eski veriyi silmez, kullanıcıya rol
vermez. Normal geçmiş kaydı için mevcut onaylı API çalışma hesabına yalnız
InUseServerReviews SELECT/INSERT gerekir. Tamamlama kapalı olsa da mevcut sonuçları
göstermek için API hesabına InUseExecutions ve InUseExecutionEvents SELECT gerekir.
Yürütme ayrıca yetkilendirildiğinde API/Worker için InUseExecutions INSERT/UPDATE
ve InUseExecutionEvents INSERT gerekir. Mevcut
`audit.AuditLog` INSERT ve uygulama erişim/rol okuma izinleri korunur. DBA ayrı
onaylı süreçle yalnız eksik nesne izinlerini tamamlar; db_owner, DDL veya genel
DELETE verilmez. Yeni `InUse.Complete` uygulama yetkisi hiçbir mevcut role
otomatik eklenmez. Admin etiketi tek başına bu yetkiyi sağlamaz.

API yolu `D:\Applications\api\wasasyonetimapi.thy.com`, UI yolu
`D:\Applications\ui\wasasyonetim.thy.com`, Worker yolu `D:\secureops_worker`
olarak bildirildi. Dağıtım ayrı operatör onayı gerektirir. API mevcut onaylı
çalışma hesabını, UI `ApplicationPoolIdentity` kimliğini kullanmaya devam eder;
bu çalışma sunucuda tekrar doğrulama yapmadı. Worker aynı onaylı hesabın gerçek
yerel oturumunda mevcut ön plan konsolu olarak kalır; Windows Service kurulmaz.

Yetkili yönetici mevcut operasyon tanılamasından **etkin** arşiv yolunu ve API
kimliğini kontrol eder. Arşivler bu özel dizinde XLSX baytları, aktör, zaman,
boyut ve SHA-256 içeren değişmez JSON zarflarıdır; gevşek `.xlsx` dosyası
beklenmez. API hesabı mevcut dosyaları okuyabilmeli ve yeni arşiv oluşturabilmeli;
özel arşiv dizini dışında izin genişletilmez. UI hesabının doğrudan arşiv erişimi
gerekmez. Tarayıcı indirmesi kullanıcının tarayıcı ayarındaki konuma gider.

## Operatör Akışı

1. **Sunucuları incele:** OR, bildiren, inceleyici ve son kaynak okumasını kontrol
   edin. Listeye dönüş kayıt değiştirmez. Eski/eksik kaynak uyarısı varsa yetkili
   kaynak yenilemesini kullanın; bu işlem güvenlik kontrolü veya kapanış yapmaz.
2. **Eksikleri tamamla:** Her sunucu için internet çıkışı, internetten erişim ve
   mikrosegmentasyon cevaplarını seçin. Mikrosegmentasyon, sunucunun ağ erişiminin
   küçük ve kontrollü güvenlik bölgeleriyle sınırlandırılmasıdır. Bilinmeyen
   cevap Hayır değildir. Seçili sunuculara kopyalamada değişiklikleri inceleyin;
   farklı cevapları tek tek düzeltebilirsiniz. İptal etmek cevap üretmez.
3. **İnceleme geçmişi:** Aynı doğrulanmış kaynak kimliğindeki önceki OR, kişi,
   tarih ve cevapları görün. Yalnız seçtiğiniz uygun cevapları kabul edin. Ortam,
   ağ, IP, ad veya servis bağlamı değişmişse öneri engellenir. Kaynak gözlemi
   24 saatten, önceki inceleme 30 günden eskiyse yeniden kontrol gerekir.
4. **Ortam/NMS:** PROD üretimdir; DEV geliştirme, TEST test ortamıdır. PROD için
   NMS ve MEMORY/CPU/Disk önerisi Evet; tanınan üretim dışı ortamda Hayır;
   UP_DOWN önerisi Evet'tir. Bilinmeyen ortam üretim dışı sayılmaz. Bunlar
   izleme kurulumu kanıtı değildir. Grupları ve istisnaları görüp öneriyi kabul
   edin, taslağı açıkça kaydedin. Kaydedilmiş ve kaydedilmemiş ilerleme ayrıdır.
5. **Excel'i kontrol et:** Sunucular ve NMS değerlerini inceleyin. Servis sahibi
   eksikse bildiren veya ortak posta adresiyle doldurmayın. Servis unsuru eksikse
   doğru servis ilişkisi doğrulanmalıdır. Eksik alanlar kısmi raporda görünür.
   Arşivleyip indirin; indirme başarısız olsa bile oluşan arşivi yeniden indirin.
6. **Talebe ekle ve tamamla:** Bu kurumsal teslimde kullanılamaz; neden ekranda
   gösterilir. İleride sözleşmeleri doğrulanmış ve ayrıca yetkilendirilmiş ortamda
   tek son onay, arşivdeki aynı baytları sunucu üzerinden kullanacaktır; bilgisayara
   indirip uygulamaya geri yüklemek gerekmez.

| Durum | Operatörün sonraki adımı |
|---|---|
| Eksik cevap | İlk eksik cevaba git; diğer cevaplar korunur |
| Kaydetme çatışması | Güncel kayıtla farkları karşılaştır; açık onay olmadan yeniden kaydetme yapılmaz |
| Yükleme kullanılamıyor | Yerel inceleme/arşivleme ile devam et; yazma bayraklarını açma |
| Ek doğrulandı, görev başarısız | Ek sonucunu koru; yeniden yükleme yapma, görev kanıtıyla yöneticine başvur |
| Yanıt kaybı / belirsiz sonuç | Yazmayı tekrarlama; yetkili kaynak mutabakatı gerekir |
| Görev yanıtı alındı, OR okunamadı | OR kapatıldı sayılmaz; yetkili son-durum kanıtı beklenir |

## Geri Dönüş

Önce yeni In Use komutlarını durdurun; çalışan/belirsiz işlemlerin kanıtlarını
koruyun. Yeni API/UI/Worker birlikte yönetilir. Eski eşleşmiş rc6.22 ikililerine
dönüş yalnız onaylı operatör işlemiyle yapılır; server-owned JSON, web.config,
ring, Branding, arşivler ve SQL 022 verileri silinmez. Eski ikililer yeni cevap
kökeni alanlarını yeniden yazarken kaybedebilir: geri dönüşte In Use yazma
akışını kapalı tutun. Yıkıcı aşağı geçiş yoktur. Başarılı dış etkiler veritabanı
geri dönüşüyle geri alınamaz. Eski engellenmiş niyetler sonradan otomatik başlamaz.
