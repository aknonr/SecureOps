# TARAMA-C raporu — Worker JEA çalıştırıcı, uç nokta paketi, Jea toplayıcı (2026-10-14)

Dal `feature/worker-jea-runner-20261014` (güncel `origin/master` + `feature/service-accounts-web-usage-scan-20261013` merge'i,
rebase yok). PR açılmadı, merge yapılmadı. Hiçbir sunucuya bağlanılmadı; WinRM/JEA kaydı, hizmet kurulumu yapılmadı.

## Ne değişti

- **B4 genel JEA çalıştırıcı** (`src/SecureOps.Worker/Jea/`): `JeaEndpointCatalog` tek izin listesi (yapılandırma adı + tek fonksiyon +
  tipli, desenle denetlenen parametreler); `JeaCommand` yalnız katalogdan kurulur (genel yapıcı yok). `IJeaRunner` / `JeaRunner`:
  açma ve işlem zaman aşımı, çıktı boyutu sınırı, aşamaya göre sınıflama (Tamam/Erişilemedi/UçNoktaYok/YetkiYok/ZamanAşımı/Bozuk),
  oturum her durumda kapanır, iptal `OperationCanceledException`. `WsManJeaTransport`: `WSManConnectionInfo` + yapılandırma adının
  shell URI'si (`-ConfigurationName` eşdeğeri), yalnız Kerberos, kimlik nesnesi yok (hizmet kimliği = okuma gMSA'sı), varsayılan HTTPS
  5986, `AddCommand` + tipli `AddParameter` (NoLanguage ile uyumlu, metin birleştirme yok). `FakeJeaRunner` deterministik.
- **B6 Jea toplayıcı** (`JeaServiceAccountUsageCollector`, `Kind = "Jea"`): TARAMA-S raporundaki 5 madde birebir — her sunucu tam bir kez
  `ServerCompletedAsync`, hata = sonuç, belge yalnız Ok ile, `serverName` istenen ad, `MaxDocumentBytes`, durum eşlemesi,
  `MaxParallel`/`OpenTimeout`/`OperationTimeout`, iptalde yeni sunucu başlamaz. DI: `AddWorkerJea` (Program.cs'e tek satır); varsayılan
  kapalı — `Collector = Jea` + modül etkin + kullanılabilir taşıyıcı olmadan toplayıcı kaydedilmez (`collectorUnavailable`).
- **Windows hizmeti:** `scripts/worker/Install|Uninstall-SecureOpsWorkerService.ps1` (gMSA yer tutucusu `CONTOSO\gmsa-placeholder$`
  reddedilir, parola parametresi yok, `sc.exe config obj=`, Manual, başlatmaz; Application olay günlüğü kaynağı `SecureOps.Worker`).
  Worker kaynak varsa yalnız sabit metinli 1000/1001/1002 yazar, kaynağı kendisi oluşturmaz. Hangfire ve paketleme notu
  `src/SecureOps.Worker/README.md`'de.
- **B5 uç nokta:** `scripts/jea/proposed/…` → `scripts/jea/SecureOps.ServiceAccountUsage/` **1.0.0**; `-AsJson` anahtarı (uzak
  serileştirme iç içe nesneleri sığ taşıdığı için), pssc'ye `LanguageMode = NoLanguage`, rol grubu R2-1 yer tutucusu
  `CONTOSO\SecureOps-SaRead`; `scripts/jea/Install|Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1` (sunucu sahibi, bir kez; WinRM
  yeniden başlar). Kişi çalıştırmalı toplayıcının birebir kopyası güncellendi. Ekibin script'leri repoya kopyalanmadı.
- **Testler:** `JeaRunnerTests`, `JeaServiceAccountUsageCollectorTests` (modül iş çalıştırıcısıyla uçtan uca dahil),
  `JeaEndpointPackageTests` (psrc/pssc veri olarak okunur; kurulum betikleri ayrıştırılır, komut izin listesiyle).

## Yapılamayan: sahip istisnası ve karar kayıtları

(0) adımı — `docs/service-accounts/README.md` istisna satırı, ADR-0024 R2 güncellemesi ve AGENTS.md "Open decisions" satırı —
**yazılmadı**: otomatik izin denetleyicisi, sahip kararını yalnız başka oturumdan aldığım için bu düzenlemeyi engelledi. Önerilen
metin (koordinatör veya sahip uygular):

- README "Authority": `- **Owner-approved exception (2026-10-14):** TARAMA-C (Worker JEA çalıştırıcı, Windows hizmet kurulumu,
  SecureOps.ServiceAccountUsage JEA uç noktası, Jea toplayıcı) sahip kararıyla Claude'da; varsayılan sahiplik değişmez.`
- ADR-0024 R2-6'ya: `Owner decision 2026-10-13: Worker access path = direct WinRM + Kerberos + JEA (BeyondTrust path not rejected,
  out of scope); no separate scan approval is sought (owner statement; formal reference in the owner's private record).`
- AGENTS.md: "Worker privileged access (BeyondTrust vs. direct WinRM + Kerberos + JEA)" maddesini "decided 2026-10-13: direct WinRM +
  Kerberos + JEA (ADR-0024 R2)" olarak güncelle (yalnız o satır).

## Sözleşmeyle ilgili bulgular (sözleşme değiştirilmedi)

1. **serverName:** uç nokta `$env:COMPUTERNAME` döndürür; toplayıcı onu istenen adla değiştirir (oturum o ada Kerberos karşılıklı
   doğrulamayla açıldı; kişi çalıştırmalı JEA modu da aynısını yapıyor). FQDN↔kısa ad dönüşümü yapılmıyor. Onay istenir.
2. **COM+ ve uç nokta sürümü (R2-2):** şema kapalı (`sources` üç anahtar, ek alan yok); eklemek şema + ADR-0027 ayrıştırıcı değişikliği.
   Bu işte yapılmadı.
3. **WinRM hata kodu eşlemesi** (`JeaFailureClassifier`) belgelenmiş kodlarla yazıldı, gerçek sunucuda doğrulanmadı; bilinmeyen açılış
   hatası Erişilemedi, sonrası Bozuk sayılır (ikisi de "bilgi yok").
4. **Uyuyan `-UseJeaEndpoint` modu** (`Invoke-ServiceAccountUsageScan.ps1`) iç içe nesneleri sığ serileştirmeye takılır
   (`verification` alt dizileri metne döner); `-AsJson` ile düzeltilebilir — ayrı iş.
5. Sözleşme ve rehberler (030, 040) bu işi "Codex" diye anar; istisna kaydı (yukarıda) yazılınca tutarlı olur.

## Sahibin TEST'te yapacakları

1. AD ekibinden okuma gMSA'sı ve grubu (yer tutucular `CONTOSO\gmsa-so-sa-read$`, `CONTOSO\SecureOps-SaRead`); Worker sunucusunun gMSA
   parolasını alabilmesi; gMSA'ya Worker sunucusunda "Log on as a service" (GPO).
2. Her hedef sunucuya bir kez, sunucu sahibi: `Install-SecureOpsServiceAccountUsageEndpoint.ps1 -ReadGroup … -WhatIf`, sonra gerçek
   (WinRM yeniden başlar). HTTPS dinleyicisi yoksa Worker'da `JeaRunner:UseSsl=false`.
3. Worker sunucusunda: `Install-SecureOpsWorkerService.ps1 -ExecutablePath … -ServiceAccount <gMSA> -DataDirectory … -WhatIf`, sonra
   gerçek; `docs/worker-service-operations-tr.md` kabul kapıları; config: `ServiceAccounts:UsageScanJobs:Collector = Jea`.
4. Pilot (B7): önce 1 sunucu, `NoEndpoint`/`Forbidden`/`Unreachable` sınıflarını bilerek üret (kayıtsız sunucu, gruptan çıkarılmış
   kimlik, kapalı port) ve sınıflamayı doğrula; Windows PowerShell 5.1'de `-AsJson` çıktısı.

## Nasıl doğrulandı / ne doğrulanmadı

- **Çalıştı (yerel Windows 11, SDK 9.0.317):** tam birim süiti 2783 geçti, 0 başarısız, 1 atlanan (mevcut);
  `dotnet format SecureOps.sln --verify-no-changes` temiz. Yerelde derleme/restore için `-p:NuGetAudit=false` gerekti: master'da da
  `src/SecureOps.Ui` için NU1903 (`Microsoft.AspNetCore.Authentication.Negotiate` 8.0.30, GHSA-vrrp-6v32-2p8g) uyarısı hata olarak
  restore'u durduruyor — bu işle ilgisiz, ayrı ele alınmalı.
- **SA SQL süiti** (`sa-sql-harness.ps1 -DatabaseSuffix TaramaC1014`, seri; DB sonra silindi): migration'lar uygulandı, **037 guard
  betiği başarısız**; SA SQL testleri 140 geçti, **3 başarısız**, 2 atlandı, 1205 yok. Üçü de TARAMA-S (037) kaynaklı, bu dalın kodu
  değil: (a) 037 tetikleyicileri `AFTER UPDATE, DELETE` içinde `IF NOT EXISTS (SELECT 1 FROM inserted)` ile siliniyor sanıyor; 0 satırlık
  bir UPDATE'te de tetiklenip 51431–51433 fırlatıyor (`FinishInTransactionAsync` → `ServiceAccountUsageScanJobSqlTests` iki test) —
  önce `IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;` gerekir; (b) `sa-037-guards.sql` biten satırda `Status='Ok'` güncellemesinde
  51433 bekliyor, CHECK `CK_SaScanJobServers_Document` önce 547 veriyor; (c) `SA-010-Worker-permissions.sql` Worker rolüne
  `svcacct.History` SELECT veriyor, `ServiceAccountRoleSqlTests.WorkerRole_OnlyReminderOperationsAreAllowed` bunu reddediyor (hangisinin
  doğru olduğu karar ister). Düzeltmeyi TARAMA-S dalında yapmak gerekir; burada dokunulmadı.
- **Doğrulanmadı:** gerçek WinRM/JEA (hiçbir sunucuya bağlanılmadı), hata kodu eşlemesi, Windows PowerShell 5.1'de `-AsJson`,
  kurulum betiklerinin çalıştırılması (yalnız ayrıştırıldı), SCM hizmeti, olay günlüğü yazımı, gMSA ile Kerberos, Hangfire üzerinden
  gerçek Worker koşusu, tarayıcı.

## Açık sorular

1. (0) adımındaki üç karar kaydını kim yazacak (koordinatör/sahip)?
2. `serverName` değişimi (bulgu 1) kabul mü?
3. TEST'te WinRM HTTPS (5986) dinleyicisi var mı, yoksa `UseSsl=false` mı?
4. Worker rolü `svcacct.History` SELECT almalı mı (SQL hata c)?

## Sonraki adım

TARAMA-S dalında 037 tetikleyici/guard/izin düzeltmesi → SQL süitini yeniden koş; koordinatör (0) kayıtlarını işler; sonra sahip TEST'te
1 sunuculuk pilot (yukarıdaki adımlar).
