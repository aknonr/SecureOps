# OR-2 — Onaylı gMSA planından OR açma (yalnız sahte adaptör) — rapor

Tarih: 2026-10-13. Dal: `feature/service-accounts-or-creation-20261013` (master `e79ac0b` + `origin/docs/service-accounts-or-creation-adr-20261012`
merge edildi, rebase yok). PR açılmadı, merge edilmedi. **ADR-0030 *Proposed* kalır**; bu iş onu kabul edilmiş saymaz. ADR metni "uygulama kabulden
sonra başlar" diyor; sahip bu kez yalnız sahte adaptörle uygulamayı istedi (AGENTS kural 8: önce sahte adaptör). Bu çelişki sahibin kararıdır, ADR'ye
yazılmadı.

## Ne değişti

- **SQL 035 (SA-008):** `svcacct.ChangePlanRecordSubmissions` (plan hesabı başına her "OR aç" denemesi; uzak çağrıdan **önce** `Sending` yazılır,
  gönderilen kısa açıklama/açıklama, ortam, TH kullanıcı kimliği, parmak izi, işlem anahtarı saklanır; içerik değişmez, silinmez — 51403; yalnız
  izinli geçişler — 51404; plan+hesap başına tek canlı gönderim ve OR numarası başına tek canlı sahip — filtreli tekil indeksler) ve
  `ChangePlanRecordSubmissionEvents` (ekleme-yalnız — 51402). Tekrar/eksik ön koşul 51400. API rolüne SELECT/INSERT + yalnız sonuç sütunlarında
  UPDATE (`SA-008-API-permissions.sql`). `DBA-035-TR.md` (ön kontrol, uygulama, son kontrol, geri dönüş), harness'a 035 adımı,
  `sa-035-guards.sql`, `sa-035-dba-checks.sql`. Mevcut tablo/kısıt değişmedi; `OcoNumber` **dokunulmadı** (ADR-0030 §10 açık).
- **Sözleşme (yeni klasör `Infrastructure/ItsmRequests/`):** modülden bağımsız `IItsmRequestCreator`; ortak ekleme kuralı `ItsmInsertRule`
  (vendor `Success=true` + geçerli `InsertedId` = oluştu; yanında dolu hata metni **yok sayılır**; diğer her şey Belirsiz); `FakeItsmRequestCreator`
  (deterministik, ağ yok, açıklamadaki `[fake:…]` işaretiyle her durum denenir) ve `DisabledItsmRequestCreator`. Yapılandırma
  `ItsmRequestCreation:Provider` varsayılan **Disabled**; Fake yalnız Development/Demo/Test; `TuruncuHat` başlangıçta reddedilir (adaptör yok);
  `DispatchTimeoutSeconds` 5–300 (varsayılan 60). `ItsmRecords/` ve `TuruncuHat*` dosyalarına dokunulmadı.
- **Domain/servis/API:** `OperationalRecordRules` (durumlar, şablon, metin kuralları, parmak izi), `OperationalRecordFlow` (anahtar tekrarı ilk sonucu
  döndürür, başka içerik 409; `Sending` ayrı işlemde; tek gönderim, tarayıcıdan bağımsız süre sınırı; zaman aşımı/istisna = Belirsiz; sonuç
  yazılamazsa satır `Sending` kalır; **asla otomatik tekrar yok**). Yeni rotalar (`ServiceAccountOperationalRecordsController`):
  `GET change-plans/{id}/operational-records`, `POST …/accounts/{accountId}/operational-record/preview`, `POST …/operational-record`,
  `POST …/candidate`, `POST …/link`, `POST …/not-created`, `GET …/tracking`. Komutlar `Work` + planın tüm hesaplarında sorumlu dayanak (sahip
  talimatı; ADR'nin önerdiği yeni yetenek eklenmedi), okumalar `View`; kapsam dışı = 404. Bildiren: `IItsmUserMapping.MatchAsync(Uid, LoginName)`;
  **Matched + konum dışında her sonuç durdurur** (önizleme gönderilemez der, gönderim 409, hiçbir şey yazılmaz). Mutabakat: numara ADR-0031
  aramasıyla kontrol edilir; Found = doğrulanmış bağ, diğerleri "doğrulanamadı" (asla "yok"). İzleme ADR-0032 ile canlı okunur, saklanmaz; bilinmeyen
  alan null.
- **OpenAPI:** yeniden üretilmedi; yerelde üretilen belgeden **yalnız** 7 yol + 15 şema eklendi (diff yalnız ekleme, 1762 satır; üretilenle birebir).
- **UI:** plan sayfasında onaydan sonra `SaPlanOperationalRecords`: hesap kartları, "OR aç" → önizleme (eşleme rozeti, ortam, düz metin, sabit plan
  referans satırı, alanların kaynağı) → **ayrı** "Bu içerikle OR aç" (yalnız tam bu metnin güncel önizlemesiyle etkin); cevap gelmezse "Aynı anahtarla
  yeniden dene"; Belirsiz/Kısmen dürüst rozet + aranacak satır + mutabakat formu; izlemede her bilinmeyen "bilinmiyor"; Fake her yerde "sentetik".
  Yetki yalnız sunucunun `canOpen/canLink/canConfirmNotCreated` cevabından.

## Nasıl doğrulandı (ve ne doğrulanmadı)

Linux bulut oturumu; repo SDK 9.0.317 ister, burada indirilemedi (proxy 403). **SDK 8.0.131 ile ayrı bir kopyada** derlendi (kopyada `global.json`
gevşetildi ve SDK 8 derleyicisinin belirsiz bulduğu tek bir mevcut satır yalnız kopyada `new[]` yapıldı; repoya yansımadı).

- `dotnet build SecureOps.sln`: 0 uyarı, 0 hata.
- Tam birim: 2716 testten 2713 geçti, 3 düştü — üçü de ortamdan (SDK sürüm sabitlemesi, Linux'ta PowerShell yürütme ilkesi, yayın dizini yolu);
  aynı üçü bu ortamda master'da da düşüyor. Yeni: `OperationalRecordTests` (birim, akış, Fake, kayıt, eşleme), `ServiceAccountOperationalRecordUiTests`
  (19), `SqlAssetContractTests` 035 testi.
- API kompozisyon (`ServiceAccountApiCompositionTests`) 15/15 ve OpenAPI anlık görüntü testi geçti. Tam entegrasyon: 596 testten 380 geçti, 208
  atlandı (SQL/ortam gerektirenler, yenileri dahil), 7 düştü — hepsi `AnnouncementTests` görsel/codec testleri; aynı 7'si bu ortamda master'da da
  düşüyor. Bir koşuda `AnnouncementSmtpTests.RealSmtp_…(DataRejected)` de düştü, iki tekrarında geçti (zamanlamaya bağlı, bu işle ilgisiz).
- `dotnet format SecureOps.sln --verify-no-changes`: Windows çekimini taklit için kopyada CRLF'e çevrildi; **değişen satırlarda bulgu yok** (iki `var`
  bulgusu düzeltildi). Kalan bulgular dokunulmamış satırlarda ve SDK 8 biçimleyicisinden; master kopyasında da aynısı çıktı (aşağıda). Kesin kapı Windows'ta SDK 9.0.317 ile koşulmalı.
- **Koşulmadı:** SQL harness ve `sa-035-guards.sql` (SQL Server yok), `ServiceAccountOperationalRecordSqlTests` (9 test; yalnız derlendiği doğrulandı,
  burada "atlandı"), tarayıcı yolculuğu, %200 yakınlaştırma/ekran okuyucu/koyu tema gözle, Windows/IIS, kurumsal TEST. Koordinatör Windows'ta:
  `sa-sql-harness.ps1 -DatabaseSuffix <ek>` → `SECUREOPS_SA_SQL_TEST_CONNECTION` ile entegrasyon testleri; `-ThroughMigration 34` kopyasında
  DBA-035 adımları.

Format karşılaştırması: aynı SDK 8 + CRLF koşulunda master kopyası **346** bulgu, bu dal **346** bulgu verdi (aynı dosya/satırlar); bu işin eklediği
bulgu yok.

## Gerçek adaptör için Turuncu Hat ekibinden beklenenler

1. Arındırılmış örnek cevaplar: `SMSS_oRFF` insert (başarı, hata, `Success=true` + `InsertedId` + dolu hata metni), `DCM_DynamicCase` sorgusu,
   `DCM_DynamicCaseProperty` insert; 4xx/`Success=false` cevabının "hiçbir şey yazılmadı" anlamına gelip gelmediği (gelirse `NotSent`/kesin ret durumu
   ADR değişikliğiyle eklenir; o zamana kadar Belirsiz).
2. OR numarasının iç kimlikle geri okunma yolu ve alan adı.
3. Ortam ve "Jira proje kapsamı" şablon özellik kimlikleri ve izinli değerleri (şimdilik yapılandırmada `Test/Prod` yer tutucu).
4. Kısa açıklama azami uzunluğu; düz metnin HTML-encode edilip edilmediği.
5. `p_rel_organization_id` ve `p_phone_number` zorunlu mu, kaynağı ne.
6. Entegrasyon hesabının başka bir kişiyi `p_rel_requester`/`p_rel_affected_end_user` yapabildiğinin kanıtı (Bilgi Güvenliği onayıyla).
7. Katalog/aciliyet/öncelik kodları (sunucu yapılandırması; repoya girmez).

## Açık sorular (sahip)

1. ADR-0030'un kabulü; kabul edilmeden gerçek adaptör yazılmaz ve açılmaz.
2. Yetki: şimdilik `Work`; ADR-0030 yeni `ServiceAccounts.CreateOperationalRecord` (yalnız korumalı Admin paketi) öneriyor. Hangisi?
3. `ChangePlanApprovals.OcoNumber` isteğe bağlı olsun mu (ADR-0030 §10, açık soru 2)? 035'te değiştirilmedi; gerekirse ayrı ve küçük bir migration.
4. "Açılmadı" teyidini aynı yetki mi (şimdiki) yoksa `Verify` mi versin (ADR açık soru 6)?
5. Ortam önerisi: planda ortam alanı yok; şimdilik kişi seçmeden gönderemez. Plandan öneri istenirse kaynağı ne?
6. İzleme sonucunun saklanması (ADR §6 "son sonucu gösterim için saklanır"): OR-2'de saklanmadı, her "yenile" canlı okur. Saklansın mı (Worker ile
   periyodik yenileme sonraki adım)?

## Sonraki adım

Koordinatör Windows'ta harness (001–036) + `sa-035-guards.sql` + `ServiceAccountOperationalRecordSqlTests` + tam birim/format (SDK 9.0.317) koşar;
yerel Demo'da `ItsmRequestCreation:Provider=Fake` ve `ItsmRecordLookup:Provider=Fake` ile tarayıcı yolculuğu (390 px, klavye, koyu tema). Sonra sahip
ADR-0030 kararını ve yukarıdaki soruları verir; TH cevapları gelince gerçek adaptör ayrı PR'da, `ReadOnlyIntegrationMode` çitinin altında yazılır.
