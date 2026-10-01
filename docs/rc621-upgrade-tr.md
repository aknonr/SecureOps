# rc6.21 Sonrası TEST Düzeltme Geçişi

Bu belge kurulum onayı değildir. Yeni paketin kaynak SHA ve arşiv hashleri
`release-metadata.json` içindedir. rc6.21 korunur; 001-021 ve Hangfire schema 9
yeniden çalıştırılmaz. Bu devamda yeni migration yoktur. Windows Service kurulmaz.

## Etkin Yapılandırma

API `D:\Applications\api\wasasyonetimapi.thy.com`, UI
`D:\Applications\ui\wasasyonetim.thy.com`, Worker `D:\secureops_worker` olarak
kalır. Mevcut çalışma kimliklerini, sırları ve ayrı API/UI kalıcı ringlerini koruyun.
UI'ye SQL, Turuncu Hat veya SMTP sırları eklemeyin. API'nin IIS `web.config`
environmentVariables değerleri JSON'u geçersiz kılabilir. Worker bunları devralmaz.
Binary değişimi yeni ayarları sunucu dosyalarına kendiliğinden eklemez.

Yeni eşleşen API/UI ile yetkili kullanıcının **Sistem Durumu / OCO ve rapor arşivi**
denetimini çalıştırıp tanılama kaydını indirin. Bu, UI sunucusunun API'den aldığı
izinli sonucu gösterir; tarayıcı Network kaydından API isteği çıkarmaya çalışmayın.
JSON yalnız yapılandırma anahtarları, izinli değerler/özetler, kimlik, dizin ve
kuyruk gözlemleridir. İç değişiklik kaydında saklayın; tam config/HAR paylaşmayın.

Worker'ın mevcut onaylı gerçek yerel oturumunda, aynı dizin ve ortamda:

```powershell
Set-Location -LiteralPath 'D:\secureops_worker'
$env:DOTNET_ENVIRONMENT = 'Test'
whoami
dotnet .\SecureOps.Worker.dll --diagnostics > .\worker-operations.json
```

Bu komut ikinci job server başlatmaz, kaynak sağlayıcılarına bağlanmaz; SQL'de
yalnız sınırlı Hangfire heartbeat okur. Normal Worker başlangıç kaydı da etkin
ayar özetini içerir. Rapor tarihi yeni sürecin tarihidir; mevcut Worker'ın eski
config ile çalışmadığını başlangıç özetiyle ayrıca karşılaştırın.

```powershell
.\Compare-OperationsReadiness.ps1 -ApiReport '<indirilen API raporu>' -WorkerReport '.\worker-operations.json'
```

Farkları yalnız mevcut onaylı değişiklik prosedüründe düzeltin:

| Ayar | Gerekli durum |
| --- | --- |
| `Announcements:Enabled` / `AnnouncementSource:Enabled` | Kaynak iş akışı onaylıysa API ve Worker'da true |
| `Hangfire:Enabled` / `SchemaName` / `Queue` | İki süreçte aynı onaylı DB/schema/kuyruk; `PrepareSchema=false` |
| `ConnectionStrings:SecureOpsDb` | Var olan Integrated Security bağlantısı; yalnız hedef özeti karşılaştırılır |
| `AnnouncementSource:CollectionProvider` / `ServiceProvider` | Onaylı ConfigurationManager / TuruncuHat; sunucuda Fixture kullanılmaz |
| `SiteCode` / `ProviderMachineName` | Mevcut onaylı SCCM değerleri; modül/runtime uyumu ayrıca doğrulanır |
| `ServiceInstanceBaseObject` / `ServiceNameSelect` / `ChangeBaseObject` | İncelenen kaynak sözleşmesinin mevcut değerleri |
| `AnnouncementSource:Profiles:{profil}` | CollectionId, Scope, Impact, Checks ve Description veya DescriptionTemplate; iki süreçte aynı Revision ve alan özeti |

İzinli profiller NonProd, Prod01, Prod02, ProdSingle, ProdRPA'dır. Yeni
`DescriptionTemplate` yalnız `{WorkStart}` ve `{WorkEnd}` yer tutucularını açar;
örneğin `Çalışma: {WorkStart} - {WorkEnd}`. Kaynak saatinin farkı belirsizse
operatör saat dilimini incelemeden tarihler uygulanmaz. To/Cc kaynak okumanın
önkoşulu değildir; göndermek için ayrı doğrulama/onay gerekir. SMTP açmayın.

Durumlar: **Kapalı** açık kapatma; **Yapılandırma eksik** anahtar denetimi;
**Doğrulanamadı** depolama/okuma hatası; **Güncel Worker yok** heartbeat yok;
**Worker kuyruğu farklı** yaşayan kayıt var ama kuyruk eşleşmiyor. **Kuyruk hazır**
yalnız enqueue/Worker hazırlığıdır. SCCM veya Turuncu Hat başarısı değildir.

## Görseller ve Raporlar

Kurulmuş altı özgün görseli ve
`D:\SecureOpsData\Announcements\Branding\rc6.21` dizinini koruyun. Mevcut
`oco-rc621-test` eşlemesi yeniden adlandırılmaz. main.jpg PNG baytlarını korur.
Yeni taslaklar `oco-table-v3` kullanır; eski taslak geçişi açık yükseltme ve yeni
kayıttır. Tarihsel hazırlanmış MIME değişmez. TEST altbilgisi kurumsal onay değildir.

API hesabı görsel dizininde okuma/listeleme yetkisine ihtiyaç duyar; yazma veya
Full Control gerekmez. Son ACL düzeltmesinin yapıldığı doğrulanmış değildir.
Rapor dizini `InUseReports:Directory` etkin değeridir; özel ve deployment/webroot
dışında olmalıdır. API hesabının bu dizinde okuma, yeni dizin/dosya oluşturma ve
uygulamanın geçici dosyayı değişmez ada taşıması için gereken dar izinlerini
mevcut onaylı ACL prosedürüyle denetleyin. UI hesabına bu dizin izni vermeyin.
SQL `db_owner` uygulama yetkisi veya dosya izni sağlamaz; grant genişletmeyin.

Arşivde gevşek XLSX beklemeyin: JSON zarfı XLSX baytları, hash, boyut, sürüm ve
aktörü içerir. İndirme tarayıcının seçtiği konuma gider. Arşiv başarılı, indirme
başarısızsa aynı **Arşivi indir** eylemini kullanın; raporu tekrar üretmeyin.

`InUsePolicy:Revision` değişiklikle birlikte sürümlenir. `Proposals` altındaki
izinli alanlar COUNTRY, Department, Sub_Department, Contact_email,
ITMC_Event_Owner_Group, Device_Type'tır. İş sahibinin onaylı değerleri dışında
öneri eklemeyin. Kaynak değerleri öneriden önceliklidir. KONTROL, bireysel sahip,
OS sürümü veya tamamlanmış alarm kurulumu bu ayarlarla doldurulamaz. PROD/DEV/TEST
kuralları yalnız öneridir; operatör gösterilen sürümü ayrıca taslağa kabul eder.

## Kurulum ve Geri Dönüş

1. Yeni manifest/ZIP hashlerini kontrol edin; API/UI/Worker aynı build SHA olmalı.
2. Onaylı pencerede üç binary/config yedeğini, SQL recovery point ve özel arşiv/ring
   yedek referanslarını kaydedin. Paket config dosyaları mevcut dosyaların yerine geçmez.
3. Aynı kaynaklı API/UI/Worker'ı mevcut dağıtım prosedürüyle değiştirin. Yalnız
   incelemede saptanan ayar farklarını onaylı süreçte uygulayın. IIS reset yapmayın.
4. Worker'ı aynı hesap/dizin/Test ortamında foreground başlatın; startup özeti,
   yeni heartbeat ve tek açıkça başlatılan kaynak işinin terminal sonucunu eşleştirin.
5. Yerel arşiv, yeniden indirme ve normal rol değişikliğini ayrı doğrulayın.
   Kaynak iş başarısı gönderim veya OR kapanışı değildir.

Geri dönüş, onaylı önceki üç binary ve config yedeğine dönüş kararıdır. Önce
yazıları durdurun; eski binary'nin yeni JSON politika/aktör alanlarını koruyacağı
kanıtlanmadığından otomatik rollback uyumu iddia edilmez. 001-021, audit/geçmiş,
hazırlanmış MIME ve arşiv zarfları silinmez. SQL restore ayrıca DBA kararıdır.

## Açık Kurumsal Kabul

OCO numarası/profil ile tek kaynak işi: job kimliği, UTC aralığı, terminal durum,
cihaz/servis sayıları ve başarısız aşama kaydedilir. Hata varsa yalnız tam destek
referansı, endpoint/problem code, aktör erişim sürümü ve aşama paylaşılır.
Kimlik bilgileri, kaynak içerikleri veya bulanık referanstan tahmin paylaşılmaz.

Outlook'ta yeni v3 `.eml` dosyasını referansla aynı pencere ve %120 zoom'da açın.
Ortalanmış 600px alan, üstte header, altında main, başlık, iki sütunlu tablo,
altbilgi/logo/üç sosyal görseli kontrol edin. Uzun servis listesi taşmamalı, altı
CID çevrimdışı açılmalı. Eski hazırlıklar aynı kalmalı. Yerel MIME/browser testi
bu gerçek Outlook kontrolünün veya gelen kutusu tesliminin yerine geçmez.

WebSocket/Long Polling ayrı taşıma incelemesidir. `unload` uyarısını uygulama
hatasının nedeni saymayın; Permissions-Policy gevşetmeyin. Kalıcı Windows Service
işletim modeli ayrı takip edilir. Bu geçiş dağıtım maili, ek yükleme veya BPM/OR
kapatma için yeni yetki sağlamaz.
