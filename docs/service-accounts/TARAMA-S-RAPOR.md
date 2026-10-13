# TARAMA-S raporu — web kullanım taraması, modül tarafı (2026-10-13)

Dal `feature/service-accounts-web-usage-scan-20261013` (master `e79ac0b`'den). PR açılmadı, merge yapılmadı.
Commit'ler: `616fa0e` sözleşme · `a2cb401` 037 + iş + API · `311fc84` OpenAPI ekleri + SQL testleri + DBA-037 · `fd632c7` UI ·
son commit belgeler ve bu rapor.

## Ne değişti

- **Sözleşme (ilk commit, Codex için):** `USAGE-SCAN-JOB-CONTRACT.md`; `IServiceAccountUsageCollector` + `IUsageCollectionObserver`
  (girdi: 1–20 hesap, 1–500 sunucu, sınırlar, iptal; çıktı: sunucu başına sonuç), durumlar Tamam/Erişilemedi/UçNoktaYok/YetkiYok/
  ZamanAşımı/Bozuk (+ yalnız modülün koyduğu Çalışmadı), deterministik `FakeServiceAccountUsageCollector` (ağ yok).
- **Migration 037 (SA-008):** `UsageScanJobs`, `UsageScanJobAccounts` (süzülmüş benzersiz dizin: hesap başına tek etkin tarama),
  `UsageScanJobServers`; koruma tetikleyicileri (silme yok, biten sunucu/iş değişmez); API ve Worker izin betikleri; harness adımı,
  `sa-037-guards.sql`, `sa-037-dba-checks.sql`, `DBA-037-TR.md`.
- **İş:** mevcut Hangfire kuyruğunda `UsageScanJobRunner` (yeniden deneme kapalı, SQL kirası ile korumalı); başlatanın yetkisi
  sunucuya dokunmadan önce yeniden okunur; her sunucu belgesi ADR-0027 §2 ayrıştırıcısından (önce sır koruması) geçer; iş bitince
  tek kanıt kaydı **mevcut 030 tablolarına** `tool = Jea` ile yazılır ve her hesaba başlatanın kimliğiyle, yazma içinde kapsam
  yeniden kontrol edilerek bağlanır. İptal (başlatan veya yönetici) biten sunucuları korur; yeniden dene yalnız tam okunamayan
  sunucuları yeni işle tarar. Kirası düşen iş okunurken `workerLost`, alınmayan iş `notStarted` olarak kapanır.
- **API (yalnız ekleme, OpenAPI anlık görüntüsüne elle eklendi — yeniden üretilmedi):** `GET usage-scan-jobs/availability`,
  `POST usage-scan-jobs`, `GET usage-scan-jobs/{id}?after=`, `POST …/{id}/cancel`, `POST …/{id}/retry`,
  `GET accounts/{id}/usage-scan-jobs`.
- **Kapalı başarısızlık:** `ServiceAccounts:UsageScanJobs:Collector` varsayılanı `None` → özellik kapalı, panel dürüstçe söyler ve
  script yüklemesine yönlendirir. `Fake` yalnız Development/Demo/Test'te; Hangfire yoksa yalnız bu durumda API içinde demo çalıştırıcı.
- **UI:** `/service-accounts/usage-scan-jobs/{id}` canlı sayfa (kutucuklar: simge + metin + kenar biçimi; ilerleme çubuğu ve kalan
  süre tahmini; bulunanlar anında listeye; özet "3 sunucuda 7 kullanım…; 2 sunucuya erişilemedi… → yeniden dene"; her durumda
  "Sıradaki adım"; tek `aria-live="polite"` yalnız kilometre taşlarında; `prefers-reduced-motion` ile hareket kapalı). Hesap
  sayfasında "Webden tara" paneli (+ son taramalar), listede seçili hesaplar için aynı panel.
- **Belgeler:** SPEC kural 10, README, PROGRESS, USAGE-SCAN-TR §0, WINDOWS-ACCEPTANCE satır 89–92, ADR-0027'ye 2026-10-13 notu.

## Codex için sözleşme özeti

1. `IServiceAccountUsageCollector` uygulayın, `Kind = "Jea"`, Worker DI'a kaydedin. Modül `Collector = Jea` görünce onu çözer;
   bulamazsa iş `collectorUnavailable` ile biter ve hiçbir sunucuya bağlanılmaz.
2. Her istenen sunucu için **tam bir kez** `ServerCompletedAsync`; hata = sonuç (istisna değil). Belge yalnız `Ok` ile, UTF-8 JSON,
   `service-account-usage-v1`, `serverName` istenen adla aynı, ≤ `MaxDocumentBytes` (aşarsa `Malformed`).
3. Durum eşlemesi: WinRM yanıtı yok → `Unreachable`; WinRM var uç nokta yok → `NoEndpoint`; kimlik reddi → `Forbidden`;
   işlem süresi doldu → `Timeout`; tek geçerli belge değil → `Malformed`.
4. Yalnız JEA (`-ConfigurationName`), okuma gMSA'sı, parola yok, metin birleştirme yok, erişilebilirlik önbelleği yok;
   `MaxParallel`/`OpenTimeout`/`OperationTimeout` uyun; iptalde yeni sunucu başlatmayın ve dönün.
5. `ServerStartingAsync` isteğe bağlı ("bağlanıyor" kutucuğu için). Gözlemci thread-safe; paralel çağrılabilir.

## Nasıl doğrulandı / ne doğrulanmadı

- **Çalıştı (Linux bulut, SDK 9.0.317 conda-forge paketinden, net8 çalışma zamanı apt'tan):** `dotnet build SecureOps.sln` 0 hata
  0 uyarı; birim testleri 2245 geçti, 2 başarısız — ikisi de master'da aynı biçimde başarısız (bilinen Linux kaynaklı:
  `AuditConfigurationValidatorTests.Validate_WhenProductionFailOpen_Throws`, `SccmFailureEvidenceTests.StagedInvocations_*`);
  entegrasyon: 360 geçti, 7 `AnnouncementTests` başarısız (master'da da aynı 7, SkiaSharp/Linux), SQL testleri atlandı.
  OpenAPI anlık görüntü testi geçti. `dotnet format --verify-no-changes`: Linux checkout LF olduğu için deponun CRLF kopyasında
  koşturuldu, temiz.
- Yeni testler: birim (kurallar, durum eşlemesi, Fake ile uçtan uca, iptal, kuyrukta iptal, başlatan yetkisi kalkınca, toplayıcı
  yok/izinli değil, toplayıcı istisnası, bozuk/sır içeren belge, sözleşme ihlali, kira kaybı, gMSA kontrolü, kullanılabilirlik),
  UI (sunum kuralları, canlı sayfa ve panel render'ı, CSS hareket/odak kuralları), API rota politikaları.
- **Doğrulanmadı:** 037 ve guard'lar, `ServiceAccountUsageScanJobSqlTests` (SQL Server yok; yalnız derlendi), Windows/IIS, tarayıcı
  (canlı sayfa gerçek tarayıcıda hiç açılmadı; 390 px / %200 / koyu tema / ekran okuyucu gözle denenmedi), Hangfire üzerinden gerçek
  Worker çalıştırması, gerçek JEA. Hiçbir sunucuya bağlanılmadı.

## Açık sorular / riskler

1. **Sürüm aracı:** `scripts/release/Get-ReleaseSqlPlan.ps1` "tam 001–N zinciri" ister; 037 (035/036 olmadan) bunu bilerek reddeder
   ve `tests/release/*` bu dalda kırılabilir (çalıştırılmadı, pwsh yok). Gözden geçirilmiş güncelleme Codex'in işi.
2. **R2-4 ile fark:** ADR "her sunucu bittikçe aynı append-only tablolara" diyor; 030'un başlığı (dosya, özet, sayılar) değiştirilemez
   olduğundan sunucu bittikçe sonuç 037 iş tablosuna, tek kanıt kaydı iş bitince 030'a yazılıyor. Canlı görünüm aynı. Ayrıntılı neden
   (UçNoktaYok vb.) 030'da `NoResult` olarak duruyor; 030 CHECK'ine eklemek ayrı karar ister.
3. **Bekleme okuması yazı yapar:** kirası düşmüş iş, durum okunurken kapanır (tekrarlayan kurtarma işi eklenmedi; Hangfire sunucu
   dosyası Codex'te). Worker kapalıyken kuyruktaki iş 15 dk sonra `notStarted` olur.
4. AGENTS.md "Open, do not assume: Worker privileged access" satırı ve ADR-0024 R2-6 güncellemesi TARAMA-C'de bekliyor.
5. Sahip kararı: script onaylı olduğu için ayrı tarama onayı aranmıyor; JEA uç noktası kaydı ve okuma gMSA'sı yine kurum sürecinde.

## Sonraki adım

Koordinatör Windows'ta: `sa-sql-harness.ps1` (037 + guard), SQL testleri, yerel Demo'da `Collector=Fake` +
`FakeDelayMilliseconds=600` ile WINDOWS-ACCEPTANCE satır 90–92 (tarayıcı, klavye, 390 px, koyu tema, azaltılmış hareket). Codex:
TARAMA-C'de `Jea` toplayıcısını bu sözleşmeye bağlar ve sürüm aracını 037 için günceller.
