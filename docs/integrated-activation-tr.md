# Entegre TEST teslimatı ve kontrollü aktivasyon

Bu belge kurumsal aktivasyon yapıldığını bildirmez. Paket metadata'sındaki kaynak
SHA ve bileşen hash'leri esas alınır. Yerel fixture/SMTP kanıtı kurumsal kabul
değildir. Sonuç matrisi: `integrated-test-activation.md`; kaynak sözleşmesi
eksikleri: `rc624-workflow-continuation.md` ve ilgili entegrasyon sözleşmeleri.

## Önce hedef envanteri

API `D:\Applications\api\wasasyonetimapi.thy.com`, UI
`D:\Applications\ui\wasasyonetim.thy.com`, Worker `D:\secureops_worker`.
Bu geliştirme makinesinde bu hedefler erişilebilir değil. Kurulu sürüm, 022'nin
kurulumu, etkili ayarlar, gerçek süreç kimliği ve D: arşiv konumu hedef operatörü
tarafından doğrulanmadan kurulu varsayılmaz. API'nin onaylı etki alanı kimliği,
UI'nin kendi IIS kimliği ve ayrı özel key ring'leri korunur. Worker IIS ayarlarını
miras almaz. Parola, SMTP kimliği, connection string veya key ring teslimata konmaz.

## Dosyalar nerede?

| Belge | Konum / anlam |
|---|---|
| Eski PowerShell Excel'i | Scripti çalıştıran makinede `C:\InUse\InUse_<OR>_<yyyyMMdd_HHmm>.xlsx` |
| WASAS kalıcı arşivi | Etkili `InUseReports:Directory`; önerilen hedef `D:\SecureOpsData\InUseReports` |
| Arşiv biçimi | `<kayıt-GUID>\<sürüm>.json`; XLSX baytları, SHA-256, boyut, aktör ve sürüm içeren değişmez zarf. Gevşek XLSX beklemeyin |
| Tarayıcı indirmesi | Kullanıcının tarayıcı/indirme seçimi; sunucu klasörü değildir |
| Turuncu Hat eki | Seçilen kaynak OR kimliğine `<OR>_InUse.xlsx`; yerel arşiv başarısı ek yüklemesi değildir |

Operatör `In Use → İncele → Excel'i kontrol et → Arşivden indir` ile aynı baytları
alır. İndirme başarısızsa yeni arşiv oluşturmaz, mevcut sürümü tekrar indirir.
Worker tamamlama işlemi için SQL'de dondurulmuş XLSX baytlarını okur; arşiv
klasörüne veya UI süreç kimliğine doğrudan dosya erişimi gerekmez.

## Arşivi koruyarak D: geçişi

1. Etkili mevcut dizini yetkili API tanılaması ve IIS ortam ayarı dahil
   yapılandırma önceliğiyle belirleyin; yalnız appsettings dosyasına bakmayın.
2. Yeni arşiv yazımlarını kontrollü bakım penceresinde durdurun. Devam eden
   isteklerin bitmesini bekleyin. Eski kökün yedeğini ve her JSON için göreli yol,
   byte sayısı, SHA-256 envanterini özel yönetim dizinine alın.
3. JSON zarflarını aynı GUID/sürüm yapısıyla yeni özel köke kopyalayın. Mevcut
   hedefte aynı yol farklı hash içeriyorsa durun; üzerine yazmayın. Reparse point,
   beklenmeyen dosya veya `.pending` varsa inceleyin; otomatik temizlemeyin.
4. Yedek/eski/yeni JSON envanterleri birebir eşleşmelidir. Zarfın Content baytları,
   Size ve Sha256 alanları da API'nin mevcut bütünlük kontrolünden geçmelidir.
   Zarfları elle düzenlemeyin. Eski kökü silmeyin.
5. Mevcut onaylı API kimliğine yalnız gerekli okuma/listeleme, dosya oluşturma,
   kilit/pending yazımı ve atomik rename/temizlik haklarını kurum politikasına
   göre doğrulayın. Full Control veya yeni SQL rolü önermiyoruz. UI'ye hak vermeyin.
6. API web.config içinde aşağıdaki anahtarın **mevcut tek örneğini güncelleyin**;
   çift anahtar eklemeyin. Sadece ilgili uygulamayı kontrollü yeniden başlatın.
   Worker'ın ayrıca yüklenen ayarlarını yalnız gerçekten tükettiği ayarlar için
   karşılaştırın. Tüm sunucuda IIS reset yapmayın.

```xml
<environmentVariable name="InUseReports__Directory" value="D:\SecureOpsData\InUseReports" />
```

7. Gerçek API kimliği altında eski bir arşivi indirin ve önceki hash ile
   karşılaştırın; ayrı onaylı yeni taslağı arşivleyip tekrar indirin. Başarısızlıkta
   yazımları açmayın. Yeni yazı oluştuysa geri dönüşten önce iki kökteki farklı
   sürümleri hash/kimlik karşılaştırmasıyla koruyun; eski klasörü körlemesine
   geri yüklemek yeni arşiv kaybettirir. İşlem, hedef operatör kanıtı bekliyor.

## SQL ve eşleşen bileşenler

001–021 veya Hangfire şema 9 tekrar çalıştırılmaz. 022 kuruluysa tanım/hash ve
geçmiş doğrulanır; değilse önce mevcut incelemeli 022 farkı DBA tarafından
uygulanır. Bu teslimatın yeni farkı **023-workflow-report-snapshots**: rapor
kesitleri/katkı kayıtları/arşiv makbuzları ve nullable kaynak-köken kolonu.
Kurulu migration değiştirilmez; runtime `PrepareSchema=false` kalır.

DBA, 023'te belirtilen dar SELECT/INSERT haklarını mevcut runtime politikasına
göre inceler. Yetki uygulaması geliştirici tarafından yapılmadı. 023 geri dönüşte
silinmez: eski eşleşen API/UI/Worker'a dönülse bile yeni ek veri korunur. Özel
ayarlar/key ring'ler üzerine paket varsayılanları kopyalanmaz. Yedek ve rollback
uyumu doğrulanmadan bileşen değişimi yapılmaz.

## Normal kullanıcı akışları ve gerçek sınırlar

| Akış | Kullanıcı adımı | Kabul için gereken |
|---|---|---|
| OR → SDM | OR seç, kaynak/hedef önizle, yalnız Jira veya ayrı desteklenen kaynak-tamamlama niyetini onayla | Pozitif politika ServerRequest. Diğer enum türleri otomatik SunucuTalep olmaz. Kesin TEST OR/Jira proje/tip/aktör ve alan eşlemesi gerekir |
| In Use | OR'yi incele, üç cevap veya tarihli açık yeniden kullanım, seçili bulk önizleme, kaydet, Excel/arşiv | 29 satır Sunucular, 22 sütun NMS, dört sayfa. Ortam/NMS türetilmiş öneridir; KONTROL/alarmlar yapılmış işlem kanıtı değildir |
| In Use tamamlama | Hazır olduğunda kesin rapor/OR için tek onay, adım sonuçlarını izle | Kurumsal transport hâlâ eksik readback/koşullu-mutasyon sözleşmesi nedeniyle kapalı; Fixture yürütücüsü kurumsal kabul değildir |
| Planlı OCO | OCO+profil, kaynak önerisini seçerek uygula, alıcıları incele, değişmez hazırlık/indir | API/Worker aynı DB/şema/queue/profil; gerçek SCCM/Turuncu Hat okuması ayrı kabul |
| OCO deneme/dağıtım | Kesin hazırlık, güvenilir Mail gönderen, To/Cc/sayı/tarih önizlemesi, ayrı onay | `AnnouncementMail:Enabled`, `SelfTestEnabled`, `SendEnabled` ve ayrı yetkiler. Deneme sadece operatör Mail adresine gider |

Kurumsal In Use için **gerçek adapter henüz hazır değildir**. Kaynak sahibinin
dinamik vaka/sürüm-koşulu, ek kimliği+bayt/hash okuması, son OR durumunun anlamını
sağlaması ve bunun gerçek transport'a uygulanması gerekir. Sadece flag açmayın.
Jira kaynak kapanışı için de otoritatif readback olmadan BPM onayını kapanış
saymayın. Jira bağlantısı varsa kaynak hatasını gidermek için yeni issue yaratmayın.

OCO'da altı orijinal dosya `D:\SecureOpsData\Announcements\Branding\rc6.21`
altında korunur. `planlimail_duyuru_main.jpg` PNG içerir; bayt/MIME değiştirilmez.
Tarayıcı/MIME kontrolü Outlook kabulünün yerine geçmez. Gönderimden önce yetkili
yönetici `AnnouncementMail` gerçek option sınıfındaki relay/TLS/From-envelope
ayarlarını, sunucu-owned sırları ve Worker etkili kimliğini doğrular. TLS bypass
yapılmaz. SMTP kabulü gelen kutusuna teslim değildir; Unknown otomatik denenmez.

`AnnouncementMail` bölümünün gerçek ayarları: `Enabled`, `SelfTestEnabled`,
`SendEnabled`, `Host`, `Port`, `Security` (`StartTls` veya `SslOnConnect`),
`PolicyRevision`, `EnvelopeMode` (`Actor` veya `Configured`), `EnvelopeSender`
(yalnız Configured), `AllowedRecipientDomains`, `MaxRecipients`, `TimeoutSeconds`,
özel `UserName` ve `Password`. `PlaintextLoopback` yalnız izole yerel test içindir.
API/Worker etkili değerleri ve revizyonları aynı olmalıdır; SMTP kimliği, Windows
süreç kimliği ve mesajın kayıtlı insan göndereni farklı kavramlardır.

## Yönetim raporu

`Yönetim Panosu → İş akışı sonuçları`: tarih/saat dilimi seç, raporu yenile,
modül/durum/tür ile daralt, ölçünün Kayıtlar bağlantısını aç veya aynı kesitin
Excel'ini indir. Demo seçimi normalde kapalıdır. OCO yalnız sahibinin kayıtlarını
gösterir; yönetim yetkisi başka kişilerin duyurularını açmaz.
SQL sayım/sayfalama, aynı kesit ve filtreler Excel'e uygulanır. Güncel durum ile
dönem olayları toplanıp sahte bir tamamlanma sayısı yapılmaz. Kısmi/belirsiz
sonuç ve bilinmeyen tarih açık kalır; hata sıfır değildir. Eski zarflar, doğrulanmış
indirmede makbuz oluşana kadar arşiv metriğinde eksik olabilir. Kesit erişimi bir
saat veya yetki sürümü değişimine kadar; yenileme dış kaynağı sorgulamaz.

## Kontrollü TEST kabul sırası

1. Teslimat hash'leri, hedef sürümleri, yedek, schema farkı ve runtime ayarlarını
   kaydedin. Uyuşmazlıkta durun. Eşleşen üç bileşeni mevcut kontrollü süreçle alın.
2. Worker'ı `D:\secureops_worker` content root ve ayrı TEST ayarıyla mevcut
   foreground modelinde başlatın. Oturumu yürüten operatör, çalışma penceresi,
   izleme/yeniden başlatma sorumlusu isimlendirilmelidir. Heartbeat iş başarısı
   değildir. Kesintisiz ekip kullanımı için bu modelin uygunluğu ayrıca karardır.
3. Kesin OR kimliklerini ve Jira hedefini seçin; yalnız desteklenen niyet/tür için
   ordinary-user akışını çalıştırın. Uzak issue/ek/aktivite/son OR kanıtlarını ayrı
   tutun. Eksik gerçek adapter olan aşamayı **aktive etmeyin**.
4. Onaylı OCO/profil kaynağını okuyun, orijinal görsel/MIME ve Outlook'u kontrol
   edin. Önce kayıtlı Mail'e onaylı deneme; dağıtım ayrı hazırlık/açık To/Cc
   onayıyla. Bu belgede seçilmiş kurumsal mesaj veya kitle yoktur.
5. Aynı sonuçları yönetim kesiti/drilldown/Excel ile eşleştirin. Yalnız kanıtlanan
   yetenekleri ekip kullanımına açın. Belirsiz uzak sonuç, yanlış queue, arşiv
   erişim veya schema hatasında yeni etkileri durdurun; kör retry/grant yapmayın.

## Gönderilmeyecek ekip duyurusu taslağı

“WASAS TEST iş akışlarının kontrollü kabulü sürmektedir. Yerel doğrulamalar,
kurumsal upload/kapanış veya e-posta teslimi anlamına gelmez. Hedefte doğrulanan
modüller ve kullanım saatleri kabul sorumlusu tarafından ayrıca bildirilecektir.
Belirsiz işlem sonuçlarında yeniden oluşturma/gönderme yapmadan destek kaydındaki
referansı paylaşın. Tüm iş akışlarının aktif olduğu henüz ilan edilmemiştir.”
