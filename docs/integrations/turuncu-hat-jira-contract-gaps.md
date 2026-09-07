# Turuncu Hat and Jira Sanitized Contract Gaps

Controlled evidence received through 2026-09-04 resolves Jira Basic authentication, `/myself`, one exact username search success, field/create metadata, one existing issue projection, Turuncu Hat login success shape, the exact active-record query request, successful nested-array `QueryResult.Items`, independently optional empty/null error fields, and one matching BPM activity. Real write TEST remains blocked on the samples below. Every example must preserve HTTP status, method, path, relevant non-secret header names, property names, nesting, value types, and empty/null behavior. Replace URLs, credentials, sessions, people, corporate content, and issue keys with placeholders.

## Resolved Evidence

- Original script discovery and historical request construction are resolved by the
  2026-09-07 source-only review and hash in `turuncu-hat-jira-legacy-parity.md`.
  The supplied copy has a line-274 string defect; no repaired file was substituted.
  ID `3`, constant label and optional custom-field array are established, but
  neither a current issue-type name, native watchers nor a separate approver flow
  can be inferred from that script.
- Earlier controlled Jira Basic `/rest/api/2/myself`, exact `username` search,
  metadata identifying ID `3` as Task at that time, required/optional create
  fields, multi-user custom field, cascading group, optional assignee, and absent
  reporter metadata. These are separate from original-script evidence.
- Existing Jira issue presence for assignee, reporter, `customfield_11500`, `customfield_12700`, and `SunucuTalep`; observed assignee/reporter identity equality is not treated as create policy.
- String `LoginResult` with two observed pipe segments; exact `SMSS_oRFF` active/group/DCC request; successful nested-array `QueryResult.Items`; independently optional empty/null query error fields; exact seven-cell source keys (`SET.id`, `SET.p_code`, `SET.p_name`, `SET.p_description`, `KEY.p_rel_requester`, `SET.p_rel_requester`, and `num`); and exactly one controlled BPM query match. Observed segment lengths are not invariants.

## SDM Aktivasyon Kanıtı: Operatör Rehberi, 2026-09-07

Bu bölüm yalnızca kalan kanıtları toplar; çağrı yürütme veya yazma izni değildir.
rc6.11 kaynak SHA'sı `74cd8274250302a977cbc4c5cd6e4f1789c01459` değişmez.
Yetkili yayıncının açık incelemesi **iş önerisidir**, onaylanmış uygunluk politikası
değildir. Ayrı onaycı gereksinimi varsayılmaz; mevcut engeller kaldırılmaz.

### Önceden Kanıtlananlar

| Kanıt | Sınırı / tekrar istenmeyen bilgi |
|---|---|
| Özgün betik, hash ve tarihsel istek eşlemeleri | Keşif tamamlandı; betik tekrar istenmez veya çalıştırılmaz |
| SDM, gönderilen issue-type ID `3`, WASAS ve SunucuTalep | Betik güncel tip adını veya pozitif sınıflandırma kuralını kanıtlamaz |
| Önceki metadata: `customfield_12700` = İlgili Grup, zorunlu cascading select; `customfield_11500` = Takip Eden Kişiler, opsiyonel multi-user picker | Güncel SDM/3 bağlamı ve servis kimliğiyle yeniden teyit gerekir; native watcher eşitliği yok |
| Önceki Basic `/myself`, bir başarılı kullanıcı araması, `name/key/displayName` | Bugünkü izinler, sıfır/çoklu sonuç ve kimliğin kalıcılığı kanıtlanmış değil |
| Gerçek TEST'te dört OR, doğru SET/KEY ayrımı; bir BPM eşleşmesi | Sıfır/çoklu BPM, yazma başarısı ve koşullu güncelleme destekleniyor sonucu çıkmaz |
| Yerel SQL duplicate/claim/restart kanıtı ve etkileşimli Simulation yolculukları | Jira'nın uzaktan idempotency veya mutabakat sözleşmesinin yerine geçmez |

### Toplama Sınırı ve Dönüş Biçimi

Operatör mevcut onaylı istemci/oturum ve servis kimliğini kullanır; kimlik bilgisi
isteme, paylaşma veya yeni auth yöntemi yoktur. Aşağıdaki göreli yollar yalnızca
yönetici tarafından doğrulanan ürün/sürümde ve onaylı TEST kapsamındadır. TLS/proxy,
izin, otomasyon veya yapılandırma değiştirilmez. `401/403/429`, yönlendirme, beklenmedik
yanıt ya da sınır aşımında durulur; hesap kilitleme/rate-limit denemesi yapılmaz.
Boş arama, wildcard, tüm kullanıcı/proje/issue listesi, `fields=*all`, HAR/transcript
ve ham JSON dökümü yoktur. API alan seçimi sunmuyorsa yalnızca izin verilen özet
istemci belleğinde çıkarılır; yanıtın tamamı dosyaya/konsola yazılmaz.

Her adım için dönün: `step`, UTC zaman, ürün/sürüm belge referansı, `actorAlias`
(`integration-service` veya `jira-admin`), `scopeAlias`, HTTP metot/yol şablonu,
HTTP durum, `complete|incomplete|denied|not-run`, aşağıdaki izinli alanlar ve güvenli
kanıt referansı. Kişiler/OR/issue/hesap ID'leri tutarlı takma değerler olsun; eşitlik,
harf duyarlılığı, tip, eksik/null/boş ayrımı korunsun. Gerçek URL, auth/cookie/session,
e-posta, avatar, açıklama, serbest hata metni gönderilmez. Hatalarda yalnızca alan
adları, değer tipleri, boş/dolu bayrağı ve incelenmiş güvenli kod döner. Zorunlu
alan isimleri ve onaylı WASAS seçeneği dışındaki kurumsal iş değerleri maskelenir.

### Asgari Numaralı Toplama Adımları

1. **Ürün/sürüm ve bağlam.** Jira yöneticisi mevcut About/System Information
   ekranından ürün (Server/Data Center/Cloud), sürüm ve build numarasını doğrular;
   lisans/cluster dökümü alınmaz. Kurulum Server/Data Center olarak doğrulandıktan
   sonra, o sürüm destekliyorsa `GET /rest/api/2/serverInfo` ile yalnızca `version`,
   `versionNumbers`, `buildNumber`, varsa `deploymentType` döner; health check
   istenmez. Alan yoksa `missing` yazın, ürün adını tahmin etmeyin. Repo kesin
   ürün/sürümü kaydetmiyor; `/api/2` ve `name` tek başına yeterli değildir.
   Aşağıdaki REST örnekleri Server/Data Center için koşulludur. Cloud veya farklı
   sözleşme çıkarsa durun; `username/name` yerine sessizce `accountId` koymayın.
   Referans: [Atlassian sürümlü Server REST referansı](https://docs.atlassian.com/software/jira/docs/api/REST/9.0.0/).
   Bu 9.0 referansı kurulumun 9.0 olduğuna kanıt değildir; yönetici gerçek sürümün
   resmi referansını dönüşe eklemeden sonraki HTTP adımları uygulanmaz.

2. **SDM/3 metadata ve etkili izinler.** Aynı servis kimliğiyle, sürüm destekliyorsa
   `GET /rest/api/2/issue/createmeta/SDM/issuetypes?startAt=0&maxResults=50`, ardından
   `GET /rest/api/2/issue/createmeta/SDM/issuetypes/3?startAt=0&maxResults=50`.
   Yalnız SDM kapsamı; en fazla iki sayfa. `last/start/size/total` ve dönen alan
   sayısını kaydedin; tamamlanmadıysa eksik diye işaretleyin, görünmeyeni yok saymayın.
   Dönüş: tip `id/name/subtask`; hedef alanlar `summary,description,labels,assignee,
   reporter,customfield_12700,customfield_11500` için `fieldId/name/required/schema`
   (`type/items/custom/customId`), `operations`, `hasDefaultValue`; ayrıca başka
   zorunlu alanların sadece ID/tip/default-varlığı. Kullanıcı defaultları gönderilmez.
   Proje/tip kapsamlı uçlar 8.4'te eklendi; eski toplu createmeta 9.0'da kaldırıldı.
   Eski/desteklenmeyen sürümde endpoint denemek yerine yöneticinin aynı bağlamdaki
   salt okunur metadata incelemesini isteyin; dark-feature değiştirmeyin.
   [Atlassian createmeta geçişi](https://confluence.atlassian.com/jiracore/createmeta-rest-endpoint-to-be-removed-975040986.html).
   Sürüm destekliyorsa `GET /rest/api/2/mypermissions?projectKey=SDM`; yalnız Browse
   Projects, Create Issues, kullanıcı arama izni; politika gerektirirse Assign
   Issues/Modify Reporter ve watcher görünürlüğü/yönetimi için dönen izin anahtarı,
   `havePermission` ve kapsam. Global kullanıcı arama yetkisini yönetici ayrıca
   teyit eder. Bilinen tek onaylı issue için issue-bağlamlı izin de teyit edilir;
   admin sonucu servis hesabı izni sayılmaz. Bu inceleme izin verme işlemi değildir.
   [Atlassian bağlamlı izin sorgusu](https://developer.atlassian.com/server/jira/platform/rest/v10007/api-group-mypermissions/).

3. **İki özel alan ve requester.** Adım 2'den güncel alan ad/tipini, SDM/3 create
   ekranı/bağlamını ve `12700` için WASAS üst seçeneğinin ID/değer/aktifliği ile alt
   seçim gerekip gerekmediğini döndürün; yalnız ilgili allowedValues dalı. `11500`
   için beklenen dizi/öğe şekli, `name` kabulü ve kullanıcı kapsamı yöneticice
   teyit edilsin; tüm allowed-user listesi istenmez. En fazla üç önceden onaylı
   TEST requester vakası kullanın: tek eşleşme, olmayan kimlik, bilinen aynı-ad
   çakışması. Desteklenen `GET /rest/api/2/user/search?username=<encoded-known-input>&startAt=0&maxResults=10`
   yalnız bu girdilerle, birer sayfa; kapsam genişletme yok. Dönüş: satır sayısı,
   tam `name` ve tam `displayName` eşleşme sayıları, en fazla iki eşleşmenin takma
   `name/key/displayName` değerleri, varsa `active/deleted`, eksik alanlar ve
   kesilme durumu. Limit dolduysa veya tamlık bilinmiyorsa `incomplete`; tekil karar
   vermeyin. Uygulama önce ordinal-ignore-case `name`, sonra ordinal `displayName`
   eşleştirir; sıfır/çoklu/belirsiz sonuçta mevcut Block politikası korunur.
   Gerçekte çakışma vakası yoksa üretmeyin, mevcut sanitized örnek veya `not-run`
   dönün. Arama uç noktası kendisi exact lookup değildir; istemci eşleşmesi ayrıdır.
   [Atlassian Find users, sürümlü referans](https://docs.atlassian.com/software/jira/docs/api/REST/9.0.0/).
   Native watcher iddiası varsa yönetici yalnız ilgili mevcut kuralın alias/sürüm,
   etkinlik, SDM/3 kapsamı, tetikleyici, koşul, alan-kullanıcı dönüşümü ve eylemini
   incelesin; kuralı çalıştırmasın/değiştirmesin. Bilinen tek TEST issue için mevcut
   execution kanıtı ve desteklenen `GET /rest/api/2/issue/<known-key>/watchers`
   okunabilir. Dönüş yalnız hedef takma kullanıcının varlığı, count ve rule-run
   ilişkisi; tam watcher listesi değil. Üyelik veya iki alandaki eşitlik tek başına
   otomasyonu/nedenselliği kanıtlamaz. Kanıt yoksa "watcher eklenir" denmez.

4. **Belirsiz Jira sonucu için güvenilir ilişki.** Önce mevcut yerel salt okunur
   kayıt/diagnostic görünümünden OR, transfer/deneme ve correlation referanslarını,
   UTC dispatch aralığını, mapping fingerprint, varsa Jira key ve reconciliation
   bayrağını alın; değerleri dışarıya takma değerle çıkarın. Mevcut create payload'ı
   uzak bir deneme kimliği göndermez; yerel Idempotency-Key uzaktan idempotency değildir.
   Bilinen key varsa, desteklenen `GET /rest/api/2/issue/<known-key>?fields=project,issuetype,created`
   ile yalnız `id/key`, proje/tip ve zaman eşlemesini inceleyin. Key bilinmiyorsa
   yöneticinin mevcut, güvenilir kayıt ilişkisinden sağladığı en fazla iki aday key
   aynı şekilde okunur. İsteğe bağlı bounded search yalnız önceden bilinen bu
   key'ler için `GET /rest/api/2/search`, `jql=project = SDM AND key in (<known-keys>)`,
   `startAt=0,maxResults=2,fields=project,issuetype,created` kullanır; JQL URL-encoded
   olur. Genel tarih/summary/SunucuTalep aramasıyla issue taraması yapılmaz.
   [Atlassian sınırlı JQL/fields örnekleri](https://developer.atlassian.com/server/jira/platform/jira-rest-api-examples/).
   Dönüş: key/id takma değerleri, OR/deneme ile otoritatif eşleşme dayanağı, sorgu
   kapsamı/tamlığı, `0|1|multiple|unknown`, görünürlük ve indeks gecikmesi sınırı,
   ilişkiyi doğrulayan owner. OR özet metni, etiket veya zaman benzerliği yeterli
   değildir. Sonuç 0 olması "create olmadı" değildir; 1 aday da tek başına ispat
   değildir. Güvenilir mevcut ilişki yoksa eksik kalır, yeni create/retry yapılmaz.
   Gelecek çözüm için owner; create ile aynı atomik işlemde taşınabilen değişmez
   OR+deneme işaretinin gerçek alan/özellik, eşitlik sorgusu, uniqueness ve görünürlük
   sözleşmesini sağlamalıdır. Varsayımsal custom-field/JQL/property veya ikinci
   POST ile sonradan ilişki yazımı önerilmez. Mevcut onaylı Jira create geçmişinden
   başarı/hata/timeout için yalnız HTTP durum, `id/key` şekli, hatalı alan adları,
   güvenli hata sınıfı ve dispatch/sonuç bilgisi dönsün; yeni create denenmez.

5. **BPM, Jira'dan ayrı.** Turuncu Hat sahibi önce mevcut tek-OR kapsamlı BPM
   request/response örneğini inceler. Yalnız ayrıca onaylı okumada, mevcut sözleşme
   ile `POST /query`, `BaseObject=BPM_Actvty`, `Selects=[id,m_created_dt]`, bilinen
   tek OR ID + task model/status/group/main-object/active filtresi kullanılır
   (tam gramer parity belgesinde). POST burada salt okunur query'dir; `/update`
   çağrısı yoktur. Kanıtlanmamış limit/sayfalama parametresi eklenmez. Dönüş:
   `QueryResult` alanlarının varlık/tipleri, gerçek `Items` sayısı, en fazla iki
   satırın tam `Key` adları ve takma `Value` tipleri, eksik/null/boş/hata bayrakları,
   truncation bilgisi. Sıfır/çoklu vaka yoksa veri değiştirmeden geçmiş örnek istenir.
   Geçmiş onaylı update sonuçlarından `UpdateResult.Success` tip/değer, ErrorNo,
   ErrorDescription/ErrorDetails boş/dolu/tip; varsa affected-row alanının adı ve
   sayısı, HTTP durum, koşul uyuşmazlığı ve timeout sonrası gözlenebilir son durum
   gerekir. Tamamını dump etmeyin; update yan etkilerini vendor açıklasın.
   Owner, version/ETag alanı, koşulun request konumu, atomik kapsamı, uyuşmazlık
   cevabı ve tekrarın yan etkisini belgelemeli. Destek yoksa açıkça "yok", bilinmiyorsa
   "bilinmiyor"; `If-Match`, compare-and-set veya idempotent update uydurulmaz.
   Önce okumak ile sonradan ID'ye update yapmak atomik değildir. Kapanış yorumunda
   watcher cümlesi ancak adım 3'ün sözleşmesi ve başarılı işlem kanıtıyla kullanılabilir.

### Owner İş Kararları

- Yetkili yayıncının açık inceleme/teyidi önerisi kabul ediliyor mu? Hangi kayıt
  kapsamı, zorunlu kanıtlar ve ret nedenleriyle? Mevcut fail-closed değerlendirme
  değişecekse ayrı onaylı politika/sürüm gerekir; otomatik uygunluk veya ayrı onaycı
  sistemi bu rehberle doğmaz.
- Requester bulunamaz/çokluysa mevcut Block korunur. Hangi kalıcı kimlik kaynağı
  onaylıdır; assignee/reporter ProjectDefault mı, açık ve doğrulanmış mapping mi?
  Custom-field yeterli mi, native watcher gerçekten iş gereksinimi mi?
- Jira-only başarı kabulü "Jira var, kaynak açık" mıdır? Mutabakat kararını kim,
  hangi otoritatif kanıtla verir? BPM kapanışı için ayrı owner/onay ve yarış/timeout
  riski kararı kimdedir? Yokluğa dair kanıt olmadan create tekrarı kabul edilmez.

### Jira-Only TEST ve Ayrı BPM Önkoşulları

**Hazırlık yapılabilir; rc6.11 ile yalnız ayar değiştirerek çalıştırılamaz.**
`OperationalRecordsOptions` bağımsız source-close kapısı sunmuyor;
`JiraTransferService.cs:379` key kalıcılığından sonra doğrudan close'a gider,
`:295` key bulunan retry'da close'u sürdürür. ConfigurationValidator'ın mevcut
TEST kapısı corporate provider çiftini ve ortak write iznini gerektirir.
`ReadOnlyIntegrationMode=true` Jira create'i de engeller. SourceProvider=Disabled,
fake başarı, eksik BPM config, ağ/izin hatası üretmek desteklenen Jira-only mod değildir.

Jira-only için ayrı, onaylı uygulama işi: varsayılan kapalı bağımsız BPM dispatch
fence (create ve retry'da), kalıcı Jira key/claim/command/audit koruması, kaynak
okuma ve stale-preview kontrolü, açık "Jira oluşturuldu, kaynak kapatılmadı"
sunumu; sahte Completed veya otomatik close kuyruğu yok. Yetki/duplicate/ambiguous
blokları ve business eligibility değişmeden korunmalı; mode-change/restart/retry
semantiği ve targeted SQL/browser regresyonları ayrıca doğrulanmalı. Bu çalışma
kod/mod/izin eklemez; mevcut paketler değişmez.

Jira-only dispatch öncesi adım 1-4, owner politikası, SQL kalıcılığı, gerçek Jira
success/failure sözleşmesi ve ayrı kontrollü TEST yazma onayı gerekir. Remote
reconciliation yoksa belirsiz vaka yalnız bloke tutulabilir; test sonrası güvenilir
sonuç belirleme yöntemi olmadan genel aktivasyon hazır sayılmaz. Yeni sonuç
örnekleri ancak ayrıca onaylanan testte alınır, bu salt okunur rehberde değil.
BPM close için bunlara ek adım 5, exact-key/tek aktivite, atomik koşul veya açık
owner risk kararı, update başarı/timeout sonrası reconciliation ve ayrı write
onayı gerekir. Jira başarısı BPM izni/başarısı sayılmaz.

### SQL Kanıtı ve Bu Görevin Doğrulaması

Atlanan sekiz SQL testi son parser/HTTP değişiklikleri için somut yeni bir SQL
açığı bırakmıyor: `74cd827` SQL repository/command store/schema/workflow kodunu
değiştirmedi. Gerçek adapter JSON hataları son 90 contract ve 38 hosted/API/OpenAPI
testiyle kontrol edildi. Eski LocalDB kanıtı claim, restart replay, key/history
rollback, SDM evaluation ve resources işlemlerini kapsar; corporate adapter yerine
substitute kullanır. Bunları tekrar koşmak yeni remote-response veya Jira-only
semantiğini kanıtlamaz. Önceki sekiz LocalDB testi ve browser kanıtı **yeniden
kullanıldı**, bu görevde test/build/SQL/browser/paket üretimi **çalıştırılmadı**.
Yeni Jira-only kalıcı geçişleri uygulanırsa o zaman hedefli SQL testleri gerekir.
Bu doküman değişikliği için içerik/link/kaynak referansı ve `git diff --check`
kontrolü yapılır; eski format ihlalleri veya test toplamları yeniden başarı sayılmaz.
