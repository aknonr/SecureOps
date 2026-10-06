# TEST 028–032 operatör kontrol listesi

Tarih: 2026-10-07. Tek güncel operatör girişi. Bu belge ve paket **gözden geçirme adayıdır**;
`readyForInstallation=false`. Kurulu TEST'e uygulanmamıştır. Sentetik LocalDB kanıtı hedef kabulü değildir.

## 1. Salt okunur envanter ve durma noktası

- DBA, doğru veritabanında `DBA/Get-InstalledMigrationInventory025To032.sql` çalıştırır.
  Repo karşılığı: `scripts/diagnostics/Get-InstalledMigrationInventory025To032.sql`.
  Yalnız metadata ve 027/028 için sabit Admin yetkileri/audit işaretleri okunur; kişi veya iş verisi döndürülmez.
  `VIEW DEFINITION` ve ilgili iki tabloda SELECT gerekir. Yetki hatasında durun; eksik görünürlüğü yokluk saymayın.
- 025 ve 026 bütün işaretleriyle uygulanmış, 027 yetki/audit işaretleriyle uygulanmış olmalı.
  028 ya tamamen uygulanmış ya tamamen eksik olmalı. 029–032 bütün işaretleriyle eksik olmalı.
  Kısmi, çelişkili veya beklenmeyen sonuçta durun. “uygulanmamış” yanında yeniden çalıştırma engeli varsa çalıştırmayın.
- 027/028 nesne eklemez: yetki ve audit işaretleri birlikte değerlendirilir. İşaretlerin bulunması tam DDL,
  izin, veri korunumu veya change onayı kanıtı değildir. DBA tanımları ayrıca karşılaştırır.
- `svcacct_api_runtime` rolü ve önceki onaylı runtime izinleri mevcut olmalı. Bu paket eski rol/grant dosyalarını,
  rol üyeliğini, 001–027'yi veya Hangfire kurulumunu tekrar çalıştırmaz.
- Envanter sonucu ve DBA incelemesi özel kanıt dosyasına kaydedilir. Kurulu taban bilinmiyorsa paket kuruluma geçemez.

## 2. Kaynağa bağlı aday ve onaylar

- Temiz, commit edilmiş kaynak `ExpectedSource` ile birebir eşleşir. Ürün kaynağı `TestedProductSource`,
  doğrulanmış `origin/master` SHA'sıdır; hazırlık kaynağı ondan türemeli ve ürün girdileri aynı olmalıdır.
  API/UI ProductVersion bu ürün SHA'sını; SQL, belgeler ve inceleme hash'leri hazırlık kaynağını belirtir.
- `candidate.json`, payload manifestleri ve `supportingFiles` SHA-256 değerleri teslim edilen dosyalarla karşılaştırılır.
  Aday yeniden etiketlenmez veya mevcut çıktı üzerine yazılmaz. Kaynak değişirse inceleme ve hash'ler yeniden hazırlanır.
- Özel `wasas.sql-upgrade-review.v1` kaydı: `Source=ExpectedSource`; boolean `Baseline027Verified` veya
  `Baseline028Verified`, `Inventory025To032Verified`, `Unapplied029To032Verified`, `RuntimeApiRoleVerified`,
  `Delta029To032Reviewed=true`; dolu `InstalledInventoryEvidenceReference`, `Delta029To032ReviewReference`.
  027 tabanında ayrıca `Baseline028AbsentVerified`, `AdminOperations028Reviewed=true` ve
  `AdminOperations028ReviewReference` gerekir. `Files` seçilen delta, include ve SA-004 izin dosyalarının tam
  Path/Sha256 kümesidir. LocalDB kanıtı yalnız aday üretimi için kullanılabilir; hedefin görüldüğünü iddia etmez.
- Proje sahibi ve DBA: gerçek taban, 028 yetki değişikliği (gerekirse), 029 ilk kapsam, 030/izinler,
  031 istenen gMSA adı ve 032 indeks için ayrı uygulama onayı/change kaydı gerekir.
  DBA yedek/geri yükleme kanıtını, kapasite ve geri dönüş noktasını kendi sürecinde hazırlar.

## 3. DBA sırası

API/UI yayınından önce, yalnız onaylı change kapsamında:

1. Taban 027 ise ve 028 bütün işaretleriyle eksikse: **028**; yetki paketi, access version ve audit doğrulanır, dur/onay noktası.
2. **029**; IsBootstrap varsayılanı, tek bootstrap indeksi ve güvenilir self-grant kısıtı doğrulanır, dur/onay noktası.
3. **030**; beş tablo, değişmezlik tetikleyicileri ve güvenilir kısıtlar doğrulanır, dur/onay noktası.
4. **SA-004-API-permissions.sql**; yalnız API rolüne beş tabloda SELECT/INSERT, üyelik yok; dur/onay noktası.
5. **031**; iki `RequestedGmsaName nvarchar(256) NULL` sütunu, varsayılan yok, mevcut değerler NULL; dur/onay noktası.
6. **032**; `(UserId ASC, RequestedAt DESC) INCLUDE (Status)` etkin, nonunique, unfiltered indeks; dur/onay noktası.

`candidate.json.sqlSequence` aynı sırayı gösterir. Numaralı wrapper'lar `DBA/sql/migrations` çalışma dizininden
SQLCMD `-I -b` ile çalışır; `:r` include'ları bu dizine göre çözülür. SA-004 izin dosyası kendi dizininden çalışır.
Include/schema dosyaları bağımlılıktır: wrapper'a ek olarak çalıştırılmaz. Her adımda sıfır olmayan çıkışta durun.
Kurulu migration tekrar çalıştırılmaz; replay reddi yalnız sentetik harness'te denenir.

DBA ayrıntıları: [029–031 notu](../service-accounts/DBA-029-030-TR.md),
[032 notu](../access-registration-dba-032.md). Eski DBA notundaki toplu komut sırası yerine bu listenin
030 → SA-004 izinleri → 031 sırası geçerlidir. 032 normal indeks oluşturur; blocking, log ve alan ihtiyacı hedefte değerlendirilir.

## 4. UI ve API birlikte yayın

- API ve UI aynı doğrulanmış ürün SHA'sından **birlikte** yayınlanır; eski UI/yeni API karışımı kabul edilmez.
  ADR-0029: tüm POST/PUT/PATCH/DELETE ve diğer unsafe istemciler tam bir `X-SecureOps-Csrf: 1` başlığı gönderir.
  Yetkili fakat başlıksız unsafe istemci 403 `ApiCsrfRejected` alır; denial audit saklanamazsa 503 olur.
  Başlık kimlik veya yetki sağlamaz. GET/HEAD/OPTIONS mevcut davranışını korur.
- API süreç yapılandırmasında `ApiCsrf__AllowedOrigins__0`, `ApiCsrf__AllowedOrigins__1`, … tam dış tarayıcı
  origin'leri olarak tanımlanır: yalnız şema/host/gerekirse port. Sentetik örnek `https://ui.example.invalid`.
  Wildcard, path, query, fragment veya userinfo kullanılmaz. Boş liste tarayıcı origin'lerini reddeder;
  hatalı değer başlangıcı durdurur. UI sunucu HttpClient çağrısı Origin taşımadığı için origin kaydı gerektirmez,
  fakat CSRF başlığı zorunludur. Gerçek origin'ler pakete yazılmaz; sunucu sahibi onaylı yapılandırmada tutar.
- F5/IIS Origin, Referer ve Sec-Fetch-* başlıklarını korur; cross-site daima reddedilir.
  Bu ayar CORS izni değildir. CLI/operatör/API istemcileri de başlık sözleşmesine geçirilir.
- Paket `web.config` ve `appsettings*.json` taşımaz. Sunucuya ait yapılandırma, proxy güveni, dış yazma/no-send
  korumaları korunur. IIS/app pool/servis/LB değişikliği bu adayın yetkisi değildir.

## 5. Geri dönüş ve ilk kabul

- Sorunda yayın durdurulur; önceki eşleşen API/UI çifti ve önceki onaylı yapılandırma birlikte geri alınır.
  Eklenen tablolar/sütunlar/032 indeksi ve audit korunur; otomatik down migration veya kayıt silme yoktur.
  028 yetki geri dönüşü, audit/AccessVersion etkileri nedeniyle ayrı incelenmiş işlemdir; sessiz JSON geri yazımı yapılmaz.
  DBA restore yalnız önceden onaylı geri dönüş planıyla; yeni audit/iş verisinin kaybı ayrıca değerlendirilir.
- İlk kabul yalnız sentetik kayıtla: login/rol/kapsam erişimi, eski kayıtların okunması, talep/gMSA adı,
  tarama yükleme/karar/rapor; başarılı unsafe UI çağrısı, başlıksız istemci 403, cross-site red ve denial audit.
  Reddedilen istekte business/provider işlemi yoktur; güvenlik session/identity/audit yazıları ayrı tutulur.
- SQL sonrası envanter, 030 izinleri, 031 sütun tipleri, 032 plan/indeks ve hedef deadlock kanıtı kontrol edilir.
  Gerçek AD/JEA, kurumsal SQL/OIDC/Negotiate/IIS/F5, e-posta/Jira ve kurulu TEST kabulü yerel kanıtla kapanmaz.
  Herhangi bir kurumsal bağlantı veya uygulama için ayrı yetki gerekir. Bu aday `readyForInstallation=false` kalır.
