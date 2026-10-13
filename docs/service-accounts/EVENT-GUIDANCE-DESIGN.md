# "Ne oldu?" olay odaklı yönlendirme — tasarım ve sözleşme (OLAY-1)

Durum: **TASARIM, kod yok (2026-10-13).** Dal `docs/service-accounts-event-guidance-design-20261013`, master `633eed9`. Uygulama
OLAY-2 (sözleşme + backend) ve OLAY-3 (UI) ile yapılır; OLAY-3, F4 (ilk kabul UI düzeltmeleri) merge edilmeden başlamaz (aynı
UI dosyaları). Kaynak: sahibin TEST ilk kabulü isteği. Modül sahipliği Claude'da (`README.md`, 2026-09-28/10-03 kararı).

## 1. Sorun

Hesapta gerçek bir gelişme olunca (örnek: hesap sahibi mail atıp "parola değişti, `OCO-…` ile yapıldı" der) kullanıcı doğru kaydı
bulmak için dört yer arasında gezer: **Yazışmalar** (mail), **Aksiyon/İşlem bildir** (eylem), **Referans** (OR/OCO numarası), **Talep
güncelle/kapat**. Kapatma kuralı (`ServiceAccountRules.ValidateClose`: "Tamamlandı için aynı türde gerçekleşmiş işlem gerekir")
tek bir kod döndürüyor; kullanıcı neyin eksik olduğunu ve nereden giderileceğini bilmiyor.

Hedef: kullanıcı **olayı** seçer (ne oldu?), ürün gereken kayıtları **önizler**, kullanıcı ayrı bir tıklamayla **tek işlemde** kaydeder.
Sistem sorular sorar; kullanıcı hangi ekrana gideceğini düşünmez.

## 2. Sınırlar (değişmez; bu tasarım hiçbirini gevşetmez)

- **Yapay zekâ yok** (AGENTS kural 2): mail ayrıştırma kural/regex ile, deterministik. Sohbet robotu önerilmez (Faz 7, yalnız self-hosted).
- **Bildirildi ≠ doğrulandı** (SPEC kural 3–4, 6, 8). Olay akışı hiçbir koşulda `Verified` sonuç, `VerifiedOn`, doğrulayan veya
  "hesap kapandı" üretmez. Doğrulama mevcut `Verify` akışında (ayrı yetenek, ayrı tıklama, kanıt şart) kalır.
- **Parola değişimi PAM'ındır.** Ürün parola değiştirmez, taşımaz, saklamaz; parola alanı yoktur. Olay akışı yalnız
  "kişi parolanın değiştiğini bildirdi" kaydı tutar; metin ve etiket bunu açıkça söyler. (Ekip lideri sorusu açık: `password-changes-belong-to-pam`
  notu; bu kayıt türü zaten sözlükte `PasswordChange` olarak var, yeni tür eklenmez.)
- **Sunucu yetkiyi ve kuralı belirler** (CLAUDE.md). UI kapatma/doğrulama kuralını **tekrar yazmaz**; sunucudan gelen eksik listesini gösterir (§6).
- **Kural 9**: eylem eklemek talebi kendiliğinden kapatmaz. Olay akışında talep kapatma **ayrı, varsayılanı kapalı, açık bir seçimdir** (§5.4).
- Hepsi mevcut `ServiceAccounts.Work` yeteneği ve kapsam kuralları içinde; yeni yetenek yok. Katılımcı (Participant) yalnız kendi açık talebine yazar.
- **OR/OCO onayı ürünün işi değildir** (sahip kararı 2026-10-13): OR'un onayı Turuncuhat'ta zaten takım liderine düşer. Bu akış OR/OCO'yu yalnız **referans** (numara) olarak kaydeder
  ve durumu (ileride TH-4 `IItsmRecordTracking` ile) **izleme** amaçlı gösterir; onay isteği, onay ekranı, onay takibi veya onay kaydı **tasarlanmaz**. Önizlemede "OR onaylandı" gibi bir iddia yoktur;
  durum bilinmiyorsa "bilinmiyor" denir. S1–S8 varsayılanları bu kararla uyumludur (hiçbiri onay içermez).
- Gerçek ad / hesap / mail içeriği hiçbir örneğe, testte, belgeye girmez; yer tutucu kullanılır (`svc-örnek-01`, `OCO-00000`).

## 3. Olay → kayıt eşlemesi (normatif tablo)

Eylem türü ve kayıt türünü **sunucu olaydan türetir**; istemci bunları göndermez (fazla gönderme / tür kaçırma yüzeyi kapanır).
"Her zaman" = olay seçilince önizlemede zorunlu madde; "isteğe bağlı" = kullanıcı açar; ✗ = bu olay bunu **hiç** üretmez.

| Olay (`EventKind`) | Yazışma (mail) | Eylem (`ActionView`) | Referans (OR/OCO/Jira) | Talep | Doğrulama / kapanış |
|---|---|---|---|---|---|
| `OwnerReplied` — sahip yanıt verdi | Gelen, `Reply`, anlamlı yanıt işareti; **her zaman** | ✗ | isteğe bağlı (mailde geçiyorsa) | bağlı talebin `LastReplyOn` tarihi = olay tarihi (daha ilerideyse ilerletir, geri almaz) | ✗ |
| `PasswordChanged` — parola değişti (kişi bildirdi) | Gelen, `Information`; **mail varsa** (isteğe bağlı) | `PasswordChange`, **Performed**, **Intermediate**, `ActualOn` = olay tarihi, gerçekleştiren = bildiren taraf (takım/kişi seçilirse) | OCO/OR numarası → eyleme ve (varsa) talebe | bağlı `PasswordChange` talebi varsa eyleme bağlanır; kapatma §5.4 | ✗ (PAM işi; ürün doğrulamaz) |
| `AccountDeleted` — hesap silindi (kişi bildirdi) | isteğe bağlı | `Deletion`, **Performed**, **Closure** adayı | **OR numarası şart değil ama eksikse uyarı**: silme kapanışının doğrulanması OR ister (`OrRequiredForDeletion`) | bağlı `Deletion` talebi varsa bağlanır; kapatma doğrulanmış kapanış ister (`VerifiedClosureRequired`) → bu olayla **kapatılamaz** | ✗; hesap kapanmaz. "Doğrulama ayrı: Verify yetkisi + kanıt" notu gösterilir |
| `GmsaConverted` — gMSA'ya geçildi | isteğe bağlı | `GmsaConversion`, **Performed**, **Closure** adayı | OCO (plan/OR bağı varsa OR) | bağlı `GmsaHandover`/`GmsaConversion` talebi bağlanır | ✗; açık değişiklik planı varsa "plan kontrol listesini işaretle" bağlantısı (plan kalemi otomatik işaretlenmez, çünkü kontrol listesi kişi beyanı + pencere kuralı taşır) |
| `OwnerChanged` — sahip değişti | isteğe bağlı | ✗ | ✗ | ✗ | **Yalnız `OwnershipChangeRequest` modu "Propose"** (Proposed, sahip değil). Karar `Assign` yetkisiyle mevcut karar akışında; olay akışı onay vermez |
| `ServiceMoved` — servis taşındı | **her zaman** (olayın kendisini kaydeder) | ✗ (taşınma bir "yapılan işlem" türü değil) | isteğe bağlı | önerilir: yeni `Review` talebi "taşınan servisin kullanımı yeniden taranacak" (kullanıcı onaylarsa) | ✗; kullanım kaydı/tarama değişmez, **"Kullanım ve kural" / "Kullanım taraması" sekmesine bağlantı**. Bulunamadı ≠ kullanılmıyor |
| `Other` — başka | **her zaman** (en az bir kayıt olsun) | ✗ | isteğe bağlı | isteğe bağlı yeni talep (`Evaluate`) | ✗ |

Kararlar ve gerekçe:
- `Matches` kuralı (`requestType == actionType`, gMSA devri ↔ gMSA geçişi, `Evaluate` her şeyle) olduğu gibi kalır; olay akışı yeni eşleşme icat etmez.
  Bu yüzden bağlı talep yoksa eylem `RequestId` olmadan kaydedilir; ürün **talep açmayı önerir ama kendiliğinden açmaz** (kural 9 ruhu).
- `PasswordChanged` `Intermediate` kalır (SPEC kural 5); `Closure` adayı yalnız silme ve gMSA geçişinde (kural 6, `CanCloseAccount`).
- Kişi "doğruladım" derse bile bu olay akışında kaydedilmez; metinde "doğrulama için Doğrulama sekmesi (Verify yetkisi)" yönlendirmesi çıkar.
- Bir olayda eylem bir kez sayılır; aynı olayı iki kez göndermek ikinci eylem üretmez (§8 yeniden oynatma).

## 4. Akış (kullanıcının gördüğü)

Hesap sayfasının üstünde sürekli görünür **"Bir gelişme mi oldu?"** düğmesi (yalnız `Work` yetkisi varsa; yoksa düğme yerine neden yetkisiz
olduğunu söyleyen kısa metin). Adımlı, tek sütun, klavye ile tam kullanılır:

1. **Ne oldu?** — yedi kart (§3), radyo grubu. Her kartta tek cümle açıklama ve "Ürün ne kaydeder" satırı.
2. **Ayrıntılar** — yalnız o olay için gereken alanlar (§5.2 alan tablosu). İsteğe bağlı **"Yanıt mailini yapıştır"** kutusu (§7): öneri üretir, alanları
   kullanıcı düzeltir. Bağlı talep: hesabın açık talepleri arasından, olaya uyanlar önce ("Parola değişimi — Hedef takım …"); yoksa "talebe bağlama".
3. **Önizleme — "Şunlar kaydedilecek"** — satır satır: Yazışma, İşlem bildirimi (etiket: **"Bildirildi — doğrulanmadı"**), Referans, Talep değişikliği; hemen altında
   **"Kaydedilmeyecek"**: Doğrulama (yapılmadı), Hesap kapanışı (yapılmadı), Talep kapatma (seçilmedi) vb. Uyarılar ("OR numarası yok: silme sonradan doğrulanamaz").
   Yetkisiz bir madde varsa o satır "yapılamaz: yetki" gösterir ve **Kaydet kapalı kalır** (tümü ya hiçbiri).
4. **Onay** — ayrı **"Kaydet"** düğmesi (önizleme adımının son kontrolü; Enter ile geçmez, odak ilk olarak başlıktadır). Sonuç: "Kaydedildi" + oluşturulan kayıtlara bağlantılar;
   "hâlâ yapılacaklar": doğrulama, talep kapatma, plan kontrol listesi (bağlantılar, yapılmış gibi gösterilmez).

Davranış/erişilebilirlik: adım göstergesi `ol` + `aria-current="step"`; adım değişince odak adım başlığına; hata özeti `role="alert"`; `prefers-reduced-motion`
altında geçiş animasyonu yok (animasyon zaten yok, yalnız adım görünürlüğü); 390 px ve %200 yakınlaştırmada yatay kaydırma yok; renk tek sinyal değil (ikon + metin);
açık/koyu tema. Yeni genel CSS yok; kapsamlı `.razor.css` ve mevcut `SoPageHeader/SoProblemPanel/SoStatusBadge/SoFieldGrid`.
Sunucu hatasında dürüst durum: "Kaydedilemedi, hiçbir kayıt oluşmadı" (işlem tek transaction olduğu için doğru) — yalnız **sunucu cevabı hiç gelmediyse**
(zaman aşımı) "sonuç bilinmiyor; hesabı yenileyip kontrol edin" denir ve yeniden gönderme aynı idempotency anahtarıyla güvenle yapılır (§8).

## 5. Sözleşme (OLAY-2 uygular; hepsi yalnız ekleme, `/api/v1/service-accounts`)

Yetenek: `ServiceAccounts.Work` (+ hesapta sorumlu dayanak; katılımcı yalnız kendi `ParticipantRequestIds` taleplerine, §5.5). Tüm POST'lar mevcut CSRF başlığını
(`X-SecureOps-Csrf: 1`, ADR-0029) ister. Hata gövdesi mevcut `ServiceAccountReplies` ProblemDetails eşlemesi; kodlar kararlı (UI Türkçe metne eşler).

### 5.1 Uçlar

| Uç | Yetenek | Ne yapar |
|---|---|---|
| `POST accounts/{id}/events/preview` | Work | Taslağı doğrular, **yazmaz**; önizleme + `previewDigest` + `closeReadiness` döner |
| `POST accounts/{id}/events` | Work | Önizlenmiş taslağı **tek transaction'da** kaydeder; yeni `AccountDetail` döner |
| `GET requests/{id}/close-readiness?outcome=Completed` | View + talebe erişim | "Neden kapatılamıyor" listesi (§6) |
| `POST accounts/{id}/events/mail-hints` | Work | Yapıştırılan metinden alan **önerisi** (§7); durumsuz, kaydetmez |

### 5.2 `EventDraft` (istek gövdesi, preview ve commit aynı gövde)

```
EventDraft {
  eventKind        : "OwnerReplied" | "PasswordChanged" | "AccountDeleted" | "GmsaConverted" | "OwnerChanged" | "ServiceMoved" | "Other"
  occurredOn       : date            // olayın GERÇEK tarihi (mail tarihi değil, işlemin tarihi); gelecek tarih 400 ActualDateInFuture
  datePrecision    : "DateOnly" | "Unknown"   // Unknown ise tarih uydurulmaz; eylem ActualOn=null, "tarihsiz bildirim" etiketi (kural 8)
  channel          : "Mail" | "Phone" | "Meeting" | "Other"
  requestId        : guid?           // mevcut bir açık talebe bağla; null = bağlama
  createRequest    : { actionType, targetTeamId?, notes? }?   // yalnız ServiceMoved/Other; requestId ile birlikte verilemez
  closeRequest     : { outcome: "Completed" }?                // YALNIZ requestId varsa; varsayılan null (kapatma yok)
  communication    : { direction, kind, contactTeamId?, contactPersonId?, subject?, summary?, providerMessageId?, meaningfulReply? }?
  action           : { performerTeamId?, performerPersonId?, evidenceNote? }?   // actionType/result/recordKind SUNUCUDA türetilir
  references       : [ { type: "OR"|"OCO"|"JIRA"|"OTHER", number, url? } ]       // mevcut SaExternalRef; biçim kuralı mevcut dış kayıt kuralı
  ownership        : { teamId?, personId?, effectiveFrom?, reason }?              // yalnız OwnerChanged; Mode her zaman "Propose"
  accountVersion   : string          // hesap sürümü (bayat taslak 409)
  idempotencyKey   : guid            // commit'te zorunlu; preview'da yok sayılır
}
```

Sunucu doğrulaması: `eventKind` ↔ hangi blokların **izinli/zorunlu** olduğu §3 tablosundan (örn. `PasswordChanged` ile `ownership` gelirse 400 `blockNotAllowed`,
`OwnerReplied` ile `action` gelirse aynı). `action.result`, `recordKind`, `actionType` gövdede **hiç yoktur**; fazla alan 400. `Verified`/`verifiedOn`/`verifierPersonId`
alanları tasarım gereği şemada yok.

### 5.3 `EventPreview` (preview yanıtı)

```
EventPreview {
  previewDigest   : string      // SHA-256: taslağın kanonik hali + etkilenen satırların sürümleri (hesap, talep, varsa eylem)
  items           : [ { kind: "Communication"|"Action"|"Reference"|"RequestUpdate"|"RequestCreate"|"OwnershipProposal"|"RequestClose",
                        operation: "Create"|"Update"|"Link", targetId?: guid, label: string, fields: [ {name, value} ],
                        allowed: bool, deniedReason?: "NoWorkCapability"|"OutOfScope"|"NotParticipantRequest" } ]
  notRecorded     : [ "Verification" | "AccountClosure" | "RequestClose" | "OwnershipDecision" | "PlanChecklist" ]   // "bunlar YAPILMAYACAK" satırları
  warnings        : [ { code, field? } ]      // örn. OrMissingForDeletion, DateAssumed, MailDuplicate, ReplyDateEarlier
  closeReadiness  : CloseReadiness?           // closeRequest istenmişse VEYA requestId varsa: bu kayıtlardan SONRA talep kapatılabilir mi (§6)
  followUps       : [ { kind: "Verify"|"CloseRequest"|"OpenPlan"|"UsageTab"|"OwnershipDecision", href?: string } ]   // yapıldı DEĞİL, yapılacak
}
```

`items[].allowed=false` varsa commit **tamamını** reddeder (403, `eventItemDenied`). Önizleme yazma yapmaz; ama commit önizlemenin sonucuna güvenmez: sunucu
commit'te **her şeyi baştan** doğrular (bayat/yeniden oynatma/yetki), digest yalnız "kullanıcının gördüğü ile yazılacak aynı mı" kontrolüdür.

### 5.4 Commit (`POST accounts/{id}/events`)

Gövde: `EventDraft` + `previewDigest`. Sunucu: (1) yetki + kapsam, (2) taslağı yeniden doğrula, (3) digest yeniden hesapla, farklıysa **409 `eventStale`**,
(4) tek `SqlTransaction` içinde sırayla: iletişim → eylem → referanslar → talep güncelle → (varsa) kapatma → (varsa) sahiplik önerisi → olay kaydı; her kayıt için mevcut
geçmiş + `audit.AuditLog` satırı **aynı işlemde**. Herhangi bir adım başarısızsa tamamı geri alınır. Başarıda 200 + `EventCommitResult { detail: AccountDetail, eventId, created: [ {kind, id} ], replayed: bool }`.

`closeRequest` varsa kural `ValidateClose` ile **kaydın yazılmış son haline** göre sunucuda çalışır; geçmezse **422 `closeNotAllowed`** + `missing[]` ve **hiçbir şey yazılmaz**
(kullanıcı kapatmayı işaretlemeden tekrar kaydedebilir). Varsayılan: kapatma işaretsiz. Gerekçe: `Completed` kuralı yalnız "eşleşen gerçekleşmiş bildirim var" ister (doğrulama istemez,
`Deletion` hariç), yani bildirim gelir gelmez talebi kapatmak teknik olarak mümkündür; ama "kişi bildirdi" ile iş bitmiş saymak sahibin vardiya pratiğinde sorun çıkarabilir → seçim kullanıcıda. Sahibe soru S2.

### 5.5 Yetki ve kapsam

- `Work` yoksa 403. Hesap kapsam dışıysa 404 (mevcut IDOR davranışı: hesabın varlığı sızmaz).
- **Sorumlu dayanak**: hesap geneli bloklar (sahiplik önerisi, yeni talep, hesap geneli yazışma) serbest. **Katılımcı dayanak**: yalnız `requestId ∈ ParticipantRequestIds`
  olan olaylar; `createRequest`, `ownership`, hesap-geneli bloklar `NotParticipantRequest`/403. Katılımcı hiçbir zaman sahiplik önerisi göndermez.
- `Verify` yeteneği bu uçlarda **hiçbir şey kazandırmaz** (doğrulama bu akışta yok).
- Yazışma kişisi/takımı: yalnız rehberdeki (people/teams) kimlikler; serbest kişi adı kabul edilmez (§7 kişi verisi).

### 5.6 Veri modeli — migration 036 (yalnız ekleme)

Numara: **036** (035 OR-2'ye ayrılı, `00-ORTAK-MERGE-SIRASI.md`'ye "ayrıldı" yazıldı). `svcacct` şemasında, ekleme-yalnız (UPDATE/DELETE tetikleyiciyle engelli):

- `svcacct.AccountEvents (Id, AccountId, EventKind, OccurredOn, DatePrecision, Channel, IdempotencyKey UNIQUE per (AccountId, Key), DraftDigest, CreatedBy, CreatedAt)` — olay başlığı.
- `svcacct.AccountEventItems (EventId, ItemKind, EntityType, EntityId, Operation)` — olayın hangi kayıtları oluşturduğu/güncellediği. Zaman çizelgesinde "bu kayıtlar
  şu olaydan geldi" gruplaması ve **yeniden oynatma** yanıtı için.
- Mail gövdesi, gönderen, ad, adres, ek: **hiçbir sütun yok.** Yeni sürüm alanı yok. Mevcut tablolar değişmez.
- 036 uygulanmamış veritabanında (kurulu TEST 028 tabanı): özellik **kapalı ve dürüst** — `GET me` yanıtına `eventGuidanceAvailable: false`; UI düğmeyi "bu sürümde veritabanı
  güncellemesi bekleniyor" ile gösterir; `close-readiness` ve `mail-hints` migration gerektirmez, 036'dan önce de çalışır (migration bağımsız, bkz. §10).
- Kurulu TEST'e hiçbir şey uygulanmaz; DBA kılavuzu (`DBA-036-TR.md`) OLAY-2 ile yazılır.

## 6. "Neden kapatılamıyor" — tek kaynak (C)

Sorun: `ValidateClose` ilk ihlali döndürüyor. Çözüm: Domain'de `ServiceAccountRules.EvaluateClose(...)` **tüm** eksikleri listeler; `ValidateClose` bunun ilk elemanını döndürmeye
devam eder (mevcut çağıranlar ve testler bozulmaz). Aynı yapı doğrulama için (`EvaluateVerification`).

```
CloseReadiness { canClose: bool, status: "Open"|"Closed", outcome, missing: [ CloseMissing ] }
CloseMissing   { code: string,            // mevcut Errors.* kodları: NoMatchingPerformedAction, VerifiedClosureRequired, OwnershipNotConfirmed, CloseReasonRequired, RequestAlreadyClosed
                 subCodes?: [string],     // VerifiedClosureRequired için ayrıştırma: ActualDateRequired, VerifierRequired, EvidenceRequired, OrRequiredForDeletion, NotClosureKind, NotVerified
                 expectedActionType?: string, // NoMatchingPerformedAction: hangi türde gerçekleşmiş işlem aranıyor
                 relatedActionId?: guid,  // varsa "şu eylem eksik" (örn. doğrulanması gereken eylem)
                 remedy: "ReportAction"|"VerifyAction"|"ConfirmOwnership"|"GiveReason"|"None" }
```

- `GET requests/{id}/close-readiness?outcome=` (outcome: Completed/NotNeeded/Cancelled). Aynı nesne `RequestView.closeReadiness` alanıyla (**isteğe bağlı, ekleme**)
  hesap detayında da döner; hesaplama zaten yüklenen bağlı eylemlerle yapıldığı için ek sorgu gerekmez. Varsa UI "Kapat" düğmesi yanında "neden" listesini gösterir; yoksa (eski API)
  düğme eskisi gibi çalışır.
- `subCodes` ayrıştırması: bugün `VerifiedClosureRequired` "doğrulanmış kapanış yok" der ama nedenini söylemez (tarih mi, doğrulayan mı, kanıt mı, OR mu?). `EvaluateClose` en iyi
  eşleşen adayı (en çok koşulu sağlayan) seçip eksik alt koşulları listeler.
- UI kuralı tekrar etmez: kodu Türkçe cümleye eşler ve `remedy`'ye göre **doğru adımı açar** (`ReportAction` → olay akışı ilgili olay seçili; `VerifyAction` → `Verify` yetkisi varsa
  doğrulama diyaloğu, yoksa "doğrulama yetkisi olan birine" metni). Düğme `disabled` yapılmaz, `aria-disabled` + `aria-describedby` ile açıklama okunur (klavye odağı kaybolmaz).
- Sunucu yine de her komutu yeniden doğrular; `canClose=true` bir söz değil, bilgidir (bayatlamış olabilir).

**OLAY-2a uygulama notu (2026-10-13, dal `feature/service-accounts-close-readiness-20261013`).** Uygulanan: `ServiceAccountRules.EvaluateClose`
(`CloseGap` listesi; `ValidateClose` artık bunun ilk kodunu döndürür, eski uygulamayla eşdeğerliği birim testinde tüm dallar + tek eylem + 4000 rastgele
eylem çifti üzerinde kanıtlı) ve `GET requests/{id}/close-readiness?outcome=` (View; hesap detayıyla aynı kapsam, kapsam dışı/bilinmeyen = 404;
`outcome` yalnız tam üye adı, aksi 400 `field=outcome`; yazma ve audit yok). Kararlar ve sapmalar:
- `subCodes` listesine `VerificationBeforeAction` da girer (`IsVerifiedClosure` bunu da şart koşar). Aday yoksa `subCodes` ve `relatedActionId` boştur;
  "en yakın aday" = en az eksik koşulu olan, eşitlikte en yeni (sorgu `CreatedAt DESC`). Sonuç `Verified` olup `VerifiedOn` boşsa `NotVerified` sayılır.
- Silme talebinde hiç eylem yoksa liste iki maddedir: `NoMatchingPerformedAction` + `VerifiedClosureRequired` (alt kod yok).
- Kapatma gerekçesi kapatma anında yazıldığından `NotNeeded`/`Cancelled` için liste her zaman `CloseReasonRequired` (`GiveReason`) içerir.
- **`RequestView.closeReadiness` bu PR'da YOK.** Belgedeki "ek sorgu gerekmez" varsayımı koddan doğrulanamadı: `ActionView` doğrulayanı yalnız kişi olarak
  taşır (`VerifiedByUserId` yok), doğrulama kanıt dosyası kimliğini taşımaz; detaydan hesaplamak kapatma komutundan farklı sonuç verebilir (iki kaynak).
  Ya detay sorgusuna talep başına kapanış gerçekleri eklenir ya da UI talep başına bu ucu çağırır; karar OLAY-3 öncesi.
- `EvaluateVerification` (doğrulama için aynı yapı) bu PR'da yok; OLAY-3 "Doğrula neden kapalı" ipucu için gerekirse ayrı küçük iş.

## 7. Mail yapıştır — deterministik öneri (B)

`POST accounts/{id}/events/mail-hints` gövde: `{ text: string }` (en çok 20 000 karakter; fazlası 400). Yanıt **yalnız öneri**:

```
MailHints {
  eventSuggestions : [ { eventKind, reason: "Matched:<kuralId>" } ]            // sıralı; hiçbiri otomatik seçilmez
  references       : [ { type, number, rule } ]                                 // OR/OCO adayları
  dates            : [ { value, kind: "Explicit"|"AssumedYear", rule } ]
  warnings         : [ "PasswordLikeContent" | "TooLong" | "NothingFound" ]
}
```

Kurallar (Domain'de saf fonksiyon `EventMailHints`, birim testli, sürümlü `kuralSeti: "olay-1"`; **dil modeli yok, ağ çağrısı yok**):
- **Referans**: `OCO`/`OR`/`CHG`/`JIRA` önekli adaylar + mevcut dış kayıt biçim kuralı (yalnız `A–Z 0–9 - _ . /`, ≥1 rakam, ≤64). Öneksiz sayı **önerilmez**. Gerçek OR/OCO desenleri
  doğrulanmadı (bkz. S5); kural tablosu veriyle genişler, kod değil.
- **Tarih**: `gg.AA.yyyy`, `gg/AA/yyyy`, `yyyy-AA-gg`, "1 Ekim 2026". Yılsız ("1 Ekim") → `AssumedYear` (bugünün yılı; gelecekteyse önerilmez) ve önizlemede `DateAssumed`
  uyarısı; kullanıcı onaylamadan tarih sayılmaz. Birden çok tarih → hepsi listelenir, hiçbiri seçili gelmez.
- **Olay ifadeleri** (Türkçe, aksan/büyük-küçük harf duyarsız, kök bazlı): parola/şifre + değiş/yenile/güncelle → `PasswordChanged`; sil/kaldır/kapat + hesap → `AccountDeleted`; gMSA +
  geç/çevir/dönüş → `GmsaConverted`; sahip/sorumlu + değiş/devr → `OwnerChanged`; taşı → `ServiceMoved`. Çelişen eşleşmede hepsi önerilir, kullanıcı seçer.
- **Çıkarılmayanlar**: kişi adı, e-posta adresi, telefon, imza, alıntılanan eski mail zinciri. Ayrıştırıcı bunları aramaz, yanıta koymaz. Kişi, rehberden **kullanıcı tarafından** seçilir.
- **Parola benzeri içerik**: `parola:`/`şifre:`/`password=` gibi anahtar + değer deseni veya uzun rastgele dizge → `PasswordLikeContent` uyarısı ("Metinde parola olabilecek bir ifade var.
  Kaydedilmedi; metni silip yalnız ilgili cümleyi yapıştırın."). Bu yakalanan parçalar **yanıta, günlüğe, hata iletisine girmez**.
- Sınır: kural bulamazsa `NothingFound` + boş öneri; "bulunamadı" kullanıcıya "mailde yok" diye sunulmaz, "ürün otomatik bulamadı" denir.

**Saklama kararı (öneri):** yapıştırılan metin **hiçbir yerde saklanmaz**: veritabanı, günlük, audit, önbellek, tarayıcı depolaması yok (CLAUDE.md: tarayıcı depolaması yalnız görünüm tercihi). Uç, istek gövdesini
günlüğe yazmaz (mevcut hata günlük ara katmanı gövde kaydetmiyor olmalı; OLAY-2'de test ve doğrulama maddesi). Kaydedilen yalnız kullanıcının onayladığı: tarih, referans numaraları, tür,
**kullanıcının kendi yazdığı kısa özet** (`summary`, mevcut alan; arayüz "ad ve mail içeriğini kopyalamayın" ipucu verir), isteğe bağlı mevcut `providerMessageId`. Saklama süresi sorusu doğmaz
(gövde yok); özet ve olay kaydı mevcut modül saklama/audit süresine (audit ≥ 36 ay) tabidir. Sahibe soru S4 (özeti de kısıtlamak ister mi).

**OLAY-2b uygulama notu (2026-10-13, dal `feature/service-accounts-mail-hints-20261013`).** Uygulanan: `events/mail-hints` (`ServiceAccountMailHintsController`,
`ServiceAccountService.MailHintsAsync`), saf `EventMailHints` ve tek dosyalık kural tablosu `EventMailHintRules` (Domain, kural seti `olay-1`).
Kararlar ve tasarımdan sapmalar:
- Yetki `Work` + hesap kapsamda (kapsam dışı/bilinmeyen = 404, `View` tek başına 403). Metin boş/yalnız boşluk ya da 20 000 karakterden uzunsa
  400 `field=text` (`TooLong` uyarısı yok; uzun girdi eşleştirmeden önce reddedilir). İstek gövdesi üst sınırı 128 KB (413).
- Tüm desenler `RegexOptions.NonBacktracking` (doğrusal zaman) + 250 ms zaman aşımı; NonBacktracking ileri/geri bakış desteklemediği için rakam sınırları ve
  "konu ile eylem en çok 70 karakter, aynı cümlede" yakınlığı kodda denetlenir. Girdi önce katlanır (küçük harf, Türkçe harf/aksan kaldırma; eşleşmeyen vekil
  karakterler atılır, aksi halde `Normalize` istisna atar).
- Olay ifadeleri: parola/şifre·password + değişti·yenilendi·güncellendi·reset·rotated; hesap·account + silindi·kaldırıldı·kapatıldı·deleted·decommissioned;
  gMSA + geçti·çevrildi·dönüştü·migrated; sahip·sorumlu·owner + değişti·devredildi·transferred; taşındı·moved. Olumsuz/gelecek ifadeler ("değişmedi", "yapılacak",
  "henüz", "will", "not") öneri üretmez. `OwnerReplied` ve `Other` için kural yok (ifade yok; kullanıcı elle seçer).
- Referans: yalnız `OCO` (`-`, `_`, boşluk ya da bitişik) ve `OR` (`-`/`_` şart; İngilizce "or" eşleşmesin diye) önekli adaylar; kabul ölçüsü modülün mevcut
  kayıt numarası kuralı `ChangePlanRules.OcoNumber` (≤64, `A–Z 0–9 - _ . /`, ≥1 rakam). `CHG`/`JIRA` önekleri eklenmedi (gerçek desen bilinmiyor, S5).
- Tarih: `gg.AA.yyyy`, `gg/AA/yyyy`, `yyyy-AA-gg`, "1 Ekim 2026", "1 October 2026", "October 1, 2026"; yılsız Türkçe/İngilizce ay adı → `AssumedYear` (bu yıl).
  Gelecekteki, geçersiz (31.02) ve 2000–2100 dışı tarihler önerilmez; aynı tarih bir kez listelenir; en çok 10 referans ve 10 tarih.
- Parola benzeri: `parola|şifre|password|pwd|pass` + `:`/`=` + rakam ya da simge içeren değer, ya da ≥16 karakterlik üç sınıflı rastgele dizge → yalnız
  `PasswordLikeContent` uyarısı; yakalanan parça hiçbir yerde tutulmaz.
- **Günlük kanıtı:** `MailHintsRequest` kayıt türünün `ToString` çıktısı metni göstermez. Sebep: MVC, `Trace` düzeyinde eylem argümanlarını yazar
  ("Executing action method ... with arguments"); barındırılan testte (her düzey, her kategori) bu satırda metin yakalandı ve kapatıldı. Test: istek gövdesi
  geçerli/bozuk/yanlış tip/aşırı büyük, iki kimlik, CSRF reddi; günlük, audit ve yanıtlarda işaretçi yok.

## 8. Güvenlik tehdit tablosu (OLAY-2 için bağlayıcı; her satır testle kanıtlanır)

| # | Tehdit | Karşılık |
|---|---|---|
| E1 | **Bildirimi doğrulama gibi göstermek / sızdırmak**: istemci `result=Verified`, `verifiedOn`, `verifier` göndermeye çalışır | Şemada yok; fazla alan 400; sunucu `result=Performed` sabitler; test: hiçbir olay türü `Verified`/`VerifiedOn` yazmaz |
| E2 | **Tür/kayıt türü kaçırma**: `PasswordChanged` olayında `Closure` kaydı yaptırıp hesabı "kapanış adayı" göstermek | `actionType/recordKind` yalnız §3 tablosundan sunucuda türetilir; test tablo üzerinden |
| E3 | **IDOR**: başka hesabın talebine `requestId` vermek, kapsam dışı hesap | Talep `accountId` ile eşleşmeli (400 `requestAccountMismatch`); kapsam dışı hesap 404; çapraz hesap testleri |
| E4 | **Katılımcı yetki yükseltme**: katılımcı sahiplik önerisi / yeni talep / başka talep | §5.5; test: katılımcı + her blok |
| E5 | **Yeniden oynatma / çift tıklama**: aynı commit ikinci eylem üretir | `(AccountId, IdempotencyKey)` benzersiz; ikinci istek `replayed=true` ile ilk sonucu döner, yazmaz; farklı gövdeyle aynı anahtar 409 `idempotencyKeyReused` |
| E6 | **Bayat önizleme**: önizleme sonrası talep/hesap değişti | Digest hesap+talep(+eylem) sürümlerini içerir; commit'te farklıysa 409 `eventStale`, hiçbir şey yazılmaz |
| E7 | **Kısmi yazma**: yazışma yazıldı eylem yazılmadı | Tek `SqlTransaction`; hata enjeksiyonlu test (araya hata → hiçbir satır, hiçbir audit satırı kalmaz) |
| E8 | **Mail gövdesinde parola / kişisel veri** günlüğe, hataya, audit'e sızar | §7; ayrıştırıcı yakalanan parçayı yanıtlamaz; gövdeyi günlüğe yazmama testi; yanıt ve hata gövdesinde metin yok testi |
| E9 | **Aşırı büyük gövde / regex DoS** | 20 000 karakter sınırı; regex'ler `RegexOptions.NonBacktracking` ya da zaman aşımı; kötü niyetli desenli birim testi |
| E10 | **Kapatma kuralını atlatmak**: `closeRequest` ile eşleşmeyen eylemle kapatma | `ValidateClose`/`EvaluateClose` aynı transaction'da son yazılmış duruma karşı; 422 + hiçbir şey yazılmaz |
| E11 | **Referans türü karıştırma** (OR yerine OCO) | Mevcut dış kayıt kuralı (SPEC kural 7) aynen; olay türü başka tür varsaymaz; öneri `type`'ı mailin önekinden alır, yoksa önermez |
| E12 | **Dürüst olmayan UI**: önizlemede "kaydedildi/doğrulandı" izlenimi | UI sözleşme testi: etiket `Bildirildi — doğrulanmadı`; `notRecorded` her zaman gösterilir |
| E13 | **CSRF** | Mevcut (ADR-0029); testle bu uçlar kapsanır |
| E14 | Gelecek tarih / geriye dönük tarih uydurma | `ActualDateInFuture`; `Unknown` tarih için tarih uydurulmaz (SPEC kural 8, "tarihsiz rapor haftaya yayılmaz") |

## 9. Rol bazlı kısa "Nasıl kullanılır?" ve "sıradaki adım" bağlantısı (D)

**Taslak metinler** (OLAY-3'te `SaPageGuide`'a, F4'ün kısa düzeltmelerinden sonra; sahibin ağzıyla, kısa):
- *Sorumlu (Work)*: "Hesapta bir şey olduysa **Bir gelişme mi oldu?**'ya basın, ne olduğunu seçin. Ürün hangi kayıtların yapılacağını size gösterir; siz onaylayınca hepsi birlikte kaydedilir.
  Kaydedilen şey 'kişi bildirdi'dir; doğrulama ayrıdır."
- *Katılımcı*: "Size atanmış talep için gelişmeyi bildirebilirsiniz. Hesabın sahipliğini ve diğer talepleri göremez/değiştiremezsiniz."
- *Doğrulayan (Verify)*: "Bildirilen işlemler Doğrulama bekler. Doğrulamak için tarih, doğrulayan ve kanıt gerekir. 'Bir gelişme mi oldu?' bildirim kaydeder, doğrulamaz."
- *Salt okunur*: "Bu hesapta kayıt yapamazsınız; gelişmeleri sorumlu ekibe iletin."

**"Sıradaki adım" kartı (#14) bağı:** `ServiceAccountNextStep` kuralları değişmez; her adım kartına, adım bir olaya karşılık geliyorsa **"Bildir"** ikinci eylemi eklenir ve akışı `eventKind` + `requestId`
önseçili açar (örn. "Parola değişimi bekleniyor" → `PasswordChanged`, ilgili talep seçili; "Sahiplik kararı bekleniyor" → olay akışı değil, mevcut karar akışı). Kart yine yalnız yüklü detaydan hesaplanır.

**Bağlam içi ipuçları (neden kapalı):** "Talebi kapat" (§6), "Doğrula" (`NotPerformed`, `VerifierRequired`, `EvidenceRequired` alt kodları), "Hesabı kapat"/kapanış kaydı (`ClosureKindNotAllowed`: yalnız silme ve gMSA geçişi).
Düğme `aria-disabled` + görünür açıklama; metinler `ServiceAccountProblems` içindeki mevcut kod→metin eşlemesine eklenir.

## 10. Önerilen parçalama

| PR | İçerik | Migration | Bağımlılık |
|---|---|---|---|
| **OLAY-2a** (sözleşme + küçük backend) | `EvaluateClose`/`EvaluateVerification`, `close-readiness` ucu ve `RequestView.closeReadiness`, birim testleri, OpenAPI ekleme. **036'sız.** Tek başına değerli: F4/OLAY-3 "neden kapalı" için | yok | yok (master) |
| **OLAY-2b** | `EventMailHints` (saf Domain) + `mail-hints` ucu, kural tablosu, regex-DoS ve parola-sızıntı testleri | yok | OLAY-2a'dan bağımsız (paralel yazılabilir, ayrı dosyalar) |
| **OLAY-2c** | `events/preview` + `events` (commit, tek transaction), 036 şeması + SQL testleri (T-tablosu E1–E14), `DBA-036-TR.md`, `eventGuidanceAvailable` | **036** | 2a (kapatma değerlendirmesi), 2b yalnız UI için |
| **OLAY-3** (UI) | Adımlı akış, önizleme, rol rehberi, kartlara bağ, neden-kapalı ipuçları; tests/browser yolculuğu | yok | **F4 merge** + OLAY-2a (en az); tam akış için 2c |

Önerilen sıra: 2a → (2b ‖ 2c) → 3. UI iki aşamada yayınlanabilir: 2a ile yalnız "neden kapalı" ipuçları, 2c ile tam akış. UI ve API birlikte yayınlanır (CSRF notu gibi); 036 uygulanmamışsa akış gizlenir (§5.6).
OpenAPI anlık görüntüsü: yeniden üretme yerine yeni girdileri ekleme (OpenAPI notu).

Dokunulacak mevcut yerler (keşif): `ServiceAccountRules.cs` (EvaluateClose), `ServiceAccountContracts.cs` (yeni kayıtlar), `ServiceAccountsController.cs` (+ yeni `ServiceAccountEventsController`),
`SqlServiceAccountRepository.Work.cs` (mevcut komutların transaction içinden çağrılabilir iç yöntemleri; bugün her biri kendi bağlantı/transaction'ını açıyorsa ortak transaction'a alınması OLAY-2c'nin ilk işidir),
`ServiceAccountDetail.razor`, `SaNextStepCard.razor`, `SaPageGuide.razor`, `ServiceAccountProblems.cs`, `tests/...`.

## 11. ADR gerekli mi?

**Hayır.** Gerekçe: yeni mimari/güvenlik kararı yok. Mevcut modül sınırları içinde, mevcut yetenekler ve mevcut kayıt türleriyle çalışır; sunucuya yazma yok; dış sisteme çağrı yok; yeni
kimlik/yetki yok; yapay zekâ yok. Mail ayrıştırma yerel, kural tabanlı ve durumsuz. Saklama kararı (§7) "gövde saklanmaz" yönünde en sıkı seçenek. ADR gerektirecek değişimler: ayrıştırmayı dil modeline
vermek (kural 2 yasak, ADR bile yetmez), ürünün PAM/AD'ye yazması (kural 1/3, ADR-0028), doğrulamayı olay akışına katmak (SPEC kural 3–4'ü değiştirir), OR/OCO'yu ITSM'den doğrulamak (TH işi,
ADR-0031/0032; OLAY-2'de bu arayüz yok, referans yalnız biçim doğrulanır).

## 12. Testler (OLAY-2/3 için asgari)

- Domain: `EvaluateClose` her kural dalı + `ValidateClose` ile tutarlılık (aynı girdilerde ilk eksik eşit); `EventMailHints` kural tablosu (Türkçe aksan, yılsız tarih, çoklu tarih, parola benzeri, DoS).
- Olay → kayıt tablosu (§3) satır satır; hiçbir olayın `Verified` üretmediği; her yetki/dayanak kombinasyonu.
- SQL (gerçek LocalDB, mevcut harness): E5–E7 (replay, bayat, hata enjeksiyonlu geri alma), audit tutarlılığı, 036 yeniden çalıştırmayı reddeder, ekleme-yalnız tetikleyiciler.
- API: CSRF, 400/403/404/409/422 şekilleri, mail gövdesinin yanıt/günlükte bulunmaması.
- UI: bUnit/Razor testleri (adımlar, `aria-current`, odak, `notRecorded` her zaman görünür, etiket "Bildirildi — doğrulanmadı"), `tests/browser/*.cjs` yolculuğu (sentetik veri, loopback): "parola değişti" tam akışı, 390 px, koyu tema, klavye.

## 13. Bu tasarımda doğrulanmayanlar

- Hiçbir kod/derleme/test çalıştırılmadı; yalnız koddan okunan kurallar (`ServiceAccountRules`, sözlük, sözleşme tipleri, uç listesi) esas alındı.
- Mevcut yazma yöntemlerinin ayrı transaction açıp açmadığı, `summary` sütun uzunluğu, hata günlüğünün gövde kaydetmediği OLAY-2'de koddan doğrulanacak maddelerdir.
- Gerçek OR/OCO numara desenleri bilinmiyor (yalnız biçim kuralı var); mail örüntüleri sentetik varsayımlardır, gerçek maillerle ayarlanmadı.

## 14. Sahibe sorular

Cevaplar `README.md`/bu belgeye işlenir; varsayılan (koyu) önerim yazılıdır — itiraz yoksa o uygulanır.
- **S1.** `PasswordChanged` için eylem kaydı (`PasswordChange` / Performed / Intermediate) olsun mu, yoksa yalnız yazışma + referans mı? **Önerim: eylem kaydı olsun** (rapor "bildirilen parola değişimi" sayabilsin), etiketi "Bildirildi — PAM'da doğrulanmadı". Ekip lideri sorusu (parola PAM'ın mı) bu seçimi etkiler.
- **S2.** Olay akışında "Talebi de kapat" seçeneği olsun mu? **Önerim: olsun, varsayılan kapalı** (kural 9: açık seçim). Alternatif: hiç olmasın, kapatma hep mevcut akışta.
- **S3.** `AccountDeleted` ve `GmsaConverted` bildirimleri `Closure` adayı olarak kaydedilsin mi (kapanış sayılmaz, doğrulama ister)? **Önerim: evet**; aksi halde doğrulayan `Closure` türünü sonradan elle düzeltmek zorunda.
- **S4.** Kullanıcının yazdığı kısa özet serbest metin kalsın mı (uyarı ipucuyla) yoksa daha da kısıtlansın (uzunluk, kalıp)? **Önerim: serbest, ipucu + uzunluk sınırı**.
- **S5.** Gerçek OR ve OCO numara desenleri nedir (örnek yer tutucu: `OR-0000000`, `OCO-00000` gibi biçimi söylemeniz yeter, gerçek numara vermeyin)? Ayrıştırıcının öneki ve rakam uzunluğu buna göre daralır.
- **S6.** `Performed` etiketi bugün "Gerçekleşti". Olay akışında "Bildirildi" diyoruz; rapor/ekranlardaki genel etiketi de **"Gerçekleşti (bildirildi)"** yapalım mı (rapor metni değişir)? **Önerim: yalnız olay akışı ve hesap sayfasında; raporlara dokunma**, ayrı karar.
- **S7.** `ServiceMoved` ürün kaydı: v1'de yazışma + isteğe bağlı İnceleme talebi + kullanım sekmesi bağlantısı yeterli mi, yoksa taşınan servisi kullanım kaydına işleyen ayrı bir adım mı istiyorsunuz (veriyi kullanıcı beyanıyla değiştirir, dikkatli olunmalı)?
- **S8.** Birden çok hesabı etkileyen tek mail (toplu "hepsinin parolası değişti"): v1 tek hesap. Çok hesaplı olay (mevcut iletişim kaydı zaten çok hesap bağlayabiliyor) sonraki aşamada mı olsun?

## 15. Sonraki adım

Sahip S1–S8'e cevap verir (en azından S1, S2, S3, S5); 2a hemen başlayabilir (cevaplardan bağımsız). OLAY-3 F4'ün merge'ünü bekler.
