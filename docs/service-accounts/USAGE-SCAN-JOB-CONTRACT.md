# Web kullanım taraması — iş sözleşmesi (TARAMA-S ↔ TARAMA-C)

**Durum:** sözleşme (2026-10-13). Modül tarafı (TARAMA-S, Claude) bu belgeyi ve arayüzü önce yazar; Worker JEA çalıştırıcısı
(TARAMA-C, Codex) sonra bu arayüzü uygular. Karar kaynakları: ADR-0024 R2 (sahip kararı 2026-10-06; R2-4 akış), ADR-0027 §2
(ayrıştırıcı), sahip kararı 2026-10-13 (webden aynı salt okunur tarama; script kurumda onaylı olduğu için ayrı tarama onayı
aranmıyor; Worker Windows hizmeti; doğrudan WinRM + Kerberos + JEA). AGENTS.md kural 1, 3, 6, 8 aynen geçerlidir.

## 1. Sınır

| Taraf | Sahip | Ne yapar | Ne yapmaz |
|---|---|---|---|
| Modül (bu dal) | Claude | Yetki ve kapsam, iş kaydı (037), kuyruk, iptal, yeniden dene, belge doğrulama (ADR-0027 §2), kanıt kaydı (`tool = Jea`), API, UI, sahte toplayıcı | WinRM/JEA bağlantısı, Worker hizmet kurulumu, uç nokta paketi |
| Worker JEA çalıştırıcı | Codex | `IServiceAccountUsageCollector` gerçek uygulaması (`Kind = "Jea"`): bağlantı, zaman aşımı, sınırlı paralellik, iptal, sonuç türleri | Yetki kararı, SQL yazımı, belge yorumu, kanıt kaydı |

Kod: `src/SecureOps.Infrastructure/ServiceAccounts/UsageScans/Collection/IServiceAccountUsageCollector.cs` (arayüz ve kayıtlar),
`src/SecureOps.Domain/ServiceAccounts/UsageCollectionStatus.cs` (durumlar), `FakeServiceAccountUsageCollector.cs` (sahte),
`UsageScanJobBundle.cs` (belge doğrulama ve kanıt paketi).

## 2. Arayüz

```csharp
public interface IServiceAccountUsageCollector
{
    string Kind { get; }                       // "Jea" (Codex) | "Fake" (yalnız test/demo)
    Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken);
}

public interface IUsageCollectionObserver      // thread-safe; paralel çağrılabilir
{
    ValueTask ServerStartingAsync(string serverName, CancellationToken ct);              // isteğe bağlı: "bağlanıyor"
    ValueTask ServerCompletedAsync(UsageCollectionServerResult result, CancellationToken ct); // sunucu başına TAM BİR kez
}

record UsageCollectionRequest(Guid JobId, IReadOnlyList<string> Accounts /*1–20*/, string? ExpectedAccount,
    IReadOnlyList<string> Servers /*1–500*/, UsageCollectionLimits Limits);
record UsageCollectionLimits(int MaxParallel /*32*/, TimeSpan OpenTimeout /*15 s*/, TimeSpan OperationTimeout /*180 s*/,
    int MaxDocumentBytes /*512 KiB*/);
record UsageCollectionServerResult(string ServerName, UsageCollectionStatus Status, byte[]? Document, TimeSpan Elapsed);
```

### Durumlar (`UsageCollectionStatus`)

| Değer | Türkçe | Ne zaman | Belge | Kanıtta (`UsageScanServers.Result`) |
|---|---|---|---|---|
| `Ok` | Tamam | Uç nokta tek bir `service-account-usage-v1` belgesi döndürdü | var (UTF-8 JSON) | belgenin `scanResult`'ı (`Success`/`Partial`/`Failed`) |
| `Unreachable` | Erişilemedi | Açma süresinde WinRM yanıtı yok (ad çözülmedi, port kapalı, sunucu kapalı) | yok | `Unreachable` |
| `NoEndpoint` | UçNoktaYok | WinRM yanıt verdi, JEA uç noktası kayıtlı değil | yok | `NoResult` |
| `Forbidden` | YetkiYok | Uç nokta var, okuma kimliği reddedildi (Kerberos / rol eşlemesi) | yok | `NoResult` |
| `Timeout` | ZamanAşımı | Bağlandı, işlem süresinde tam yanıt yok | yok | `NoResult` |
| `Malformed` | Bozuk | Bağlandı, yanıt tam bir geçerli belge değil (modül de ayrıştırıcı reddederse koyar) | yok | `NoResult` |
| `NotRun` | Çalışmadı | **Yalnız modül koyar:** iptal veya toplayıcı durdu, sunucu raporlanmadı | yok | `NoResult` |

`Ok` dışındaki hiçbir durum "kullanılmıyor" değildir (SPEC kural 15); kanıtta "bilgi yok" olarak durur ve sonraki taramadan
sunucuyu dışlamaz. Ayrıntılı neden (UçNoktaYok/YetkiYok/ZamanAşımı/Bozuk) iş tablosunda (037) kalır; 030 kanıt tablosu
yalnız `Unreachable`/`NoResult` tutar ve **değiştirilmez**.

## 3. Uygulayıcının (Codex) yükümlülükleri

1. **Salt okunur, yalnız JEA** (kural 1, 3): her bağlantı `-ConfigurationName` ile kayıtlı uç noktaya; varsayılan uç nokta,
   `New-PSSession` ile serbest runspace, `Invoke-Expression` veya yazma komutu yok. Okuma kimliği gMSA (ADR-0024 R2-1); parola,
   kişi kimlik bilgisi veya sır yok, hiçbir yere yazılmaz.
2. **Komut yalnız doğrulanmış değerlerden:** `Accounts`, `ExpectedAccount`, `Servers` modülde `service-account-usage-scan-v1`
   desenlerinden geçmiştir (`UsageScanJobRules.IsAccountName/IsServerName`); yine de parametre olarak geçirin, metin birleştirmeyin.
3. **Her sunucu tam bir kez** `ServerCompletedAsync` ile raporlanır (iptal hariç). Bilinmeyen veya ikinci kez gelen ad modülce
   yok sayılır ve günlüğe yazılır. Sunucu başına hata istisna değil sonuçtur.
4. **Belge:** `Document` yalnız `Ok` ile; içindeki `serverName` istenen adla aynıdır (büyük/küçük harf duyarsız; FQDN↔kısa ad
   dönüşümü yapmayın), `accounts` istenen listedir, `ExpectedAccount` verilmişse `verification` bloğu vardır. Boyut ≤
   `MaxDocumentBytes`; aşarsa `Malformed` raporlayın. Belge günlüğe yazılmaz.
5. **Sınırlar:** en çok `MaxParallel` sunucu aynı anda; `OpenTimeout` sonunda `Unreachable`, `OperationTimeout` sonunda
   `Timeout`. Erişilebilirlik önbelleği yok: her iş her sunucuyu yeniden dener (R2-4).
6. **İptal:** `cancellationToken` iptal edilince yeni sunucu başlatmayın, çalışanları kısa sürede bırakın ve dönün (ya da
   `OperationCanceledException`). Raporlanmayan sunucuları modül `NotRun` yapar.
7. **Beklenmedik istisna** tüm işi bitirir (`collectorFailed`); raporlanan sunucular kanıta yazılır.
8. **Kayıt:** Worker DI'a `IServiceAccountUsageCollector` (Kind `Jea`) olarak, singleton veya scoped; modül
   `ServiceAccounts:UsageScanJobs:Collector = Jea` görünce onu çözer. Bulamazsa iş `collectorUnavailable` ile başarısız olur ve
   hiçbir sunucuya bağlanılmaz.
9. **Test:** gerçek sunucu yok; sahte transport ile her durumun üretildiği birim testleri. Modülün uçtan uca testleri
   `FakeServiceAccountUsageCollector` ile koşar.

## 4. Modülün yükümlülükleri (bu dal)

- **Başlatma** (`POST /api/v1/service-accounts/usage-scan-jobs`): `ServiceAccounts.Work` + seçilen **her** hesapta sorumlu temel
  (organizasyon kapsamı veya sahip ekip), 1–20 hesap, 1–500 sunucu. Kapsam dışı = bulunamadı (404). Hesap başına tek çalışan
  tarama: SQL'de süzülmüş benzersiz dizin (`UsageScanJobAccounts.IsActive = 1`), çakışma 409 `scanAlreadyRunning`.
- **Kuyruk:** mevcut Hangfire istemcisi (`AddSecureOpsJobClient`) ile mevcut kuyruğa tek iş; API yalnız kuyruğa verir, Worker
  çalıştırır; Hangfire yeniden denemesi kapalı (`AutomaticRetry(Attempts = 0)`), iş SQL'de kiralanır (lease) ve kira düşerse
  okunurken `workerLost` ile kapanır (hesaplar serbest kalır).
- **Her sunucu bitince:** belge tek sunuculu paket olarak `UsageScanParser` ile doğrulanır (sır koruması önce), sonuç ve bulunan
  bileşenler iş tablosuna yazılır; UI bunları anında gösterir.
- **İş bitince** (tamam veya iptal, en az bir sunucu bittiyse): tüm sunucular `service-account-usage-scan-v1` (`tool = Jea`)
  paketine dönüştürülür, aynı ayrıştırıcıdan geçer ve **mevcut** 030 tablolarına tek kanıt kaydı olarak yazılır; her hesaba
  başlatanın kimliğiyle ve yazma içinde yeniden kapsam kontrolüyle bağlanır (ADR-0027 §4). Bileşenler kendiliğinden kullanım
  olmaz; kişi karar verir.
- **İptal:** başlatan veya `ServiceAccounts.Administer` (kapsamı hesapları kapsıyorsa). **Yeniden dene:** bitmiş iş için,
  yalnız tam okunamayan sunucularla (`Ok` olmayan veya belge `Partial`/`Failed`) yeni iş; aynı yetki kuralı.
- **Kapalı başarısızlık:** `Collector = None` (varsayılan) veya Hangfire yapılandırılmamışsa özellik kapalıdır ve UI bunu
  dürüstçe söyler; `Fake` yalnız Development/Demo/Test ortamında kabul edilir ve kanıttaki çalıştırma beyanı "SAHTE" der.
- **Denetim** (R2-5): `UsageScanJobRequested` / `…Completed` / `…Cancelled` / `…Failed` sayılarla ve plan özetiyle; sunucu adları
  iş kaydında, denetim metninde değil.

## 5. Yapılandırma

```json
"ServiceAccounts": {
  "UsageScanJobs": {
    "Collector": "None",          // None | Fake | Jea
    "MaxParallel": 32, "OpenTimeoutSeconds": 15, "OperationTimeoutSeconds": 180,
    "MaxDocumentBytes": 524288, "LeaseSeconds": 300,
    "FakeDelayMilliseconds": 0    // yalnız Fake, yerel gösterim için
  }
}
```

## 6. Açık konular

- Worker erişim yolu: sahip kararı 2026-10-13 doğrudan WinRM + Kerberos + JEA; AGENTS.md "Open, do not assume" satırı ve
  ADR-0024 R2-6 güncellemesi TARAMA-C'de (Codex) yapılacak.
- Bilgi Güvenliği / sunucu sahibi onayı: sahip kararına göre script zaten onaylı, ayrı tarama onayı aranmıyor; JEA uç noktası
  kaydı ve okuma gMSA'sının kurulumu yine kurum sürecine tabidir (TARAMA-C).
- `NoEndpoint` vb. ayrıntılı nedenin 030 kanıt tablosuna eklenmesi (R2-4 "schema addition") bu dalda yapılmadı: 030'daki CHECK
  değişirdi. Ayrıntı 037 iş tablosunda; gerekirse ayrı karar.
