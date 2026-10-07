# Toplu gMSA geçiş planı ve rehberli manuel değişiklik — tasarım (d)

Durum: **PR 1 dalda (2026-10-07, `feature/service-accounts-change-plan-api-20261007`, merge edilmedi): 033 şeması, plan/önizleme/
onay/iptal ve okuma uçları; kontrol listesi ve kapanış PR 2.** Sahip kararları 1–7 (2026-10-06). Kaynak: `ops-research/04-tasarim-secenekleri.md`
(Karar 2 / M1, toplu işlem akışı), `ops-research/05-yol-haritasi.md` A6–A7. Modül sahibi Claude (2026-10-03 kararı).

## Sınırlar (değişmez)

- **Sunucuya yazma yok.** AGENTS.md kural 1 ve 3 değişmez; ADR-0028 *Proposed* kalır. Değişikliği kişi kendi yetkisiyle,
  normal değişiklik kaydıyla (OCO) yapar. Sistem yalnız **plan, önizleme, onay, kişinin işaretlediği kontrol listesi ve
  kanıt** tutar.
- **Parola alanı hiçbir yerde yok** (istek, yanıt, tablo, günlük). Parola değişimi PAM'ın işi; parola planı türü yok.
- "Yapıldı" işareti kişinin beyanıdır, doğrulama değildir. Doğrulama: gMSA kontrol taraması (ADR-0027) + mevcut `Verify`
  eylem doğrulaması.
- "Bulunamadı ≠ kullanılmıyor"; tarama kapsamadığı bileşen "bilgi yok". Sunucu yetkiyi belirler; UI yalnız yansıtır.
- Yalnız sentetik test verisi. Kurulu TEST sistemine migration uygulanmaz (DBA kılavuzuna ek yazılır).

## Sahip kararları (2026-10-06)

1. Onay yetkisi: mevcut **`ServiceAccounts.Verify`**; **onaylayan ≠ planlayan** (sunucu reddeder). Yeni yetenek yok,
   rol paketleri değişmez.
2. Tek plan türü: **gMSA'ya geçiş** (`GmsaConversion`). Parola planı yok.
3. Tarama tazeliği **7 gün**: daha eski taramadan gelen satır "eski bilgi" işaretlenir; plan **engellenmez**.
4. OCO numarası **yalnız biçim** olarak doğrulanır (mevcut `SaExternalRef` "OCO" kuralı); ITSM'e sorgu yok.
5. Bakım penceresi geçtikten sonra da kontrol listesi **işaretlenebilir**; pencere dışında yapılan işaret kayıtta ve
   arayüzde **"pencere dışında"** etiketini taşır (engellenmez).
6. Bir hesap aynı anda **yalnız bir açık planda** olabilir (açık = `Completed`/`Cancelled` dışı); ikinci plan o hesabı
   hesap bazında reddeder (`accountInOpenPlan`).
7. Plan kapanınca bağlı açık talep (`RequestId`) **otomatik kapanmaz**; kişi mevcut talep kapatma akışını kullanır.

## Akış

| Adım | Ne olur | Kim | Kural |
|---|---|---|---|
| 1. Plan (taslak) | 1–20 hesap; hesap başına hedef gMSA adı (031 kuralı, 15 karakter) | `Work` + her hesapta sorumlu dayanak | Kapsam dışı / katılımcı hesap ve başka açık plandaki hesap reddedilir (hesap bazında sonuç) |
| 2. Önizleme | Her hesabın **son** Discovery taramasından bileşen satırları: sunucu, tür, ad, mevcut kimlik, hedef kimlik, işaret | Sistem (`Work` ile istenir) | Sürüm + SHA-256 özet; plan değişirse yeni sürüm, eski onay geçersiz |
| 3. Onay | OCO numarası (yalnız biçim), bakım penceresi (başlangıç–bitiş), gerekçe | `Verify`, planlayan değil | Belirli önizleme sürümüne ve özete bağlı; bayat ise 409 |
| 4. Rehberli uygulama | Bileşen başına `Done / Skipped / Failed / RolledBack` + not; kanıt dosyası | `Work` (onaydan sonra) | Ekleme-yalnız; son işaret geçerli; pencere dışındaki işaret `OutsideWindow` ile kaydedilir, engellenmez; sistem hiçbir sunucuya bağlanmaz |
| 5. Doğrulama ve kapanış | gMSA kontrol taraması yüklenir (mevcut yol); plan kapatılır; hesap başına `GmsaConversion` **Performed** eylemi açık kullanıcı komutuyla kaydedilir | `Work`; doğrulama `Verify` (mevcut akış) | Kapanış hesabı doğrulanmış saymaz; doğrulama ayrı; bağlı talep açık kalır (kişi kapatır) |
| İptal | Gerekçeyle, onaydan önce veya sonra | `Work` (planlayan) veya `Verify` | Kayıtlar kalır |

**Önizleme satır işaretleri:** `Ok` · `StaleScan` (tarama 7 günden eski) · `NoScan` (hesapta Discovery taraması yok;
hesap için tek "bilgi yok" satırı) · `NotCovered` (sunucu taranamadı/kısmi: bileşen listesi eksik olabilir) ·
`ManualOnly` (IIS site/uygulama/sanal dizin "connect as", COM+: gMSA ile desteklenmiyor veya belirsiz) · `NameTooLong` (031 kuralı;
ad oluşturma/PATCH'te zaten reddedildiği için pratikte çıkmaz) · `NothingFound` (PR 1'de eklendi: son tarama cevap verdi ama
hesabı taranan hiçbir sunucuda bulmadı; hesap onaylayana görünsün diye tek satır, "kullanılmıyor" anlamına gelmez).
Hiçbir işaret planı engellemez; onay ekranında sayılarıyla gösterilir. Satırda tek işaret vardır, öncelik: `ManualOnly` >
`NotCovered` > `StaleScan` > `NameTooLong` > `Ok`.

**Durumlar:** `Draft → Previewed → Approved → InProgress → Completed`, her durumdan `Cancelled`. Önizleme sonrası plan
değişirse `Draft`'a döner.

## Migration 033 (yalnız ekleme; a bu numarayı kullanmaz)

032 Access'e aittir (G-34, `032-access-request-user-index.sql`, AccessRequests indeksi; Access sahibinin işi, master'a
girer). Servis Hesapları'nın sıradaki numarası 033. DBA kılavuzu sırası: **029 → 030 → 031 → 032 (Access) → 033**.

`svcacct` şemasında; geçmiş + `audit.AuditLog` her geçişte aynı işlemde. Silme/güncelleme yalnız `ChangePlans.Status`,
`RowVer` ve `UpdatedAt/By` için; diğer tablolar ekleme-yalnız.

| Tablo | Kolonlar (özet) |
|---|---|
| `ChangePlans` | Id, Kind (`GmsaConversion`), Title, Status, CurrentPreviewVersion, CreatedBy/At, UpdatedBy/At, RowVer |
| `ChangePlanAccounts` | Id (PK), PlanId, AccountId, Action (`Added`/`Removed`/`Renamed`), TargetGmsaName nvarchar(256), RequestId NULL (bağlı açık talep), At, By (ekleme-yalnız; geçerli hesap kümesi her hesabın son satırından okunur; indeks (PlanId, AccountId, At)) |
| `ChangePlanPreviews` | Id, PlanId, Version, Sha256, ScanFreshDays (7), CreatedBy/At; UQ (PlanId, Version) |
| `ChangePlanItems` | Id, PreviewId, AccountId, ScanLinkId NULL, ServerName, ComponentType, ComponentName, CurrentIdentity, TargetIdentity, Flag, ScanAt NULL |
| `ChangePlanApprovals` | Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy/At |
| `ChangeItemChecks` | Id, ItemId, State, Note NULL, OutsideWindow bit, CheckedBy/At (ekleme-yalnız; son satır geçerli) |
| `ChangePlanEvents` | Id, PlanId, Event, FromStatus, ToStatus, Reason NULL, Actor, At |

PR 1 uygulamasındaki farklar: `ChangePlanAccounts`, `ChangeItemChecks`, `ChangePlanEvents` kimlikleri `bigint IDENTITY` (son satır =
en büyük Id); `ChangePlanAccounts` kolonları `ChangedBy/ChangedAt` (`BY` ayrılmış sözcük); önizlemede `ItemCount`; ön koşul
025 + 030 + 031; hata numaraları 51390 (ön koşul/tekrar), 51391 (izin betiği), 51392 (salt eklenir), 51393 (onay ayrımı ve
önizleme bağı), 51394 (`ChangePlans` sabit kolon/silme). Kanıt dosyaları için `Evidence.OwnerEntityType` CHECK'i bugün
`ChangePlan` değerlerini kabul etmiyor; 033 mevcut kısıtı değiştirmediği için bu PR 2'nin açık sorusudur.

Kısıtlar: `ApprovedBy <> ChangePlans.CreatedBy` hem serviste hem tetikleyici/CHECK ile; `WindowEnd > WindowStart`;
`OcoNumber` mevcut OCO referans biçimi. "Bir hesap yalnız bir açık planda" kuralı serviste, aynı işlemde hesap
satırları kilitlenerek (`UPDLOCK, HOLDLOCK`) denetlenir (durum `ChangePlans`'ta olduğu için filtreli tekil indeks yok). Yeni rol/izin yok: `svcacct_api_runtime` yeni tablolarda SELECT/INSERT (+ Plans için
UPDATE). Tekrar çalıştırmayı reddeder; geri alma betiği yok. Kanıt dosyaları mevcut `Evidence` tablosunda (`OwnerEntityType`
`ChangePlan` / `ChangePlanItem`).

## API (modül, yalnız ekleme; `/api/v1/service-accounts`)

| Yöntem ve yol | Yetki | Not |
|---|---|---|
| `GET change-plans?status=&page=` | View | Yalnız **tüm** hesapları kapsamında olan planlar (kısmi kapsamda görünmez) |
| `GET change-plans/{id}` | View | Aynı kural; dışarıdaysa 404 |
| `POST change-plans` | Work | `{ title, accounts: [{ accountId, targetGmsaName }] }`; hesap bazında sonuç |
| `PATCH change-plans/{id}` | Work | Hesap ekle/çıkar, ad değiştir (`expectedVersion`); `Previewed`'dan `Draft`'a döner |
| `POST change-plans/{id}/preview` | Work | Yeni önizleme sürümü + özet + işaret sayıları |
| `POST change-plans/{id}/approve` | Verify | `{ previewVersion, sha256, ocoNumber, windowStart, windowEnd, reason }`; planlayan → 403 `approverIsPlanner`; bayat → 409 `previewStale` |
| `POST change-plans/{id}/items/{itemId}/checks` | Work | `{ state, note }`; onay öncesi 409; pencere dışındaysa kabul edilir, `outsideWindow: true` döner |
| `POST change-plans/{id}/complete` | Work | Hesap başına `GmsaConversion` Performed eylemi (mevcut eylem kaydı yolu); bağlı talep kapanmaz |
| `POST change-plans/{id}/cancel` | Work (planlayan) / Verify | `{ reason }` |

Hata kodları mevcut `SaErrors` deseninde; OpenAPI anlık görüntüsü yalnız ekleme; route envanteri testine eklenir.

### Sabitlenen sözleşme (PR 1, 2026-10-07)

DTO'lar `src/SecureOps.Shared/Contracts/ServiceAccounts/ServiceAccountChangePlanContracts.cs`, rotalar
`ServiceAccountChangePlansController`, OpenAPI `docs/contracts/secureops-api-v1.openapi.json`. Tablodan farklar ve netleşenler:

- `GET change-plans?status=&accountId=&page=&pageSize=` (hesap sayfası bağlantısı için `accountId`); önizleme satırları
  ayrı ve sayfalı: `GET change-plans/{id}/items?page=&pageSize=` (yalnız planın **güncel** önizlemesi).
- `PATCH` hesap listesinin **tamamını** alır (`{ expectedVersion, accounts }`); sunucu farkı ekleme/çıkarma/ad değişikliği
  satırlarına çevirir. Başlık değişmez (T8). `preview` ve `cancel` de `expectedVersion` taşır (T4); `approve` önizleme
  sürümü + özetle bağlanır.
- `cancel` rotada `View`; planlayan (`Work`) veya `Verify` olduğu serviste karar verilir.
- Hata kodları: `ServiceAccountChangePlanAccountsRefused` (400; `current` = hesap bazında sonuç, kapsam dışı hesabın adı
  yok), `ServiceAccountChangePlanStateConflict` (409; `field` = `previewStale` / `planNotPreviewed` / `planNotEditable` /
  `planClosed`), 403 `ServiceAccountAccessDenied` + `field` = `approverIsPlanner` / `approverChangedPlan` / `scope`,
  `ServiceAccountChangePlansNotInstalled` (503; 033 kurulu değil). Bayat `expectedVersion` mevcut 409
  `ServiceAccountConcurrencyConflict`.
- Kontrol listesi ve kapanış rotaları (`items/{itemId}/checks`, `complete`) PR 2'de eklenir.

## Arayüz

- `/service-accounts/plans`: liste (durum, hesap sayısı, OCO, pencere); hesap listesinde seçili hesaplardan "gMSA geçiş
  planı oluştur".
- `/service-accounts/plans/{id}`: adım göstergesi (Plan → Önizleme → Onay → Kontrol listesi → Doğrulama), her adımda neyin
  eksik olduğu açıkça; önizleme tablosu işaretlerle (renk tek başına sinyal değil); onay ekranı planlayana gösterilmez,
  sunucu da reddeder; kontrol listesi sunucu → bileşen gruplu, sayfalı, pencere dışı işaretler "pencere dışında"
  metniyle (yalnız renk değil); kanıt yükleme; kapanışta "bağlı talep açık kalır, talepten kapatın" notu.
- Hesap sayfasında: hesabın bağlı olduğu açık plan bağlantısı. "Sıradaki adım" kartına "Onay bekleyen plan" adımı.
- 390 px, %200 yakınlaştırma, klavye, açık/koyu; ortak bileşenler (`SoPageHeader`, `SoProblemPanel`, `SoEmptyState`,
  `SoStatusBadge`).

## Güvenlik tehdit tablosu (PR 1–2 için bağlayıcı)

Her satırın testi, ilgili PR'da **önce** yazılır; test adı dalda değişirse bu tablo aynı commit'te güncellenir. "SQL" =
`ServiceAccountChangePlanSqlTests` (gerçek `svcacct` şeması, harness 001–033, sentetik veri), "Birim" = saf domain testi,
"API" = `ServiceAccountApiCompositionTests`, "Harness" = `sa-sql-harness.ps1` adımı.

| # | Tehdit | Kontrol (sunucu + veritabanı) | Yakalayan test |
|---|---|---|---|
| T1 | **Bayat onay:** önizlemeden sonra plan değişir (hesap/ad eklenir, çıkarılır) ama onay eski önizlemeye verilir | Onay isteği `previewVersion` + `sha256` taşır; onay işleminde plan satırı `UPDLOCK` ile kilitlenir, durum `Previewed`, `CurrentPreviewVersion = previewVersion` ve kayıtlı özet = istekteki özet değilse 409 `previewStale`, hiçbir satır yazılmaz. `PATCH` planı `Draft`'a döndürür, önizleme sürümünü geçersiz kılar | SQL `Approve_AfterPlanChanged_IsStale_AndWritesNothing` (PATCH sonrası eski sürüm/özetle onay → 409; `ChangePlanApprovals`, olay, geçmiş, audit sayıları değişmez) |
| T2 | **Yeniden oynatılan onay:** aynı onay isteği ikinci kez (çift tıklama, ağ tekrarı) veya onaylanmış/iptal edilmiş plana eski istek | Onay yalnız `Previewed` durumundan; `ChangePlanApprovals` üzerinde tekil indeks `UQ (PreviewId)` (bir önizlemeye en çok bir onay); ikinci istek 409 `planNotPreviewed`, tekil indeks yarışta ikinci yazmayı DB'de reddeder | SQL `Approve_Replayed_IsRefused_OneApprovalRow` (ardışık iki aynı istek → 200 + 409, tek onay satırı); SQL `Approve_AfterCancel_IsRefused`; Harness: `UQ (PreviewId)` ihlali doğrudan INSERT ile 2627 verir |
| T3 | **Planlayan = onaylayan atlatma:** planlayan kendi planını onaylar; ya da başka biri planı değiştirir, değiştiren onaylar | Servis: onaylayan ≠ `ChangePlans.CreatedBy` → 403 `approverIsPlanner`. DB: onay INSERT'inde tetikleyici aynı kuralı uygular (servis atlansa da). **Sahip kararı (2026-10-06):** onaylayan, onaylanan önizlemeyi üreten veya plan oluşturulduktan sonra `PATCH` ile değiştiren kişi de olamaz (`ChangePlanEvents.Actor` üzerinden) → 403 `approverChangedPlan` | SQL `Approve_ByPlanner_Is403_AndWritesNothing`; SQL `ApprovalTrigger_RefusesPlannerEvenWhenServiceIsBypassed` (doğrudan INSERT → tetikleyici hatası, satır yok); SQL `Approve_ByEditorOfPlan_Is403`; SQL `Approve_ByPreviewer_Is403` |
| T4 | **Eşzamanlı onay ve iptal** (veya iki onaylayıcı aynı anda): plan hem onaylı hem iptal görünür, iki onay satırı oluşur | Onay, iptal, `PATCH`, önizleme ve kapanış aynı işlemde plan satırını `UPDLOCK, HOLDLOCK` ile kilitler, durumu kilit altında yeniden okur; `RowVer` beklenen sürümle karşılaştırılır; geçiş + olay + geçmiş + audit tek işlemde. Kazanan bir tanedir, diğeri 409 ile güncel görünümü alır | SQL `ConcurrentApproveAndCancel_ExactlyOneWins` (20 tur, `Task.WhenAll`; her turda tam bir başarı, son durum ile `ChangePlanEvents` son satırı tutarlı, en çok bir onay satırı); SQL `TwoApproversAtOnce_OneApprovalRow`; SQL `ConcurrentPlansForOneAccount_OnlyOneHoldsIt` (karar 6); tanılama günlüğünde `Number=1205` olursa test başarısız sayılır |
| T5 | **Kapsamı olmayan hesabın plana sızması:** kapsam dışı/katılımcı hesap plana eklenir; kapsam sonradan daralır; kısmi kapsamlı biri planı görür veya onaylar | Oluşturma ve `PATCH`'te hesap bazında sorumlu dayanak (a'daki `Responsible` kuralı); kapsam dışı hesap `Unavailable`, **adı dönmez**. Önizleme, onay, işaret ve kapanışta çağıranın kapsamı planın **tüm** hesaplarını yeniden kapsamalı; kapsamamıyorsa 404 (yok ile aynı). Liste ve ayrıntı yalnız tüm hesapları kapsanan planları döndürür | SQL `CreatePlan_OutOfScopeAccount_RefusedPerAccount_NoName`; SQL `ScopeShrinksAfterPlan_PreviewApproveUpdateAndCancel_Are404` (işaret kısmı PR 2); SQL `PartialScope_PlanInvisibleInListAndDetail`; SQL `List_AccountWithoutOrganizationOrOwner_CountsAsOutsideScope`; SQL `ParticipantBasis_CannotAddAccount` |
| T6 | **IDOR:** başka planın kalemi (`itemId`), başka planın önizlemesi veya hesap kimliği tahmin edilerek işlem | Her rota kaynağı üst kaynağına bağlı okur: kalem `ChangePlanItems → ChangePlanPreviews.PlanId = {id}` ve **planın güncel onaylı önizlemesine** ait olmalı; değilse 404. Kanıt yükleme/indirme mevcut `Evidence` kapsam denetiminden geçer, sahip varlık türü + kimlik plana bağlı doğrulanır. 404 gövdesi var olan/olmayan ayrımı yapmaz | PR 1: SQL `Items_AreOnlyThePlansCurrentPreview`; SQL `Get_PlanOutsideScope_Is404_SameBodyAsMissing`; PR 2: SQL `Check_ItemOfAnotherPlan_Is404_AndWritesNothing`, `Check_ItemOfOlderPreview_Is404`; API `ChangePlanRoutes_PoliciesAndMethods` (her rota doğru politika: View/Work/Verify, yazma rotaları yalnız POST/PATCH) |
| T7 | **Önizleme sürümü/özet doğrulaması:** istemci kendi özetini üretir; aynı içerik farklı sırayla farklı özet verir; kayıtlı kalemler sonradan değişir | Özet yalnız sunucuda, kalemlerin kanonik dizilişinden (sabit alan sırası, `AccountId, ServerName, ComponentType, ComponentName` sıralı, UTF-8, ayraçlı) SHA-256. Onayda istekteki özet kayıtlı özetle **ve** kalemlerden yeniden hesaplanan özetle karşılaştırılır; uyuşmazlık 409 `previewStale` (yeniden hesap farkı ayrıca `logger.LogError`, içerik yazılmadan) | Birim `PreviewDigest_IsCanonical_IndependentOfRowOrder` ve `PreviewDigest_ChangesWhenAnyFieldChanges`; SQL `Approve_WrongSha_Is409`; SQL `Approve_RecomputesDigestFromStoredItems` (yalnız test veritabanında tetikleyici geçici kapatılıp bir kalem değiştirilir; onay 409 alır, onay satırı yazılmaz) |
| T8 | **Ekleme-yalnız kayıtların bozulması:** önizleme kalemi, onay, işaret, olay veya hesap listesi sonradan güncellenir/silinir; `ChangePlans`'ta izinli olmayan kolon değiştirilir | `ChangePlanPreviews`, `ChangePlanItems`, `ChangePlanApprovals`, `ChangeItemChecks`, `ChangePlanEvents` üzerinde UPDATE/DELETE engelleyen tetikleyiciler (modülün mevcut 513xx deseni); `ChangePlans`'ta yalnız `Status`, `CurrentPreviewVersion`, `UpdatedAt/By` (`RowVer` otomatik) güncellenebilir, `Kind`/`CreatedBy`/`CreatedAt` değişimi tetikleyiciyle reddedilir; silme yok. `svcacct_api_runtime` yeni tablolarda yalnız SELECT/INSERT (+ `ChangePlans` UPDATE); DELETE izni yok. Her geçişte audit aynı işlemde | Harness (`sa-033-guards.sql`): her ekleme-yalnız tabloda UPDATE ve DELETE doğrudan denenir → 51392; `ChangePlans.CreatedBy`/`Title` UPDATE ve DELETE → 51394; API rolüyle (`EXECUTE AS USER`) DELETE → 229, depo için gereken her izin var, DELETE izni yok. SQL `FailureBeforeCommit_LeavesNoPlanNoEventNoAudit` (enjekte hata) |

Notlar:
- Tüm 409/403/404 yanıtları planın veya kapsam dışı hesabın içeriğini taşımaz; 409 yalnız çağıranın zaten görebildiği
  güncel görünümü döndürür (modülün mevcut deseni).
- T1–T8 testleri PR 1'de (T6 kalem/işaret ve T8 `ChangeItemChecks` kısmı PR 2'de) yazılır; satır karşılığı yeşil olmadan
  PR açılmaz.

## Testler

- SQL harness 001–033 (ikinci çalıştırma reddedilir; 032'de bırakılmış kopyaya ileri uygulama).
- Entegrasyon: planlayan onaylayamaz (servis ve DB kısıtı); bayat önizleme 409; kısmi kapsamlı plan görünmez; `NoScan`,
  `StaleScan`, `NotCovered`, `ManualOnly` işaretleri; reddedilen istek hiçbir şey yazmaz; onay öncesi işaret 409; pencere
  dışı işaret kabul edilir ve `OutsideWindow` taşır; açık plandaki hesap ikinci planda reddedilir (kapanmış/iptal plandaki
  kabul); OCO biçimi hatalıysa 400; kapanış eylemleri yazar ama doğrulamaz, bağlı talebi kapatmaz; iptal kayıtları korur; yanıtlarda parola/gizli alan yok.
- Birim: önizleme işaret kuralları (saf domain), adım göstergesi, 390 px yerleşim testleri; route envanteri; OpenAPI.
- Yerel Demo (kendi portunda): 390 px açık/koyu, klavye.

## Üç PR

1. **033 + plan/önizleme/onay API** (domain kuralları, SQL, servis, controller, OpenAPI, DBA kılavuzu eki; sıra 029 →
   030 → 031 → 032 (Access) → 033). ~3 gün.
2. **Kontrol listesi + kanıt + kapanış/iptal** (API ve testleri). ~2 gün.
3. **Arayüz** (liste, plan sayfası, hesap sayfası bağlantısı, sıradaki adım). ~2–3 gün.

Her PR ayrı dal, küçük commit'ler, build/test/format/SQL harness; push edilir, merge sahibe sorulur.

## Açık sorular

Yok. Tehdit tablosundan çıkan iki soru 2026-10-06 sahip kararıyla kapandı:

1. **Onaylayanın kapsamı (T3):** Evet. Onaylayan, planı oluşturan kişi olmadığı gibi, onaylanan önizlemeyi üreten veya planı sonradan `PATCH` ile değiştiren kişi de olamaz (`ChangePlanEvents.Actor` üzerinden, 403 `approverChangedPlan`).
2. **`ChangePlanAccounts` ekleme-yalnız (T8):** Evet. Her ekleme, çıkarma ve ad değişikliği yeni satırdır; hiçbir satır güncellenmez veya silinmez. Şema tablosu ve T8 testleri buna göre okunur.
