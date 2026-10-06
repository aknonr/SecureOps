# Toplu gMSA geçiş planı ve rehberli manuel değişiklik — tasarım (d)

Durum: **tasarım, onaylı kararlarla (sahip, 2026-10-06); kod yok.** Kaynak: `ops-research/04-tasarim-secenekleri.md`
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

## Akış

| Adım | Ne olur | Kim | Kural |
|---|---|---|---|
| 1. Plan (taslak) | 1–20 hesap; hesap başına hedef gMSA adı (031 kuralı, 15 karakter) | `Work` + her hesapta sorumlu dayanak | Kapsam dışı / katılımcı hesap reddedilir (hesap bazında sonuç) |
| 2. Önizleme | Her hesabın **son** Discovery taramasından bileşen satırları: sunucu, tür, ad, mevcut kimlik, hedef kimlik, işaret | Sistem (`Work` ile istenir) | Sürüm + SHA-256 özet; plan değişirse yeni sürüm, eski onay geçersiz |
| 3. Onay | OCO numarası, bakım penceresi (başlangıç–bitiş), gerekçe | `Verify`, planlayan değil | Belirli önizleme sürümüne ve özete bağlı; bayat ise 409 |
| 4. Rehberli uygulama | Bileşen başına `Done / Skipped / Failed / RolledBack` + not; kanıt dosyası | `Work` (onaydan sonra) | Ekleme-yalnız; son işaret geçerli; sistem hiçbir sunucuya bağlanmaz |
| 5. Doğrulama ve kapanış | gMSA kontrol taraması yüklenir (mevcut yol); plan kapatılır; hesap başına `GmsaConversion` **Performed** eylemi açık kullanıcı komutuyla kaydedilir | `Work`; doğrulama `Verify` (mevcut akış) | Kapanış hesabı doğrulanmış saymaz; doğrulama ayrı |
| İptal | Gerekçeyle, onaydan önce veya sonra | `Work` (planlayan) veya `Verify` | Kayıtlar kalır |

**Önizleme satır işaretleri:** `Ok` · `StaleScan` (tarama 7 günden eski) · `NoScan` (hesapta Discovery taraması yok;
hesap için tek "bilgi yok" satırı) · `NotCovered` (sunucu taranamadı/kısmi: bileşen listesi eksik olabilir) ·
`ManualOnly` (IIS sanal dizin "connect as", COM+: gMSA ile desteklenmiyor veya belirsiz) · `NameTooLong` (031 kuralı).
Hiçbir işaret planı engellemez; onay ekranında sayılarıyla gösterilir.

**Durumlar:** `Draft → Previewed → Approved → InProgress → Completed`, her durumdan `Cancelled`. Önizleme sonrası plan
değişirse `Draft`'a döner.

## Migration 032 (yalnız ekleme; a bu numarayı kullanmaz)

`svcacct` şemasında; geçmiş + `audit.AuditLog` her geçişte aynı işlemde. Silme/güncelleme yalnız `ChangePlans.Status`,
`RowVer` ve `UpdatedAt/By` için; diğer tablolar ekleme-yalnız.

| Tablo | Kolonlar (özet) |
|---|---|
| `ChangePlans` | Id, Kind (`GmsaConversion`), Title, Status, CurrentPreviewVersion, CreatedBy/At, UpdatedBy/At, RowVer |
| `ChangePlanAccounts` | PlanId, AccountId, TargetGmsaName nvarchar(256), RequestId NULL (bağlı açık talep), AddedAt; PK (PlanId, AccountId) |
| `ChangePlanPreviews` | Id, PlanId, Version, Sha256, ScanFreshDays (7), CreatedBy/At; UQ (PlanId, Version) |
| `ChangePlanItems` | Id, PreviewId, AccountId, ScanLinkId NULL, ServerName, ComponentType, ComponentName, CurrentIdentity, TargetIdentity, Flag, ScanAt NULL |
| `ChangePlanApprovals` | Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy/At |
| `ChangeItemChecks` | Id, ItemId, State, Note NULL, CheckedBy/At (ekleme-yalnız; son satır geçerli) |
| `ChangePlanEvents` | Id, PlanId, Event, FromStatus, ToStatus, Reason NULL, Actor, At |

Kısıtlar: `ApprovedBy <> ChangePlans.CreatedBy` hem serviste hem tetikleyici/CHECK ile; `WindowEnd > WindowStart`;
`OcoNumber` mevcut OCO referans biçimi. Yeni rol/izin yok: `svcacct_api_runtime` yeni tablolarda SELECT/INSERT (+ Plans için
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
| `POST change-plans/{id}/items/{itemId}/checks` | Work | `{ state, note }`; onay öncesi 409 |
| `POST change-plans/{id}/complete` | Work | Hesap başına `GmsaConversion` Performed eylemi (mevcut eylem kaydı yolu) |
| `POST change-plans/{id}/cancel` | Work (planlayan) / Verify | `{ reason }` |

Hata kodları mevcut `SaErrors` deseninde; OpenAPI anlık görüntüsü yalnız ekleme; route envanteri testine eklenir.

## Arayüz

- `/service-accounts/plans`: liste (durum, hesap sayısı, OCO, pencere); hesap listesinde seçili hesaplardan "gMSA geçiş
  planı oluştur".
- `/service-accounts/plans/{id}`: adım göstergesi (Plan → Önizleme → Onay → Kontrol listesi → Doğrulama), her adımda neyin
  eksik olduğu açıkça; önizleme tablosu işaretlerle (renk tek başına sinyal değil); onay ekranı planlayana gösterilmez,
  sunucu da reddeder; kontrol listesi sunucu → bileşen gruplu, sayfalı; kanıt yükleme.
- Hesap sayfasında: hesabın bağlı olduğu açık plan bağlantısı. "Sıradaki adım" kartına "Onay bekleyen plan" adımı.
- 390 px, %200 yakınlaştırma, klavye, açık/koyu; ortak bileşenler (`SoPageHeader`, `SoProblemPanel`, `SoEmptyState`,
  `SoStatusBadge`).

## Testler

- SQL harness 001–032 (ikinci çalıştırma reddedilir; 031'de bırakılmış kopyaya ileri uygulama).
- Entegrasyon: planlayan onaylayamaz (servis ve DB kısıtı); bayat önizleme 409; kısmi kapsamlı plan görünmez; `NoScan`,
  `StaleScan`, `NotCovered`, `ManualOnly` işaretleri; reddedilen istek hiçbir şey yazmaz; onay öncesi işaret 409; kapanış
  eylemleri yazar ama doğrulamaz; iptal kayıtları korur; yanıtlarda parola/gizli alan yok.
- Birim: önizleme işaret kuralları (saf domain), adım göstergesi, 390 px yerleşim testleri; route envanteri; OpenAPI.
- Yerel Demo (kendi portunda): 390 px açık/koyu, klavye.

## Üç PR

1. **032 + plan/önizleme/onay API** (domain kuralları, SQL, servis, controller, OpenAPI, DBA kılavuzu eki). ~3 gün.
2. **Kontrol listesi + kanıt + kapanış/iptal** (API ve testleri). ~2 gün.
3. **Arayüz** (liste, plan sayfası, hesap sayfası bağlantısı, sıradaki adım). ~2–3 gün.

Her PR ayrı dal, küçük commit'ler, build/test/format/SQL harness; push edilir, merge sahibe sorulur.

## Açık sorular

1. OCO numarası biçimi: mevcut `SaExternalRef` "OCO" doğrulaması yeterli mi, yoksa ITSM'den doğrulama mı (entegrasyon yok;
   şimdilik biçim)?
2. Bakım penceresi geçtikten sonra kontrol listesi işaretlenebilir mi? Öneri: evet, ama "pencere dışında" etiketiyle.
3. Bir hesap aynı anda iki açık planda olabilir mi? Öneri: hayır (ikinci plan o hesabı reddeder).
4. Plan kapanışında bağlı açık talep (`RequestId`) otomatik kapanmasın; kişi mevcut kapatma akışıyla kapatır (öneri).
