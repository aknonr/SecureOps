# API TEST Deployment Readiness

## RFC Reporter Source/UI Acceptance, 2026-09-12

Starting branch `feature/sql-runtime-hardening-20260902`, full HEAD
`b61c594b29e2c154b6afe38503d484a7b11b3586`; only pre-existing `.vscode/`
was untracked. This increment preserves the operator-approved RFC/Bildiren
mapping and explicit refresh path; it does not repeat corporate collection.

Baseline reproduction found four DOM rows and four selection options. The
server table reused the workbook's 28rem internal scroll region: 446px visible,
614px content, fourth row below the region. No projection row was lost.
Server rows now use normal page scrolling, stacked mobile presentation and
keyboard-operable answer buttons. Identity-based bulk preview remains explicit.
Ordinary rows show hostname/context/RFC/person/resolution; technical references
and verification time are in the existing collapsed source evidence view.
`RFC eşleşti` describes exact matching only, not server checks or ownership.

Current `<b>` versus retained `&lt;b&gt;` is intentional fixture input difference:
one source contains `&lt;b&gt;`, the other `&amp;lt;b&amp;gt;`. Both decode once.
New ReviewEvidence workbook rows now include reporter/RFC/reference/time with
the same plain-text contract. Raw snapshots, hashes and old XLSX are untouched.
Persisted OIDC fixtures cover trusted, absent and duplicate display names and
the existing successful profile-refresh path without changing immutable IDs.
The old long synthetic `test` identity was fallback fixture behavior, not proof
of a production name regression. Existing BOM reproduction/tests remain intact.

Commands/results (repository root, Release):
- `dotnet build SecureOps.sln -c Release --no-restore -v minimal`: 0 warnings/errors.
- `dotnet test SecureOps.sln -c Release --no-build -v minimal`: 1227 unit and 243 integration pass; 20 opt-in SQL tests skipped.
- `dotnet format SecureOps.sln --no-restore --verify-no-changes`: fails on existing unrelated whitespace/charset/import/naming findings. The same command with `--include` for all five changed C# files passes; no repository-wide cleanup was performed.
- Focused unit filter `FullyQualifiedName~InUse|FullyQualifiedName~Access`: 228 pass, no skips.
- Integration filter `FullyQualifiedName~InUse_ReporterAcceptance_NewFixtureOnly`: 1 pass, no skips, using the existing isolated LocalDB schema and a fresh `SECUREOPS_INUSE_ACCEPTANCE_SOURCE_ID` in 930000000..939999999.
- No migration harness was run. The narrow test refreshes fake HTTP into memory, inserts one NEW synthetic aggregate, then verifies SQL round-trip and new service reads without target calls. Existing RecordJson values compare unchanged. It does not replace the previous full adapter-to-SQL-refresh evidence.
- Browser: `node tests/browser/in-use-reporter.cjs <existing-playwright-core> https://localhost:63947/ http://localhost:5000/ <fresh-output> <synthetic-code>`; add `before` only against the starting binary to reproduce internal scrolling. Foreground Demo hosts used persisted SQL with source/Jira adapters Disabled; hosts stopped afterward.
- Browser checks passed: all four rows/options/keyboard paths, shared/different RFCs, retained denial/null RFC, inert UI/Excel text, trusted labels, unassignment, selected identities and six explicit differences, 1/4 then 3/4 counts, missing-answer focus, conflict preservation and authorization denial. Source/hash remain unchanged.

Screenshots and machine-readable results:
`C:\SecureOpsBuild\validation\inuse-acceptance-20260912\baseline` and `after-4`.
The latter contains `reporter-detail-{1440,390}.png`, `reporter-retained-evidence-*`,
`reporter-reviewer-labels-*`, `reporter-bulk-differences-*`, `reporter-conflict-*`
and `result.json`. These are synthetic local checks, not corporate/VDI acceptance.
No schema/config/auth change, migration, deployment or replacement package.
rc6.15, older diagnostics, data and audit history remain intact. Populated
Virtual PC User and separate Istem Sahibi remain independent unverified gaps.
Final format/diff counts and live push outcome are recorded in the task handoff.
Pre-commit live remote probe failed noninteractively: `unable to get password from user`.

## Verified RFC Reporter Connected, 2026-09-11

Source-only implementation on top of actual HEAD `744b7d37d168bd1da2a8586b134b4f339e4bcec3`.
The operator supplied evidence-only JSON and attested browser agreement: four
direct Service Items, SET.c_rfc_record String/OrCode, one exact referenced OR,
four links, KEY.p_rel_requester display and SET.p_rel_requester user reference.
This approves the specific RFC/Bildiren mapping, not ownership or Istem Sahibi.
Private values, LocalComparison, credentials and actor provenance are not stored
in source. Null Virtual PC User cells do not approve a populated-value contract.

Explicit In Use refresh now resolves each item's own RFC through the existing
exact resolver/session transport, including closed targets. It deduplicates
across the refresh, reads at most ten distinct targets and retains every link.
Identity/code disagreement, missing/ambiguous/denied results and budget overflow
remain explicit. Existing JSON persistence, audit, concurrency and source hashes
are reused. Failed/partial reads retain old evidence/time without claiming fresh
verification; whole failed or omitted-parent refresh also stales old reporters.
Optional WASAS reviewer assignments and historical archives remain unchanged.
List/detail show `İlgili talebi bildiren` with RFC, separately per service item
and grouped only by matching RFC/person/state in the list. Reads use SQL only.
The narrow Razor integration is part of this explicitly requested delivery.

Local UTF-8 BOM reproduction confirms that direct byte deserialization rejects
a BOM-bearing JSON input. Dictionary and RFC contract parsing now accept one
leading UTF-8 BOM, preserving original hash input and the 4096-byte bound. This
reproduces a compatible failure mechanism, not a forensic claim about the
operator's unavailable original failed file. Existing executables are unchanged.

Verification: Release solution and standalone collector builds; scoped format
verification, OpenAPI snapshot checks and public NuGet vulnerability check pass;
1223 unit tests;
243 non-SQL integration tests (19 SQL tests skipped in that invocation); all 19
SQL tests passed separately in the fresh isolated `RfcVerified20260911` harness.
The harness applied 001-013 only to new synthetic LocalDB databases. An initial
fixture reviewer lacked review permission and was corrected; rerunning the full
suite against a retained database hit an existing duplicate-fixture limitation.
No existing data was deleted to make tests pass. The final fresh-database run
passed, including adapter -> explicit refresh -> SQL -> new repository reads,
partial/whole failure, assignment retention, source versions and stale writes.
Synthetic SQL/browser evidence is not corporate deployment or VDI acceptance.

Playwright journey: `tests/browser/in-use-reporter.cjs`, desktop 1440x900 and
mobile 390x844, interactive list/detail, grouped/different RFCs, Turkish and
encoded markup, null RFC, denied stale values, capability denial and unchanged
DB versions. Screenshots/result are under
`C:\SecureOpsBuild\validation\inuse-rfc-connected-20260911\browser-verified`.
Only loopback Demo/Simulation hosts and fake HTTP were used; no corporate calls.

No new migration, runtime configuration, role, authentication or write activation
is needed. No replacement release was prepared; rc6.15 and both old diagnostic
deliveries remain unchanged. Source implementation is not a claim about the
corporate installed binary. Broader source completeness and populated Virtual
PC User/independent ownership semantics remain unverified. Remote synchronization
must be checked live; the pre-commit probe failed because credentials were not
available noninteractively. The final delivery reports the actual push result.

## RFC Reporter Diagnostic Preparation, 2026-09-11

The standalone delivery now separates DOM-backed candidate inspection (A) from
one exact RFC hop after operator representation verification (B). See
`scripts/diagnostics/InUseEvidence/operator-reporter-tr.md` for current Turkish
commands. Preserve the old C:\SecureOpsBuild\diagnostics\inuse-evidence-20260909
delivery; it cannot accept the new options. This is not an API/UI release.

Observed p_rel_requester is Bildiren, not the independently verified Istem Sahibi
field. Local code prepares per-server reporter evidence and existing JSON merge
semantics; production refresh and UI rendering await operator A/B evidence and
the existing UI ownership boundary. No migration is rerun. Corporate binaries
cannot be identified from source HEAD. No SQL/API, deployment or source write is
executed by this task. Exact new package metadata is recorded in
`docs/release-candidates/2026-09-11-inuse-reporter-diagnostic.md`.

<!-- TEST-RELEASE-RUNBOOK:START -->
## Birleşik TEST Teslimatı, rc6.15 (2026-09-10)

Teslimat kökü `C:\SecureOpsBuild\release\2026-09-10-pilot-rc6.15`.
Bu bölüm önceki rc6.14 ve kaynak-only notlarının güncel devamıdır. Kurulum ve
kurumsal yazma onayı değildir. Kesin build SHA, boyut, sürüm ve SHA-256 değerleri
`release-metadata.json`, `release-artifacts.sha256`, `manifests/*-files.json` ve
`*-payload.sha256` içindedir. Son belge HEAD'i build SHA'dan ayrı tutulur.
API/UI aynı net8.0 / FileVersion 0.1.0.0 build'idir; eski API ile yeni UI desteklenmez.

### İlk İşlem ve Kurulum Sırası

1. İlk işlem yalnız salt okunur envanterdir: gerçek API/UI hedefi, kurulu assembly
   ProductVersion/hash, mevcut config ve özel dizinlerin yedek referansı, DBA'nın
   doğruladığı nesne/constraint/trigger/grant listesi değişiklik kaydına yazılır.
   Tarihsel ekran görüntüsü tam şema kanıtı değildir. Ayrı kurulum onayı yoksa durun.
2. `Get-FileHash -Algorithm SHA256 -LiteralPath '<teslim alınan ZIP yolu>'` ile
   üç arşivi, açılan dosyaları manifestlerle karşılaştırın. Hash farkında durun.
   Runtime'a yalnız API/UI payload gider; DBA/runbook/metadata/evidence/staging gitmez.
3. Binary/config, SQL recovery point, In Use özel arşivi ve Data Protection ring
   yedeklerini doğrulayın. Geri dönüşte eski yazarlar additive JSON alanlarını
   kaybedebilir veya pozitif değerlendirmeyi bozabilir. Eski binary+013 yazma
   uyumluluğu kanıtlanmadı. Önce yerel/dış yazmaları durdurun; audit veya 012
   tablolarını silmeyin. SQL restore veri kaybı riskiyle ayrı DBA kararıdır.
4. DBA SQLCMD mode ve `-I` ile, `sql/migrations` dizininden yalnız eksikleri uygular:
   **012 tamamen doğrulanmışsa yalnız `013-sdm-pilot-policy.sql`; 011 ise 012 ve 013.**
   DBA ZIP 001-013 zinciridir. 001/002/012 körlemesine yeniden çalıştırılmaz.
   013 transaction içinde `ops.OperationalRecords` üzerindeki
   `CK_OperationalRecords_SdmEvaluation` kısıtını WITH CHECK ile değiştirir;
   yeni tablo/kolon/rol/veri güncellemesi yoktur. Hata halinde otomatik repair yoktur.
5. Yeni runtime grant yoktur. 012 izinleri: `ops.InUseRecords` SELECT/INSERT/UPDATE;
   `ops.InUseRefresh` SELECT/UPDATE. Refresh Id=1/Version=0 satırını 012 oluşturur;
   runtime INSERT etmez. Eksik satırda izin genişletmeyin. Mevcut ops kayıt/transfer/
   command SELECT/INSERT/UPDATE, workflow history ve audit INSERT, access/reporting
   ve resources izinleri canonical Database Contract ile doğrulanır. DELETE, DDL,
   db_owner, audit UPDATE/DELETE verilmez. DBA DDL kimliği runtime'dan ayrıdır.
6. `InUseReports:Directory` için mutlak, deployment/webroot dışında özel dizin
   hazırlığı gerekir. API işlem kimliğine okuma/listeleme, dosya/dizin oluşturma,
   yazma ve atomic move için gerekli sınırlı dizin ACL'si verilir; UI/IIS static
   erişimi açılmaz. Reparse-point kullanılmaz. Aynı OR/sürüm tek immutable JSON
   envelope içinde XLSX+aktör/zaman/boyut/hash tutar; eski raporlar silinmez.
   Arşivlenmiş olmak Turuncu Hat'a eklenmiş olmak değildir.
7. API/UI için ayrı kalıcı `DataProtection:KeyRingPath`, sabit `ApplicationName`
   ve `Mode=FileSystemDpapi` (tek Windows host) veya mevcut onaylı sertifika modu
   korunur. Her işlem kimliği yalnız kendi ring'ine gerekli erişimi alır.
   DPAPI makineye bağlıdır; çok host/LB taşınabilirliği varsayılmaz. Ring, kimlik,
   uygulama adı, HTTPS ve `__Host-` cookie kapsamı doğrulanır; sır/key içeriği alınmaz.
   Eski ephemeral cookie/token için yeniden giriş gerekir; antiforgery kapatılmaz.
8. Ayrı kurulum penceresinde API, sağlık/sürüm/auth kontrolü, sonra eşleşen UI.
   `web.config`, `appsettings*.json`, server-owned secrets, ring, log ve rapor dizini
   korunur. IIS environment değişiklikleri ayrı inceleme konusudur; web.config JSON
   değildir. Bu görev deployment yapmaz.

### Salt Okunur Kabul

- Mevcut provider/session ayarları korunur. `ReadOnlyIntegrationMode=true`,
  `ControlledTestWritesEnabled=false`, `SourceCloseEnabled=false` kalır.
  `OperationalRecords:Pilot` varsayılan boş/kapalıdır; yeni onay kaydı teslim edilmez.
- Resources.View olan non-admin kişisel grup oluşturur/kaydeder/sıralar/açar;
  başka kişinin API verisine erişemez. Shared yönetim Resources.Manage ister.
  Üç kontrollü link site popup izniyle üç hedef açar; default/managed engelde ayrı
  native linkler kullanılır. Açma isteği hedefe giriş başarısı değildir.
- InUseReviewer View/Review; InUseCoordinator ve Admin ayrıca Assign/Refresh.
  Eski Operator/Lead otomatik In Use hakkı almaz. Atama opsiyoneldir; üç cevap,
  seçilmiş sunuculara fark önizlemeli toplu uygulama, eksik taslak, çatışma ve
  sürüme bağlı preview/arşiv denenir. Direkt route/API yetki reddi doğrulanır.
  Yönetim panosu ayrıca Reporting.ManagementView ister; sayımlar OR bazındadır.
- Gerçek 15 alanlı servis öğesi projeksiyonu desteklenir; gözlenen adet global
  tamlık değildir. RFC/Virtual PC User/Reporter/teknik creator/Affected Assets ve
  In Use kaynak açılış/durum eşlemeleri doğrulanmadıkça bilinmiyor kalır.
  Kurumsal Excel şablon kabulü, kaynak upload/kapanış ve RFC sahipliği operasyonel
  değildir. Yerel completion journal dış yazı göndermez.

### Ayrı Tek Kayıt Jira-only Pilotu

Pozitif kod vardır; iş kararı ve aktivasyon yoktur. Somut öneri ADR-0018:
yalnız Sunucu Talebi, bir sayısal OR, exact kaynak hash/kapsam/mapping sürümü,
süreli karar ve gerekçe; yapılandırılmış sunucu referansı destekleyici/opsiyonel.
İş sahibi bu kuralı kabul etmeli veya zorunlu ek bilgi koşulunu somutlaştırmalıdır.
Tür beyanı/free text onay değildir; ikinci onaycı kuralı eklenmemiştir.

Server-owned `OperationalRecords:Pilot` alanları: `RuleSetVersion` =
`WASAS-SDM-PILOT-2026.09-v1`, `ApprovalReference`, `TrackingReason`, `SourceRecordId`,
`SourceFingerprint`, `SourceScope`, `MappingVersion`, `ExpiresAt` (UTC),
`RequestType=ServerRequest`. Hash yetkili kayıt detail API'sinin `sourceFingerprint`
alanıdır; token/sır değildir. Kapsam `SMSS_oRFF:<RelatedGroupId>:<sıralı ExcludedDccIds>`;
4241 hariç tutulmalıdır. Aktiflik/kapsam mevcut exact-ID kaynak sorgu filtresinden
gelir, yeni response alanı varsayılmaz. Kaynak değişirse karar yeniden ele alınır.

Kalan kanıt: mevcut Jira proje/issueTypeId/team/requester-watcher/label mapping
sürümü, requester ve authenticated-operator reporter exact hesap çözümü ve create
response sözleşmesi TEST'te doğrulanmalı. Uygulama Kurulumu ve İade için ayrı
mapping eksik; pozitif yayın kapalıdır. Kaynak Reporter alanı tahmin edilmez.
JiraPublisher capability ve ayrı bir-record aktivasyon onayı gerekir; In Use beklenmez.
Onay sonrasında akış: güncel kaynak -> yetkili `jira-preview` -> içerik/fingerprint
ve kaynak açık kalacak uyarısı -> açık create onayı -> persisted intent -> Jira key.
SourceCloseEnabled false ve In Use dış adımları kapalı kalır.
Timeout/key-persistence belirsizliğinde create tekrarlanmaz. Bilinen en fazla iki
Jira key için mevcut onaylı salt okunur kontrol, `turuncu-hat-jira-contract-gaps.md`
kapsamında yapılır. Bilinmeyen key'i arayan doğrulanmış uzak correlation sözleşmesi
yoktur; source OR kodunun summary'de olması unique arama/absence kanıtı değildir.
Bu sözleşme için sistem sahibinin dar arama alanı/semantiği ve temizlenmiş örneği
gerekir; otomatik reconciliation çözümü veya exactly-once iddiası yoktur.
<!-- TEST-RELEASE-RUNBOOK:END -->

## Resources Pre-Package Source Correction, 2026-09-10

The canonical UI README records actual three-target popup-policy reproduction,
personal-group recovery and published local verification. No new release package
or deployment was authorized or produced. rc6.14 and older artifacts stay unchanged.
No schema, runtime grant, API contract, provider configuration or write-fence delta.
Local Chrome default blocking opens one target; an explicit site allowance opens
three. Actual TEST managed policy/installed binary/catalogue identity remains an
operator verification, not a locally established root cause. The full In Use
waiting-age/dashboard, RFC ownership and controlled completion/reconciliation
backlog remains active; do not package the combined milestone as complete.

## Optional Review And RFC Diagnostic Source Update, 2026-09-10

The current canonical UI handoff records optional capability-authorized review,
stored conflict comparison and cross-instance refresh exclusion. Schema remains
012; no new runtime grants/configuration. The fixed transaction-owned application
lock uses existing PUBLIC access, verified with an isolated least-privilege user.
The standalone source has an optional approved RFC/Reporter dictionary contract;
the preserved collector archive does not contain it. No deployment or replacement
package was made. Persisted RFC ownership, management dashboard and durable source
completion remain LOCAL implementation work as well as corporate contract gates.
Existing corporate write fences and the independent SDM continuation are unchanged.

## Service-Item Mapping Source Update, 2026-09-10

The operator's bounded evidence now supports the original 15-select service-item
projection. The implemented adapter/storage/UI/XLSX mappings and verification
are in `src/SecureOps.Ui/README.md`, with exact keys in the legacy parity record.
Explicit refresh maps observed rows only; completeness stays unverified. Missing
previous rows/fields retain prior evidence and prevent current archival. Virtual
PC User/RFC, creator/status and affected-assets mappings remain unqueried.
This source update is NOT in preserved rc6.14 or the standalone collector ZIP.
No deployment/package, migration, grant or runtime configuration change occurred.
The historical delivery and initial evidence-collection procedure below remain
applicable to those older artifacts, not proof that all relationships are missing
from the current source. The remaining two-field dictionary requires exact direct
keys, types/null/cardinality/reference targets and sanitized KEY/SET examples.

## In Use TEST Teslimatı, rc6.14 (2026-09-09)

Bu bölüm rc6.14 için güncel kurulum prosedürüdür; aşağıdaki rc6.13 sırası yalnız
eski paket içindir. Son mevcut release metadata rc6.13 olduğundan yeni teslimat
`C:\SecureOpsBuild\release\2026-09-09-pilot-rc6.14` olarak ayrılır. Önceki
arşivler değişmez. Hazırlık kurulum, kurumsal kabul veya yazma onayı değildir.
Kesin build SHA, ProductVersion, boyutlar ve hash'ler bu kökteki
`release-metadata.json`, `release-artifacts.sha256` ve `manifests/` içindedir.
API/UI aynı build SHA'dan hazırlanır; sonraki belge HEAD'i build SHA değildir.
FileVersion 0.1.0.0 / net8.0 korunur. Yeni UI eski API ile desteklenmez.

### İlk Manuel Adım ve Kurulum

1. Yetkili TEST operatörü yönetim istasyonundan, salt okunur olarak gerçek API/UI
   hedeflerini, kurulu assembly ProductVersion/hash'lerini, server-owned config
   yedek referanslarını ve DBA'nın doğruladığı şema seviyesini değişiklik kaydına
   yazar. Tarihsel ekran görüntüsü tüm güncel nesne/kolon/index/trigger/grant
   tanımlarını kanıtlamaz. Hedef, ayrı kurulum onayı veya rollback planı yoksa durun.
2. Teslim alan istasyonda `Get-FileHash -Algorithm SHA256 -LiteralPath <arşiv>`
   ile API/UI/DBA arşivlerini ve açılan dosyaları ilgili manifestle karşılaştırın.
   Uyuşmazlıkta durun. Yalnız API/UI runtime payload'ları uygulama dizinine gider;
   DBA, runbook, metadata, manifest, staging ve evidence uygulama payload'ı değildir.
3. Operatör/DBA ayrı onayla binary, config ve geri yüklenebilir SQL yedeğini
   doğrular. rc6.13 binary'lerinin 012 ile rollback uyumluluğu test edilmemiştir.
   Eski paket mevcut diye güvenli rollback varsaymayın. SQL restore ayrı DBA
   kararıdır; sonradan oluşan audit/review verisini kaybettirebilir. 012 tablolarını
   veya append-only kanıtı silerek geri dönülmez.
4. DBA yalnız eksik migration'ları sırasıyla uygular. **011 tamamen doğrulanmışsa
   yalnız `sql/migrations/012-in-use-workspace.sql` gerekir.** SQLCMD mode, `-I`
   ve `sql/migrations` çalışma dizini zorunlu; entrypoint
   `:r ../schema/012-in-use-workspace.sql` kullanır. Paket 001-012 zincirini içerir;
   tamamını körlemesine çalıştırmayın. 012 tekrarlanabilir değildir: mevcut nesne
   veya rol çakışmasında durur; hatada otomatik retry/repair yoktur.
5. DBA `ops.InUseRecords` ve `ops.InUseRefresh` tanımlarını schema/012 ile,
   FK/index/check'leri ve etkin audit korumalarını karşılaştırır. Migration aynı
   transaction'da `ops.InUseRefresh` için Id=1, Version=0 başlangıç JSON satırını
   ve security.Roles 8=InUseReviewer, 9=InUseCoordinator tanımlarını oluşturur.
   Runtime başlangıç satırı INSERT etmez. Satır eksikse INSERT iznini genişletmek
   yerine migration/tanım tutarsızlığını DBA inceler.
6. Yalnız eksik izinler, gerçek onaylı runtime principal'a ayrı DBA onayıyla:

   ```sql
   GRANT SELECT, INSERT, UPDATE ON OBJECT::ops.InUseRecords TO [approved_runtime_principal];
   GRANT SELECT, UPDATE ON OBJECT::ops.InUseRefresh TO [approved_runtime_principal];
   ```

   Mevcut audit.AuditLog INSERT, ops.CommandExecutions ve access okuma izinleri
   korunur. Yeni DELETE, DDL, db_owner veya audit UPDATE/DELETE izni yoktur.
7. API sonra eşleşen UI mevcut onaylı kurulum yöntemiyle kurulur. Her aşamada
   sağlık/sürüm/auth doğrulanır; başarısızsa sonraki aşamaya geçilmez. `web.config`,
   `appsettings*.json`, sırlar, Data Protection ve log dizinleri korunur.

### Konfigürasyon ve Rol Kabulü

Yeni InUse provider/config anahtarı yoktur. SQL seçimi
`Access:RepositoryProvider=SqlServer` ile yapılır (varsayılan InMemory);
`ConnectionStrings:SecureOpsDb` mevcut server-owned bağlantıdır. Kaynak seçimi
`OperationalRecords:SourceProvider=TuruncuHat` (varsayılan Disabled); mevcut
TuruncuHat session/query ayarları yeniden kullanılır. Fake/Simulation yalnız
yerel sentetik profillerdir. TEST ortamı `Test`, mevcut Jira provider `Corporate`
ve doğrulanmış mapping ayarları korunur. Yeni secret veya config kopyası istenmez.
`ReadOnlyIntegrationMode=true`, `ControlledTestWritesEnabled=false`,
`SourceCloseEnabled=false` değişmez. Yeni şema dışında rc6.13'e runtime config
delta beklenmez; Access zaten SqlServer değilse kalıcılık geçişi ayrıca incelenir.

- Admin ve InUseCoordinator: View/Review/Assign/Refresh. InUseReviewer: yalnız
  View/Review; taslak save sadece mevcut atanmış inceleyicide. Excel aynı Review
  capability ile güncel kayıt/taslak sürümünü kontrol eder. Eski Operator/Lead
  otomatik In Use yetkisi kazanmaz; rol seed etmek kullanıcı atamak değildir.
- Yetkisiz kullanıcıda menü görünmez; `/in-use`, doğrudan list/detail/refresh/
  assignment/draft/report API istekleri engellenmelidir. Yetkili Reviewer için
  refresh/assignment reddi de kontrol edilir. Resources kişisel sınırları değişmez.
- Koordinatör açık refresh yapar; kayıtlı liste, arama, tümü/bana atanan/atanmamış,
  sayfa boyutu ve sayfalar denenir. Eksik kaynak sonucu toplam envanter sayılmaz.
  Hatalı refresh önceki kayıtları silmemeli, stale/hata görünmelidir.
- Atama gerekçesiyle inceleyici seçilir; requester, teknik creator, Virtual PC
  User, servis sahibi ve WASAS inceleyicisi ayrı kontrol edilir. Gerçek join henüz
  yoksa 0 sunucu yerine sorgulanmadı/sözleşme bekleniyor görülmesi beklenir.
- Sunucu cevapları ayrı incelenir; bilinmeyen kontroller Unknown kalır. Toplu
  cevap için seçim ve açık onay gerekir. İki oturumda aynı sürümü kaydetmek/atamak
  conflict üretmeli; kaynak değişince eski taslak ve export güncel sayılmamalıdır.
- Excel önce preview sonra download: Sunucular 29 satır, NMS 22 kolon; iki teknik
  sheet boş, Provenance/ReviewEvidence ekli. Kurumsal şablon sahibi ek sheet,
  boş/Unknown hücre, layout ve mapping kabulünü ayrıca vermelidir. Bu dosya
  tamamlanmış teknik kontrol veya yüklenmiş/onaylanmış kaynak eki değildir.
- Rehber replay/klavye gezinmesi refresh/save yapmamalıdır. Sağlık/audit ve
  onaylı entegrasyon kayıtlarıyla hiçbir Jira create, attachment upload, kaynak
  update veya BPM closure olmadığını kontrol edin; bu eylemleri test için çalıştırmayın.

### Tek Kayıt İlişki Kanıtı Toplama

Yalnız ayrıca onaylı TEST API'de, mevcut server-owned TuruncuHat kimliğiyle. Admin
veya hem InUse.Refresh hem OperationalRecords.ViewDiagnostics yetkisi gerekir.
InUseCoordinator tek başına diagnostics yetkisi almaz. Kaynak OR kimliğini
operatör açıkça sağlar; kayıtlı WASAS GUID ve sürümüyle eşleşmek zorundadır.
OIDC etkinse API JWT bearer bekler; UI token'ı sunucuda saklar. Windows
UseDefaultCredentials bu modele uygun değildir. Negotiate yalnız doğrulanmış
OIDC-kapalı Windows profilinde geçerlidir. Dağıtılmış authentication profili bu
görevde okunmadı; endpoint için genel bir PowerShell Windows-auth çağrısı önerilmez.

**UI dağıtmadan bağımsız yol:** `scripts/diagnostics/InUseEvidence` mevcut session/
query transport'unu kullanan dar .NET 8 konsoludur. Yalnız ayrıca izin verilmiş
TEST Windows API/yönetim host'unda, server-owned config dosyasını okumaya yetkili
operatör kimliğiyle çalışır. WASAS OIDC oturumu yerine bu ayrı yönetim izni gerekir;
bir UI capability atlaması veya genel kullanıcı aracı değildir. Araç geliştirme
makinesinde bağımsız win-x64 olarak derlenir; hedefte SDK/repo gerekmez.
Hedefte x64 Microsoft.NETCore.App ve Microsoft.AspNetCore.App 8.0 shared runtime
gerekir; paket içindeki runtimeconfig ve metadata ile doğrulayın. Tüm `tool/`
bağımlılık ağacı korunur; yalnız exe kopyalanmaz. UI/API dağıtımı gerekmez.
Config mevcut etkin TuruncuHat/OperationalRecords bölümlerini içeren korunan
dosyadır; repoya kopyalanmaz. Write fence üçlüsü true/false/false zorunludur.

Sistem sahibinden yalnız `LCSIMS_ServiceInstance` için **Virtual PC User** ve
**RFC Kaydı** alanlarının doğrudan property anahtarı, veri tipi, null/çokluk ve
referans hedefini içeren iki satırlık alan sözlüğü isteyin. Metadata endpoint'i
kanıtlanmadığından bu sözlük kaynaktan otomatik çekilemez. Onaylı iki anahtar
`dictionary.json` içinde bu iki görünür etikete eşlenir; gerçek anahtar yerine
tahmin yazmayın. `{}` ile mevcut 15 select'in yapısı toplanabilir, fakat bu iki
alan toplanmış sayılmaz. Nested selector, serbest query veya başka etiket reddedilir.

Bağımsız teslimat: `scripts/release/New-InUseEvidencePackage.ps1`; operatör
kılavuzu ve kabul edilen JSON yapısı: `scripts/diagnostics/InUseEvidence/README.md`
ve `server-config.example.json`. İlk çalışma için teslim edilen `dictionary.json`
yalnız `{}` içerir. Aşağıdaki yollar operatör tarafından sağlanır; `100` gerçek
onaylı tek sayısal kaynak OR kimliğiyle değiştirilmesi gereken sentetik örnektir:

2026-09-09 standalone teslimat dizini:
`C:\SecureOpsBuild\diagnostics\inuse-evidence-20260909`. Kesin build SHA/runtime
`delivery-metadata.json`, boyut ve dosya hash'leri `payload-manifest.json` /
`payload.sha256`, ZIP hash'i `archive.sha256` içindedir. rc6.14 değişmez.
Yerel collector Release build'i 0 uyarı/hata ve 116 odaklı unit testi geçti:
gerçek DI/session bileşimi sentetik login + sıfır/dört ilişkili kayıtla, boş
sözlükte tam 15 select ve sanitize alias davranışı doğrulandı. Kurumsal çağrı yok.

```powershell
& 'C:\OPERATOR_TOOL_DIRECTORY\tool\InUseEvidence.exe' 'C:\OPERATOR_PRIVATE_CONFIG\server-config.json' '100' 'C:\OPERATOR_TOOL_DIRECTORY\dictionary.json' 'C:\OPERATOR_PRIVATE_OUTPUT\inuse-one-or.json' --collect
```

Collector yalnız tek JSON dosyası okur. IIS `web.config` XML'i, IIS environment
ayarları veya `__` anahtarları JSON yerine geçmez; otomatik overlay yoktur.
Etkin ayarlar yalnız IIS ortamındaysa yetkili config sahibi, ayrıca onaylı yerel
hazırlıkla sadece TuruncuHat ve OperationalRecords bölümlerini korunan JSON'a
aktarır. IIS değişmez; config teslimata/Git'e/evidence'a girmez. Örnek dosyanın
boş/0 kimlik değerleri çalıştırılabilir config değildir. SDK kurulumu istenmez.
Dört argüman sadece korunan mutlak yollar ve sayısal kimliktir; secret/header/token
argümanı yoktur. Yeni çıktı dizini sadece operatör/
entegrasyon sahibine ACL ile açık olmalı. Mevcut çıktı üzerine yazılmaz. Yerel
dosyada Windows actor SID/zaman/sözlük hash'i tutulur; yalnız **Evidence** bölümü
paylaşılır. Başarı exit 0 ve `CollectedNotMapped`; hata/yarım dosya başarı değildir,
durun. Yerel dosya standalone attempt kaydıdır; SQL/WASAS audit kaydı değildir.
RFC hedef OR'u, affected assets veya kullanıcı envanteri sorgulanmaz. Karışık
Value object/array yanıtında araç durur: sahibinden sadece o alanın kişisiz,
tip/null/çokluk yapısı gerekir. Kaynak payload dump veya credential istenmez.

Helper önce exact aktif kategori 4241/grup 68 OR'u tekil doğrular; sonra yalnız
`rel`, m_tid=100049 ve m_lid=bu OR için tam scriptteki 15 select'i sorgular.
Bağımsız konsol yalnız onaylı sözlük verilirse bu listeye en çok iki doğrudan
alan ekler; eski deployed API helper bu sözlük parametresini kabul etmez.
Bir ilişki seviyesi, en çok 10 sonuç/64 hücre, 3 iç içe array seviyesi, her başarılı
response en çok 64 KiB ve toplam 45 saniye; transport en çok bir session yenileme
tekrarı yapar. Sonuç bounded olduğu için kurumsal tamlık kanıtı sayılmaz.
Ham değerler/log dump yerine yalnız key/type ve çağrı-içi tutarlı value-N
alias'ları döner. Kimlik, isim, IP, description, credential/session çıkmaz;
alias haritası saklanmaz. Sadece bu küçük sanitize çıktı onaylı kanıt alanında
tutulur, Git'e kurumsal screenshot/kişisel değer eklenmez. Kaynak veya yerel
review değişmez; yalnız istek/hazırlık audit'i vardır. Yetki, tekillik, süre,
boyut, key biçimi veya audit hatasında durun; kapsamı genişletmeyin.

Beklenen eksik kanıt: bu çıktının service-item key/type/çokluk yapısı ile sistem
sahibinin onayladığı alan sözlüğü. Virtual PC User, RFC Kaydı, teknik OR creator,
servis sahibi ve Etkilenen Varlıklar için wire property/join henüz kanıtlanmadı;
helper bu alanları tahmin edip sorgulamaz. Sistem sahibi bir OR için 4 servis öğesi /
0 affected asset örneğini ve sıfır/çoklu ilişki semantiğini alias'larla eşleştirmeli.
RFC başka OR'a gidebilir; Virtual PC User otomatik atama kaynağı değildir.
Kaynak navigasyonunun yapılandırılmış onaylı route'u yok; screenshot task query
parametreleriyle link üretilmez. Operational Records mevcut ServerReference ve
ApplicationReference'ı gösterir; gerçek adapter bunları henüz çözmediğinden
ilişkili envanter tamamlandı iddiası yoktur. Per-row remote lookup yoktur.

**SDM ayrı devam:** gerçek classifier daima NeedsManualReview/false döndürür;
JiraIssueDraftService request-type review CategoryPolicyPending bırakır. Sunucu
talebi için pozitif politika iş kararı, bunun uygulanması/testi, kurumsal alan/
kimlik/create/reconciliation kanıtı ve ayrı aktivasyon onayı gerekir. Yalnız config
değişikliği yeterli değildir. In Use ilişki/şablon veya scheduler SDM önkoşulu değildir.

## Current Evidence Boundary, 2026-09-08

The operator reports manual rc6.13 deployment to TEST. This task has not verified
deployed API/UI hashes, schema/grants or corporate acceptance checks; the runbook
below remains the acceptance reference. No deployment, release repackaging,
corporate SQL/API call or write-fence change was performed. In Use V1 is implemented
and verified locally, with additive migration 012 applied only to isolated LocalDB.
Its canonical implementation/evidence/remaining-contract handoff is
`src/SecureOps.Ui/README.md`; exact future runtime grant deltas are in `sql/README.md`.
It is not included in rc6.13. Do not apply 012 or deploy this source as part of the
existing rc6.13 runbook without separate rollout approval. The rc6.13 pilot below
still uses its own 001-011 baseline and does not wait for In Use rollout.

Remaining prerequisites for a **single-record Jira-only TEST pilot**, independent
of In Use development:

- Verify deployed API/UI pairing and 001-011 persistence/grants against the existing
  runbook; the operator deployment report alone does not establish these checks.
- Approve the selected record's positive eligibility policy and required evidence,
  then implement/verify that bounded policy. The current operator-declared request
  type alone remains blocked. Software installation additionally needs its approved
  label/field mapping; this is not required for a server-request-only pilot.
- Confirm current Jira product/version, SDM/type-3 required fields and effective
  integration-user permissions; verify the two custom-field/requester contracts and
  the chosen assignee/reporter policy. Preserve unresolved-requester Block behavior.
- Establish accepted create success/failure shapes and an authoritative method and
  accountable operator for reconciling an uncertain result without a second create.
- Obtain separate single-record TEST write approval and a reviewed activation delta;
  current values remain ReadOnlyIntegrationMode=true, ControlledTestWritesEnabled=false,
  SourceCloseEnabled=false. Jira-only approval must keep source close disabled.

Exact bounded evidence requests remain in
`docs/integrations/turuncu-hat-jira-contract-gaps.md`, numbered steps 1-4 and owner
decisions. BPM step 5 is not a Jira-only prerequisite. No In Use completion,
background worker or In Use ownership/template contract is a dependency of this pilot.

## Güncel TEST Operatör Runbook, rc6.13

Paket kökü: `C:\SecureOpsBuild\release\2026-09-07-pilot-rc6.13`.
API/UI build kaynağı: `1935dc522e70b0fcfe602812bffb06f2858d61b1`;
son runtime commit'i: `2b895f6e6c66553be52f44471a231892865a9937`.
Gerekli şema **001-011**. Sonraki teslimat-dokümantasyon commit'i build SHA'sını
değiştirmez. Kanonik kaynak/kanıt handoff'u: `src/SecureOps.Ui/README.md`.
Bu hazırlık kurulum veya yazma aktivasyonu değildir; canlı uzak Git yayını da
yerel build kanıtından ayrıdır. Kesin paket hash'leri `release-artifacts.sha256`,
dosya hash'leri `manifests/`, kaynak ilişkisi `release-metadata.json` içindedir.
Yalnız API/UI/DBA arşivleri ve teslimat belgeleri dağıtılır; `staging/` ve
`evidence/` build istasyonunda kalır, uygulama dizinine kopyalanmaz.

rc6.12 API/UI paketleri çalışma alanı değişikliklerini içermez ve korunmuştur.
DBA arşivi rc6.12'den byte-for-byte yeniden kullanılır: SQL kaynağı
`682fa8eafcac611b0d18f93d0eb541f6a5acd2fc`, arşiv SHA-256
`CE38FF8ECDA060B3111E7C9FE9A23A2F5AD6088E36499478FAFBF9A2D317F2D1`.
24 arşiv girdisi ve güncel kaynakla aynı 22 SQL dosyası doğrulandı; yeni şema/grant
yoktur. DBA ZIP içindeki rc6.12 runbook'u tarihsel belgedir; **yalnız yeni paket
kökündeki `operator-runbook-tr.md` güncel kurulum sırasıdır**. Arşivi yeniden
paketlemeyin veya içindeki tarihsel belgeyi güncel talimat olarak kullanmayın.

### Tek Güncel Kurulum Sırası

| Adım / makine | İşlem türü | Önkoşul, beklenen çıktı ve durma koşulu |
|---|---|---|
| 1. Yetkili TEST operatörü, yönetim istasyonu | Salt okunur envanter | Değişiklik kaydına gerçek API/UI hedeflerini, mevcut binary/config sürümlerini, DBA tarafından doğrulanmış şema seviyesini ve rollback paketini yazın. Hedef, şema, geri dönüş tabanı veya ayrı kurulum onayı eksikse durun. İlk manuel TEST adımı budur; sunucu adı veya mevcut seviye bu belgede varsayılmaz. |
| 2. Paket teslim alan istasyon | Salt okunur doğrulama | API/UI/DBA arşivlerinin `Get-FileHash -Algorithm SHA256 -LiteralPath <arşiv>` sonuçlarını `release-artifacts.sha256` ile karşılaştırın; per-file manifestlerini doğrulayın. API/UI ProductVersion `0.1.0+1935dc522e70b0fcfe602812bffb06f2858d61b1` ve metadata kaynak SHA'sı eşleşmeli. Eksik/farklı hash, sürüm veya manifesto halinde durun; rc6.12 veya önceki API/UI binary'lerini karıştırmayın. Build kaynağı normal push sonrası canlı uzak SHA ile doğrulandı; sonraki belge commit'i build kaynağı değildir. |
| 3. TEST operatörü ve DBA, doğrulanmış hedefler | Ayrı onaylı yedek hazırlığı | Mevcut API/UI binary ve server-owned config yedeklerinin yolunu, hash'ini, tarihini, geri dönüş sürümünü; geri yüklenebilir SQL yedeğinin referansını ve sorumlusunu değişiklik kaydına ekleyin. Sırları belgeye/pakete koymayın. 011 sonrası eski binary uyumluluğu kanıtlanmış değildir; geri dönüş planı ve uyumluluk değerlendirmesi yoksa kurulumdan önce durun. |
| 4. DBA, doğrulanmış TEST SQL hedefi | Ayrı onaylı şema değişikliği | Envanterde eksik olduğu doğrulanan migration'ları sırasıyla 011'e kadar uygulayın; mevcut 010 ise yalnız 011 gerekir. Arşivde `sql/migrations` çalışma dizininden SQLCMD mode ve QUOTED_IDENTIFIER ON (`-I`) kullanın; `:r ../schema/...` yollarını koruyun. 001/002 yeniden uygulanmaz; tüm zincir körlemesine çalıştırılmaz. Hata veya uyuşmayan mevcut tanımda durun; otomatik yeniden deneme yapmayın. |
| 5. DBA, aynı SQL hedefi | Salt okunur kontrol; yalnız eksik grant için ayrı onaylı değişiklik | 011 kolonlarını iki tabloda `bit NOT NULL DEFAULT (0)` olarak, mevcut satırların false niyetini ve append-only trigger'ların etkinliğini doğrulayın. Aşağıdaki mevcut nesne grant'lerini gerçek runtime principal ile kontrol edin. 011 yeni runtime grant istemez. DDL kimliği runtime'dan ayrı olmalı; ownership-chain doğrulanmadan API'ye geçmeyin. |
| 6. TEST API operatörü, doğrulanmış API makinesi | Ayrı kurulum onayıyla binary değişikliği | Şema/grants tamamlanınca yalnız eşleşen API arşivini mevcut onaylı dağıtım yöntemiyle kurun. `web.config`, `appsettings*.json`, Data Protection dizini, loglar ve sırlar korunur. Aşağıdaki üç fence değerini server-owned yapılandırmada doğrulayın. Sağlık/sürüm/kimlik doğrulama başarısızsa durun; UI'ye geçmeyin. |
| 7. TEST UI operatörü, doğrulanmış UI makinesi | Ayrı kurulum onayıyla binary değişikliği | Aynı build SHA'lı UI arşivini kurun; mevcut API adresi, auth/offload ayarları ve kalıcı UI key ring korunur. LB/IIS/binding yeniden yapılandırması kapsam dışıdır. API uyumsuzluğu veya key-ring sorunu varsa durun. |
| 8. Yetkili kullanıcılar, TEST tarayıcısı | Salt okunur rol kontrolü; yerel uygulama değişiklikleri için ayrıca onaylı UAT | Admin sağlık/sürüm; ordinary kullanıcı bağlantı arama ve grupları; curator yönetim yetkisi; manager tarih/boş/hata durumları; denied kullanıcının menü/doğrudan API reddi doğrulanır. Publisher iki talep türünü, gerçek engelleri ve yazma kapalı açıklamasını inceler; create/retry yapmaz. Katalog/grup kaydı gibi uygulama içi değişiklikler ayrı UAT onayına bağlıdır. Kaynak yenileme/AD/Jira çağrıları ayrıca kurumsal smoke onayı gerektirir. Beklenmeyen yazma, yanlış yetki veya sentetik corporate kayıt görünümünde durun. |

### Kısa TEST Kontrol Listesi

Bu liste ayrı kurulum/UAT onayından sonra gerçek TEST tarayıcısında yürütülür;
bu teslimatta uygulanmış değildir. Yerel sentetik kanıt kurumsal kabul değildir.

1. **API/UI eşleşmesi:** her iki assembly ProductVersion değerini yukarıdaki tam
   SHA ile, arşiv/dosya hash'lerini manifestlerle karşılaştırın. Sağlık, kimlik
   doğrulama ve API adresini doğrulayın; eski API ile yeni UI'yi karıştırmayın.
2. **CSS/JS:** Network'te `secureops-theme.css?v=...` ve
   `workspace-guide.js?v=...` isteğini kaydedin. Beklenen sürüm/hash değerleri
   `release-metadata.json/versionedAssets` içindedir. CSS `text/css`, JS geçerli
   JavaScript MIME türü (`text/javascript` veya `application/javascript`) dönmeli;
   HTML giriş/hata sayfası olmamalı. 200 veya geçerli 304 normaldir; hash/MIME
   farkı, 404 veya konsol hatasında durun. Sır/oturum başlığı içeren HAR paylaşmayın.
3. **Gerçek yerleşim:** masaüstü genişliğini ve zoom'u kaydedin; filtre toolbar'ının
   computed `display:grid` değerini, kart/liste yapısını ve etiketleri ekran
   görüntüsüyle doğrulayın. Mobil, açık/koyu tema ve gerçek %200 zoom'u kontrol
   edin. Canlı TEST sorununun nedeni hâlâ doğrulanmadı; cache temizliğini tek başına
   çözüm saymayın. Paket/served dosya/computed-style kanıtını önce karşılaştırın.
4. **Görünür sayfa seçimi:** tümünü seç yalnız mevcut sayfayı seçmeli; filtre/sayfa
   değişiminde temizlenmeli. Onaylı kişisel UAT'ta `Gruba ekle` doğru sayıda öğeyi
   tek versioned işlemle eklemeli, gizli üyelik korunmalı. `Bağlantıları aç` önce
   güncel yetkili bağlantıları çözmeli; ikinci native kullanıcı eylemi açmalı.
   Tekil bağlantı/popup yardımı görünür kalmalı; tüm sekmeler veya hedef oturumu
   başarılı varsayılmamalı. Hedef ziyaretleri ayrı erişim onayına bağlıdır.
5. **Kişisel düzen:** görünüm, yoğunluk, sayfa boyutu ve izinli kısayol sırasını
   kaydedip yeniden girişte doğrulayın. `Reset layout` karşılığı düzen sıfırlama
   varsayılanları geri getirmeli; grup/favorileri silmemeli. Yetki değişimi gizli
   kısayolları açmamalı; çatışmada otomatik yazma tekrarı olmamalı.
6. **Operasyonel liste:** ilk açılış/arama/sıralama/sayfalama
   `GET /api/v1/operational-records/stored` kullanmalı; toplam yalnız kayıtlı
   eşleşmelerdir. Turuncu Hat toplamı değildir. Yalnız `Kaynağı yenile` kaynak
   sorgusudur; ayrıca kurumsal smoke onayı yoksa bu eylemi çalıştırmayın.
7. **Rehber/yetki:** bağlantı, grup, düzen ve talep rehberlerinde hedef konumu,
   ileri/geri/atla/Escape, resize ve kapanış odağını doğrulayın. Rehber gezinmesi
   kayıt oluşturmamalı veya tercihi değiştirmemeli. Ordinary/curator/manager ve
   denied menü/doğrudan erişimini kontrol edin. Talep tipi onay değildir;
   kapsam dışı sunucu emekliliği zorla sınıflanmamalı. Publisher yazma kapalı
   açıklamasını görmeli; Jira create/retry veya kaynak/BPM close yapılmamalı.

Yerel Release/publish/paket kapıları ve 9 OpenAPI/Swagger testi yeniden geçti.
1092 unit, 250 integration (gerçek izole SQL dahil) ve yayımlanmış UI tarayıcı
kanıtları değişmeyen runtime için yeniden kullanıldı; SQL/tarayıcı yeniden
çalıştırılmadı. Gerçek TEST asset/MIME, native %200 zoom, ekran okuyucu ve kurumsal
popup/kimlik davranışı için yeni doğrulama iddiası yoktur.

### Korunacak Sınırlar ve Grants

Server-owned `OperationalRecords` bölümünde:

```text
ReadOnlyIntegrationMode=true
ControlledTestWritesEnabled=false
SourceCloseEnabled=false
```

Jira-only runtime desteği Jira yazısını açmaz. Sunucu Talebi ve Uygulama Kurulumu
seçimi operatör beyanıdır, pozitif uygunluk/onay değildir. Uygulama kurulumu
eşlemesi ve kurumsal politika/kimlik/yetki/mutabakat kanıtları beklenmektedir.
`JiraCreated`, kaynak tamamlandı demek değildir; kaynak açık gösterilir. Kapatma
simüle edilmez, belirsiz Jira sonucunda otomatik create/retry yapılmaz.

Mevcut runtime grant sözleşmesi (rc6.11'e göre yeni izin yok):

| Nesne | Runtime izni |
|---|---|
| `audit.AuditLog`, `security.AccessRequestHistory`, `ops.OperationalRecordWorkflowHistory` | INSERT |
| `security.Users`, `security.RoleAssignments`, `security.AccessRequests`, `security.ApplicationSessions` | SELECT, INSERT, UPDATE |
| `security.Roles` | SELECT |
| `ops.OperationalRecords`, `ops.JiraTransfers`, `ops.CommandExecutions` | SELECT, INSERT, UPDATE |
| `resources.Categories`, `resources.Links`, `resources.PersonalPreferences` | SELECT, INSERT, UPDATE |
| `reporting.ManagementAuditEvents`, `reporting.ManagementWorkflowEvents`, `reporting.ManagementOperationalStatus` | SELECT |

Veritabanı CONNECT ve mevcut ownership-chain DBA tarafından doğrulanır. Runtime'a
DELETE, DDL, schema ownership, `db_owner`, `db_ddladmin`, doğrudan audit/history
SELECT veya kullanılmayan `ManagementSessionStatus` SELECT verilmez. Tarihsel
grant-only dosyası resources izinlerini içermez; tek başına tam güncel grant seti
değildir. Gerçek principal varsayılmaz, geniş rol atanmaz.

### 011 Etkisi ve Geri Dönüş

`sql/migrations/011-independent-source-close.sql`, tek transaction içinde
`ops.JiraTransfers` ve `ops.OperationalRecordWorkflowHistory` tablolarına
`SourceCloseRequested bit NOT NULL DEFAULT (0) WITH VALUES` ekler. Kolon-varlık
guard'ı uyuşmayan mevcut tanımı onarmaz. Eski satırlar kapatma niyeti kazanmaz;
daha sonra gate açılması false niyeti yükseltmez. Mevcut migration'lar değişmedi.

Yerel 001-011 kurulum, legacy transfer/history upgrade ve tekrarlı 011 kanıtı
önceki handoff'tan kullanılır; gerçek SQL restart/concurrency/audit testleri
deterministik dış servislerle çalışmıştır. Bu, kurumsal şema/grant doğrulaması
veya eski binary rollback uyumluluğu değildir.

Binary rollback yalnız doğrulanmış önceki API/UI ve config yedeklerine dönüştür;
011 kolonları veya append-only kanıtlar silinmez. Eski binary'nin 011 ile
uyumluluğu ayrıca kanıtlanmadan rollback güvenli ilan edilmez. Uyumluluk belirsizse
DBA ve operatör onaylı kurtarma planında durun. SQL restore ayrı DBA kararıdır;
yedekten sonra oluşan işlem/audit verisini kaybettirebilir. Binary rollback dış
sistemde oluşmuş Jira'yı geri almaz; transfer silerek yeniden create yapılmaz.

## Historical Reference Only

The sections below preserve earlier deployment records and contracts. In
particular rc6.10/rc6.11 and 001-010 (or older) sequences are NOT installation
instructions for rc6.13. The unchanged rc6.12 DBA ZIP also embeds a historical
runbook; use only the current root export and sequence above. rc6.11 retains
source `74cd8274250302a977cbc4c5cd6e4f1789c01459`; previous packages/hashes are
unchanged and do not contain the independent close gate or migration 011.

## Historical SDM/Resources TEST Operator Runbook, rc6.10

Bu teslimat yerel doğrulama ve paket hazırlığıdır; TEST kurulumu veya SDM yazma
aktivasyonu değildir. Paket kökü mevcut standarda göre
`C:\SecureOpsBuild\release\2026-09-07-pilot-rc6.10` olur. Kesin kaynak SHA ve
arşiv/per-file SHA-256 değerleri bu dizinin `release-readiness.md` dosyasındadır.
Sunucu adları, kurumsal mevcut şema ve rollback sürümü doğrulanmış değildir.

| Adım / makine | Tür | Önkoşul, beklenen sonuç ve durma koşulu |
|---|---|---|
| 1. Yetkili TEST operatörü, kendi yönetim istasyonu | Salt okunur | Değişiklik kaydına gerçek API/UI hedeflerini, mevcut binary/config sürümlerini, DBA tarafından doğrulanmış şema seviyesini ve geri dönüş paketini kaydedin. Bilgi veya ayrı kurulum onayı eksikse durun. Bu ilk manuel TEST adımıdır. |
| 2. Build istasyonu / paket teslim alan istasyon | Salt okunur | `Get-FileHash -Algorithm SHA256 -LiteralPath <arşiv>` sonuçlarını `release-artifacts.sha256` ile karşılaştırın. API/UI aynı build kaynak SHA'sını taşımalı; eksik manifest veya hash farkında durun. Paketleri eski dizinlerin üzerine yazmayın. |
| 3. TEST operatörü ve DBA | Değiştirici, ayrı onaylı hazırlık | Mevcut API/UI binary ve server-owned config yedeklerinin yolunu, SHA'sını, tarihini ve geri dönüş sürümünü değişiklik kaydına yazın. DBA geri yüklenebilir SQL yedeğinin referansını ve geri yükleme sorumlusunu doğrulasın. Gizli config/değerler teslimat paketine veya bu belgeye konmaz. Doğrulanmış geri dönüş tabanı yoksa durun. |
| 4. DBA, doğrulanmış TEST SQL hedefi | Değiştirici, ayrı DBA onayı | Gerekli seviye 001-010'dur. Önceden uygulanmış migration dosyaları değiştirilmez ve tüm zincir körlemesine yeniden çalıştırılmaz. Onaylı mevcut seviyeden yalnızca eksik migration'ları sıra ile uygulayın. 009 değerlendirme kanıtı, 010 kaynak kataloğudur. 001/002 idempotent değildir; kayıtlı migration-history tablosu yoktur. Şema belirsizse durun. |
| 5. DBA, aynı SQL hedefi | Değiştirici grants; ardından salt okunur kontrol | DDL/migration kimliği ile runtime kimliğini ayırın. Mevcut nesne grant listesi aşağıdaki Database Contract bölümündedir. Ek olarak runtime için `resources.Categories`, `resources.Links`, `resources.PersonalPreferences` üzerinde SELECT/INSERT/UPDATE ve mevcut `audit.AuditLog` üzerinde INSERT gerekir. DELETE, DDL, db_owner verilmez. Gerçek runtime principal ve ownership-chain doğrulanmadan durun. |
| 6. TEST API operatörü, doğrulanmış API makinesi | Değiştirici, ayrı kurulum onayı | Şema ve grants tamamlandıktan sonra eşleşen API paketini mevcut dağıtım yöntemiyle kurun. `web.config`, `appsettings*.json`, Data Protection dizini, loglar ve sunucuya ait sırlar korunur. ReadOnlyIntegrationMode=true, ControlledTestWritesEnabled=false kalır. Jira/source/BPM yazıları açılmaz. Sağlık veya sürüm kontrolü başarısızsa UI adımına geçmeyin. |
| 7. TEST UI operatörü, doğrulanmış UI makinesi | Değiştirici, ayrı kurulum onayı | Aynı kaynak SHA'lı UI paketini kurun; server-owned API adresini, auth/offload ayarlarını ve UI key-ring yolunu koruyun. LB/IIS/binding ayarlarını bu teslimatla değiştirmeyin. Uyumlu API veya kalıcı UI key ring doğrulanamıyorsa durun. |
| 8. Yetkili kullanıcılar, kurumsal TEST tarayıcısı | Salt okunur doğrulama; yerel uygulama kayıtları için ayrıca onaylı UAT | Admin sağlık/sürüm bilgisi; ordinary kullanıcının bağlantı arama/grupları; curator yetkisiyle kontrollü katalog taslağı; manager tarih/boş/hata ekranı ve yetkisiz doğrudan erişim kontrolü. Publisher mevcut gerçek kayıtlarda SDM engellerini ve yazma kapalı açıklamasını doğrular, create/retry çalıştırmaz. Kaynak yenileme/AD/Jira çözümleme çağrıları kurumsal erişim olduğundan ayrı smoke onayı gerektirir. Beklenmeyen gerçek yazma, sentetik kaydın corporate listede görünmesi veya yanlış yetkide durun. |

**Geri dönüş:** binary rollback, kayıtlı önceki API/UI paket ve kontrollü
konfigürasyonlarının geri yüklenmesidir. Additive 009/010 tabloları/kolonları ve
append-only kanıtlar silinmez; eski binary'nin uyumu önceden doğrulanır. SQL
yedeğinden geri yükleme ayrı DBA kararıdır; yeni işlem/audit verisini kaybettirebilir.
Binary rollback, dış sistemde oluşmuş Jira kaydını geri almaz. Belirsiz Jira
sonucunda yeniden create, transfer satırı silme veya kanıtsız SQL düzeltmesi yapılmaz.

**Aktivasyon engelleri:** özgün betik 2026-09-07 tarihinde kaynak olarak incelendi;
keşif engeli kapandı (parity belgesinde hash ve sözdizimi kusuru kayıtlı).
Pozitif SDM kategori politikası, kayıt bazlı kapsam/altyapı kanıtı, requester/reporter
eşleme ve yetki sözleşmesi, operatör teyidinin yeterliliği veya ayrı onay kararı,
Jira create hata/başarı ve mutabakat
arama/idempotency sözleşmesi gerekir. Turuncu Hat update/BPM close sözleşmesi ve
onayı ayrıca gereklidir. Mevcut servis hesabı bunların yerine geçmez.

This is the general controlled deployment contract. The authoritative 2026-08-23 TEST/Pilot release-candidate manifests are under `docs/release-candidates/2026-08-23-api-test-pilot-rc/`. The application never executes SQL or edits IIS configuration. Server-owned `web.config` and `appsettings*.json` files are excluded from the deployment ZIP.

## Migration Review

Run the SQLCMD-mode entrypoints in exact order through the approved DBA process:

| Order | Entrypoint | Creates or changes | Re-runnable |
|---|---|---|---|
| 1 | `sql/migrations/001-audit-and-access-control.sql` | `audit` and `security` schemas; audit, user, role, assignment, request, and request-history objects | No. All `CREATE` statements and role seed inserts are unconditional. |
| 2 | `sql/migrations/002-operational-record-jira-workflow.sql` | `ops` schema; Operational Record, Jira transfer, and workflow-history objects | No. Only schema creation is guarded. |
| 3 | `sql/migrations/003-platform-access-concurrency-hardening.sql` | access status/authentication columns, two roles, source freshness/claim fields, command execution state | Partially. Columns and the command table are guarded; fixed role IDs, constraint-name checks, and prerequisite tables can still fail. |
| 4 | `sql/migrations/004-access-read-model-and-versioning.sql` | explicit access-user and access-request mutation versions | Yes for column presence; prerequisite access tables must exist. |
| 5 | `sql/migrations/005-management-reporting-read-model.sql` | limited reporting views and supporting indexes | Yes for schema, views, and index presence; prerequisite audit and ops objects must exist. |
| 6 | `sql/migrations/006-operational-record-source-created-at-nullable.sql` | preserves unavailable source-created time as nullable | Yes when the prerequisite Operational Record table exists. |
| 7 | `sql/migrations/007-application-session-governance.sql` | authoritative application-session table, indexes, and limited reporting view | Yes for object presence/replacement; prerequisite access and reporting schemas must exist. |
| 8 | `sql/migrations/008-oidc-user-profile.sql` | nullable bounded OIDC login name, display name, mail, uid, and profile-update timestamp on `security.Users` | Yes for column presence; prerequisite `security.Users` must exist. |
| 9 | `sql/migrations/009-sdm-evaluation-foundation.sql` | nullable bounded SDM evidence on operational records/history and validation constraints | Guarded column/constraint creation; requires 001-008. DBA execution before SDM binary upgrade. |
| 10 | `sql/migrations/010-resource-catalogue.sql` | resources.Categories, Links, PersonalPreferences and unassigned ResourceCurator role | Guarded object/role creation; requires 001-009; collision fails closed. |

The `:r` directives require SQLCMD mode and resolve files under `sql/schema`. Migrations 003-008 require successful prerequisites. None assumes empty tables, but 001 and 002 require the target object names to be absent. Existing rows are supported by defaults in 003 and 004; migration 008 adds nullable columns and does not invent profile values for existing users. Migrations 005 and 007 can add indexes and must be scheduled and reviewed by the DBA.

There are no down migrations, migration-history table, encompassing transaction, or automatic rollback. A failure after a `GO` can leave a partially applied database. Before execution, the DBA must inventory schemas, tables, indexes, triggers, constraints, and seeded `RoleId`/`RoleCode` values, take an approved backup or recovery point, and stop on any collision. Do not re-run a failed batch without a DBA-authored corrective plan.

## Database Contract

Resource v1 extends the runtime grant set with SELECT, INSERT, UPDATE on
`resources.Categories`, `resources.Links`, and `resources.PersonalPreferences`.
Shared and personal changes INSERT audit evidence in the same transaction.
No direct audit SELECT, history modification, DELETE, schema ownership or DDL is
required. ResourceCurator is a role definition only; assignment remains an
existing Admin's versioned access operation. The provider follows Access storage.

- `audit.AuditLog`; indexes `IX_AuditLog_OccurredAt`, `IX_AuditLog_CorrelationId`; append-only trigger.
- `security.Users`, `Roles`, `RoleAssignments`, `AccessRequests`, `AccessRequestHistory`; bounded nullable OIDC profile metadata; active-role and pending-request unique indexes; status checks; no-self-approval and append-only triggers.
- `ops.OperationalRecords`, `JiraTransfers`, `OperationalRecordWorkflowHistory`, `CommandExecutions`; source/Jira/idempotency uniqueness; rowversion, source token, validation time, actor lease fields, status/claim checks, and workflow-history append-only trigger.
- `reporting.ManagementAuditEvents`, `ManagementWorkflowEvents`, and `ManagementOperationalStatus`; limited read views plus reporting indexes on underlying tables.
- `security.ApplicationSessions`; authoritative lifecycle timestamps/reasons, authentication method, access version, and active-session indexes. `reporting.ManagementSessionStatus` exposes limited aggregate fields.

The DBA migration identity needs controlled DDL authority to create schemas/tables/views/indexes/triggers/constraints and DML authority for role seeds. The runtime identity needs only: `INSERT` on `audit.AuditLog`; `SELECT, INSERT, UPDATE` on `security.Users`, `security.RoleAssignments`, `security.AccessRequests`, and `security.ApplicationSessions`; `SELECT` on `security.Roles`; `INSERT` on `security.AccessRequestHistory`; `SELECT, INSERT, UPDATE` on `ops.OperationalRecords`, `ops.JiraTransfers`, and `ops.CommandExecutions`; `INSERT` on `ops.OperationalRecordWorkflowHistory`; and `SELECT` on the three reporting views read by current code. It needs no direct `SELECT` on base audit/history tables, unused `reporting.ManagementSessionStatus`, `DELETE`, DDL, schema ownership, `db_owner`, or `db_ddladmin`. Exact grants are in the release-candidate grant-only script. View/trigger ownership chaining must be verified by the DBA.

## Bootstrap Administrator

Real first-Admin bootstrap is OIDC-only and disabled by default. The eligible OIDC principal is persisted under the existing opaque SHA256 identity derived from exact issuer plus subject; the configured login name is lookup evidence only. The legacy `Access:BootstrapAdministrators` setting is rejected at startup.

On first authenticated access, an unknown principal becomes `Pending` and receives one access request. With the SQL-only bootstrap gate enabled, only the exact configured OIDC issuer and login name can attempt the first Admin grant. SQL serializes the check, treats every historical Admin assignment including revoked rows as permanent closure, and commits approval, assignment, and append-only audit evidence atomically. Demo/Test compatibility separately maps only `demo:platform-admin` to Admin and `demo:team-lead` to Lead when both Demo flags are enabled; it must be disabled for real bootstrap.

An empty database with the OIDC bootstrap disabled is locked out until an existing Admin approves access. For a controlled first OIDC TEST login, configure the three `BootstrapAdmin` keys, keep `Access__AutoCreateRequest=true`, use SQL access/session/audit providers, and disable Demo compatibility. Disable the bootstrap setting after success as operational cleanup; assignment history is the permanent security boundary. Revoking or disabling the only Admin never reopens bootstrap.

Controlled TEST procedure: first have the DBA confirm that no historical Admin assignment exists, then configure only the approved runtime placeholders, keep both Demo switches false, enable OIDC and the bootstrap gate in the same controlled change, and let the designated user complete normal OIDC authentication. Verify the persisted Admin role through the normal access endpoint and verify `FirstAdminBootstrapped`, `AccessApproved`, and `RoleAssigned` audit evidence. Finally set `BootstrapAdmin__Enabled=false` and restart in a separate controlled change. If historical assignment evidence exists or any step fails, stop; do not delete history or substitute a Demo identity.

## Exact TEST Environment Variables

Values in angle brackets require controlled deployment input. All booleans are lower-case strings in IIS environment variables.

| Classification | Environment variable | TEST value |
|---|---|---|
| REQUIRED | `ASPNETCORE_ENVIRONMENT` | `Test` |
| REQUIRED, TEST-ONLY | `Swagger__Enabled` | `true` |
| REQUIRED, TEST-ONLY | `DemoAuth__Enabled` | `false` for real OIDC bootstrap; `true` only for separate synthetic Demo validation |
| REQUIRED, TEST-ONLY | `DemoAuth__HeaderName` | `X-SecureOps-Demo-Actor` |
| REQUIRED | `Access__RepositoryProvider` | `SqlServer` after migrations 001-008 |
| REQUIRED | `Access__AutoCreateRequest` | `true` |
| REQUIRED, TEST-ONLY | `Access__DemoCompatibilityEnabled` | same enablement decision as `DemoAuth__Enabled` |
| CONDITIONAL REQUIRED | `BootstrapAdmin__Enabled` | `true` only during the controlled first OIDC Admin login; default and post-bootstrap value is `false` |
| CONDITIONAL REQUIRED, RUNTIME-ONLY | `BootstrapAdmin__LoginName` | `<EXACT_APPROVED_OIDC_LOGIN_NAME>`; never commit a real identity |
| CONDITIONAL REQUIRED | `BootstrapAdmin__AllowedIssuer` | `<EXACT_APPROVED_HTTPS_OIDC_ISSUER>`; must exactly equal `Oidc__Authority` |
| ACTIVATION-PENDING | `Oidc__Enabled` | keep `false` until the approved IdP contract and deployment change are complete |
| REQUIRED | `SessionSecurity__IdleTimeoutMinutes` / `SessionSecurity__AbsoluteLifetimeHours` / `SessionSecurity__ActivityPersistenceIntervalMinutes` | `30` / `12` / `5` |
| REQUIRED | `SessionSecurity__RepositoryProvider` / `SessionSecurity__CookieName` / `SessionSecurity__MaxAdminPageSize` | `SqlServer` / `__Host-SecureOps.ApplicationSession` / `100` |
| REQUIRED | `SessionSecurity__SecureCookie` / `SessionSecurity__HttpOnly` / `SessionSecurity__SameSite` / `SessionSecurity__RevalidateAccessOnEveryRequest` | `true` / `true` / `Lax` / `true` |
| REQUIRED | `DataProtection__Mode` / `DataProtection__ApplicationName` | `FileSystemDpapi` / `SecureOps.Api` |
| REQUIRED | `DataProtection__KeyRingPath` | `<absolute server-owned key-ring directory outside deployment payload>` |
| REQUIRED | `CommandIdempotency__ExecutionLeaseSeconds` / `CommandIdempotency__MaxKeyLength` | `120` / `128` |
| REQUIRED | `RateLimiting__IdentityLookup__PermitLimit` / `RateLimiting__IdentityLookup__WindowSeconds` | `10` / `60` |
| REQUIRED | `RateLimiting__BulkIdentityLookup__PermitLimit` / `RateLimiting__BulkIdentityLookup__WindowSeconds` | `4` / `60` |
| REQUIRED | `RateLimiting__OperationalRecordRefresh__PermitLimit` / `RateLimiting__OperationalRecordRefresh__WindowSeconds` | `12` / `60` |
| REQUIRED | `RateLimiting__JiraPreview__PermitLimit` / `RateLimiting__JiraPreview__WindowSeconds` | `20` / `60` |
| REQUIRED | `RateLimiting__JiraCreate__PermitLimit` / `RateLimiting__JiraCreate__WindowSeconds` | `6` / `60` |
| REQUIRED | `RateLimiting__WorkflowRetry__PermitLimit` / `RateLimiting__WorkflowRetry__WindowSeconds` | `6` / `60` |
| REQUIRED | `IdentityLookup__Provider` / `IdentityLookup__DomainName` | `ActiveDirectory` / `<approved AD DNS domain>` |
| OPTIONAL | `IdentityLookup__Container` | `<approved container DN>` or omit |
| REQUIRED | `IdentityLookup__StripDomainPrefix` / `IdentityLookup__NormalizeToLowerInvariant` / `IdentityLookup__EnableUpnLookup` | `true` / `true` / `true` |
| REQUIRED | `IdentityLookup__MaxAccountLength` / `IdentityLookup__AllowedAccountPattern` / `IdentityLookup__RegexTimeoutMilliseconds` | `128` / `^[a-zA-Z0-9._@-]+$` / `250` |
| REQUIRED | `IdentityLookup__ProviderTimeoutSeconds` / `IdentityLookup__BulkMaxAccounts` | `3` / `20` |
| REQUIRED | `IdentityLookup__Cache__Enabled` / `IdentityLookup__Cache__TtlSeconds` / `IdentityLookup__Cache__MaxEntries` | `true` / `30` / `500` |
| REQUIRED, TEST-ONLY | `PamProvider__Provider` / `PamProvider__TimeoutSeconds` | `Mock` / `3` |
| REQUIRED | `OperationalRecords__SourceProvider` | paired `Simulation` for operator-visible deterministic TEST, legacy `Fake` for automated compatibility, `Disabled` for fail-closed runtime, or contract-gated `TuruncuHat` only after separate approval |
| REQUIRED | `OperationalRecords__RepositoryProvider` / `OperationalRecords__MaxImportCount` / `OperationalRecords__ClaimLeaseSeconds` | `SqlServer` / `100` / `120` |
| REQUIRED FOR REAL-DATA READ-ONLY TEST | `OperationalRecords__ReadOnlyIntegrationMode` | `true`; requires `TuruncuHat` + `Corporate` and blocks all external writes |
| REQUIRED | `Jira__Provider` | `Disabled`, paired `Simulation` for operator-visible synthetic TEST, legacy `Fake` for automated compatibility, or contract-gated `Corporate` only after separate approval |
| REQUIRED | `Jira__ProjectKey` / `Jira__IssueType` / `Jira__MappingVersion` | `<approved TEST project key>` / `<approved issue type>` / `<reviewed mapping version>` |
| REQUIRED | `Jira__UnresolvedRequesterPolicy` / `Jira__SummaryMaxLength` | `Block` / `255` |

| REQUIRED | `Audit__Provider` / `Audit__FailClosed` / `Audit__RequirePersistentStoreInProduction` | `SqlServer` / `true` / `true` |
| REQUIRED | `Audit__Queue__Enabled` / `Audit__Queue__Capacity` / `Audit__Queue__FullBehavior` / `Audit__FlushIntervalSeconds` | `true` / `1000` / `FailClosed` / `1` |
| REQUIRED, RUNTIME-ONLY | `ConnectionStrings__SecureOpsDb` | `Server=tcp:<SQL_FQDN>,<SQL_PORT>;Database=<DATABASE_NAME>;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;Application Name=SecureOps.Api;Connect Timeout=15` |
| REQUIRED CURRENT | `ReverseProxy__ForwardedHeaders__Enabled` | `false` until exact API proxy behavior and source IPs are confirmed |
| CONDITIONAL | `ReverseProxy__ForwardedHeaders__TrustedProxyIps__0` | `<exact trusted API proxy IP>` only when forwarding is explicitly enabled |

When any SQL provider is selected, startup requires Integrated Security without SQL login credentials and rejects malformed connection strings, missing server/database values, and `Connect Timeout` values outside 1-60 seconds. `GET /api/v1/health` remains process liveness; authenticated `GET /api/v1/health/persistence` performs a bounded read-only probe and returns only `NotConfigured`, `Healthy`, or `Unhealthy` plus a stable error code. Both paths bypass SQL-backed application-session creation after host authentication so SQL failure cannot hide process liveness or its own readiness result.

### OIDC Activation-Pending Values

Keep `Oidc__Enabled=false` until the corporate contract is approved. At activation, the UI host requires `Oidc__Authority`, `Oidc__MetadataAddress`, `Oidc__ClientId`, `Oidc__ClientAuthenticationMethod` (`None` or `ClientSecretPost`), conditional `Oidc__ClientSecret`, `Oidc__TokenEndpointRequestFormat`, `Oidc__ApiAudience`, callback/signed-out callback paths, scopes including `openid`, `Oidc__RequireHttpsMetadata=true`, and explicit `Oidc__UsePkce`. `Oidc__EnableRemoteSignOut` remains `false` until operational testing approves logout parameters.

The authorization challenge uses a fresh 32-byte Base64URL nonce retained and validated by the ASP.NET Core protected nonce-cookie flow. IdentityModel client telemetry parameters are suppressed. With `Oidc__UsePkce=false`, the authorization request is limited to `response_type`, `client_id`, `scope`, `state`, `redirect_uri`, and `nonce`; enabling PKCE additionally emits the standard challenge parameters.

The API host requires only `Oidc__Authority`, `Oidc__MetadataAddress`, `Oidc__ApiAudience`, and `Oidc__RequireHttpsMetadata=true`; do not copy the UI client secret to the API. Optional claim-name and bound overrides use `Oidc__IssuerClaimType`, `SubjectClaimType`, `LoginNameClaimType`, `DisplayNameClaimType`, `MailClaimType`, `UidClaimType`, `RoleEvidenceClaimType`, `MaxClaimCount`, `MaxClaimValueLength`, and `MaxRoleEvidenceCount` under the same section. Defaults map `iss`, `sub`, `loginname`, `displayname`, `mail`, `uid`, and `uygulama-role`. Only claims in the validated OIDC principal can update the persisted profile; missing existing values are backfilled on the user's next successful OIDC authentication. Active Directory lookup is optional, uses the persisted login name, and falls back to persisted display name, login name, and mail when unavailable.

`Simulation` source records and `SIM-*` Jira keys are synthetic TEST evidence only. The provider has fixed scenarios, performs no network I/O, must be selected on both sides, and fails startup outside Development/Demo/Test. `Fake`/`FAKE-*` remains a legacy automated-test compatibility path.

The UI process has its own server-owned Data Protection settings: `DataProtection__Mode=FileSystemDpapi`, `DataProtection__ApplicationName=SecureOps.Ui`, and `DataProtection__KeyRingPath=<absolute server-owned UI key-ring directory outside deployment>`. Keep API and UI rings separate and grant each App Pool identity read/write/create access only to its own ring.

Do not configure SQL usernames/passwords. The Integrated Security identity is `DOMAIN\\WASAST_YONETIM`. File-audit keys are obsolete when SQL audit is selected. `IdentityLookup__RateLimit__*` is obsolete; the active keys are under `RateLimiting__*`.

## Current Web.Config Delta

Only three older values and the working AD state are confirmed; all other existing values must be inventoried during the controlled change review.

| Action | Existing | New |
|---|---|---|
| CHANGE | `ASPNETCORE_ENVIRONMENT=Demo` | `Test` |
| PRESERVE for current TEST | `DemoAuth__Enabled=true` | `true`, plus `Access__DemoCompatibilityEnabled=true` |
| CHANGE for real-user pilot | Demo compatibility values | `DemoAuth__Enabled=false` and `Access__DemoCompatibilityEnabled=false` |
| CHANGE after DBA migration | `Audit__Provider=InMemory` | `SqlServer` plus Integrated Security connection string |
| PRESERVE | `IdentityLookup__Provider=ActiveDirectory` and externally configured domain | Keep exact approved values |
| ADD | No confirmed access/ops SQL settings | Add all REQUIRED keys above |
| ADD | No confirmed Swagger flag | `Swagger__Enabled=true` |
| PRESERVE | Server-owned process/hosting settings and IIS authentication controls | Do not replace `web.config` from the ZIP |
| REMOVE if present | `IdentityLookup__RateLimit__*`, `Audit__File__*`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Obsolete or unsafe for this profile |
| PRESERVE disabled | API forwarded-header processing | Do not enable until exact trusted API proxy IPs and emitted headers are confirmed |

## Deployment and Rollback

Exact order: verify ZIP SHA256 and payload manifest; take database recovery point; DBA preflight and run 001, 002, 003, 004, 005, 006, 007, 008; verify objects/seeds/triggers/views/columns; grant runtime permissions; provision and ACL the server-owned Data Protection key ring; preserve server `web.config` and `appsettings*.json`; back up current application payload; apply reviewed IIS environment-variable delta; replace application payload without flattening directories; start/recycle only in the approved window; run the read-only smoke script.

Application rollback restores the prior binaries and prior server configuration while leaving additive database objects in place. Database rollback has no scripted path: stop deployment and use the DBA-approved restore/corrective-migration process. Never drop audit or history data as an application rollback step.

## Deployed TEST Evidence Record

An authorized operator completed the API deployment and real Turuncu Hat read-only smoke test for source `0ec037632e44c84f65c813c611486b1f4cc67f56`. The evidence below is sanitized and records counts and state only. It contains no corporate record content, personal information, credentials, authorization values, sessions, or internal secret values.

| Evidence | Operator-recorded value |
|---|---|
| API source SHA | `0ec037632e44c84f65c813c611486b1f4cc67f56` |
| UI source SHA | Not applicable; no UI deployment was required |
| API package | Fresh API-only, framework-dependent .NET 8 package produced and validated |
| API package SHA256 | Exact deployed archive association requires operator confirmation |
| UI package SHA256 | Not applicable; no UI package was deployed for this change |
| Deployed timestamp (UTC) | Not supplied; evidence recorded on 2026-09-04 |
| Server / environment | Authorized TEST IIS environment; server identity intentionally omitted |
| Server-owned configuration baseline ID | Not supplied; verified security values are recorded below |
| Rollback package / baseline | Not supplied |
| Read-only smoke-test result and evidence reference | PASS; sanitized result recorded in this section |

### Turuncu Hat Read-Only Smoke Result

- Phase 1 - Real Turuncu Hat read-only import: **COMPLETED AND VERIFIED IN TEST**.
- The deployed API queried the real Turuncu Hat read-only source and received four records.
- Each observed corporate row contained seven direct `Key`/`Value` cells.
- The API logged `Turuncu Hat source query completed. Records: 4. MalformedOrAmbiguous: 0.`
- The UI displayed four real Operational Records. No UI deployment was required for this verification.
- All four records remained `NeedsManualReview`; `JiraEligible` remained `false`, and the Jira-transferable counter remained zero.
- Synthetic records were not displayed while the corporate source provider was active.
- The UI continued to identify real data as active and external writes as disabled.
- `OperationalRecords__ReadOnlyIntegrationMode=true` and `OperationalRecords__ControlledTestWritesEnabled=false` remained the TEST security state.
- No Jira create, Turuncu Hat update, or BPM close was performed.
- No SQL migration was required.

### Release Directory Association

Local release metadata under `C:\SecureOpsBuild\release\<release-name>` was inspected. No release metadata or artifact name in that tree associates source `0ec0376` with one exact deployed release directory. The source SHA and TEST smoke result are verified, but the deployed release-directory and archive-hash association requires operator confirmation. A rejected RID-specific packaging attempt is not deployable evidence and is excluded from this record.

### Next Development Milestone

The deterministic SDM evaluation foundation is implemented in source under
ADR-0018, separately from the deployed smoke evidence above. Migration 009 has
not been applied by this task. Next: Action Center integration with the additive
contract, followed by approved structured source/category policy and a separate
human-approval milestone. External writes remain disabled; no new deployment or
release package is implied.

## Resource Catalogue Backend V1: Local Task Evidence

Recorded 2026-09-06, starting source
`c18d196ba14905df92e57fe231c2e31b95d113de`, branch
`feature/sql-runtime-hardening-20260902`. The starting tracked tree was clean;
only the pre-existing untracked `.vscode/` was present and is preserved.

The owner explicitly authorized this milestone's total diff to exceed the
1,000-line limit in `docs/agent-guides/090-testing-quality.md`. The permanent
rule is unchanged. This covers handwritten implementation, tests, documentation
and generated OpenAPI together, not separate artificial per-commit limits.
ADR-0019 records the decision; final total additions/deletions are reported from
the starting source through the maintenance-document commit.

Final milestone diff: +4,854/-13 lines; generated OpenAPI: +2,542/-1;
handwritten implementation/tests/SQL/docs: +2,312/-12. This includes the separate
historical-inventory preservation commit; no packages, UI or applied migrations
were changed. The original `.vscode/` remains outside both commits.

Implemented: shared categories/links, manager-only categories, bounded search and
pagination, explicit Admin/ResourceCurator management, owner-only favourites and
ordered named/default sets, current visibility resolution, version conflicts,
and SQL-transactional safe audit. No real user received a role. Claude's canonical
handoff is `docs/contracts/secureops-api-v1-ui-integration.md`, section
"Resource Catalogue and Shift Start Sets: Claude Handoff".

### Executed Verification

| Gate | Actual local result |
|---|---|
| Release solution build | PASS, 0 warnings, 0 errors |
| Full unit suite | PASS, 962 passed, 0 failed, 0 skipped |
| Full integration suite with isolated SQL enabled | PASS, 234 passed, 0 failed, 0 skipped; includes four actual SQL tests |
| OpenAPI | Regenerated through the existing in-process Swagger snapshot test process; snapshot equality passes in the full integration suite |
| Vulnerability scan including transitives, public nuget.org feed | PASS, no vulnerable packages reported across eight projects; no version changes |
| Repository-wide format verification | FAIL on existing unrelated whitespace/encoding/import/naming debt, including unchanged Identity and UI files; not bulk-fixed |
| New resource C# files | PASS, scoped `dotnet format --verify-no-changes --no-restore --include` over the explicit new C# file list |
| Diff whitespace | PASS, `git diff --check`; staged diff checked again before commit |
| Corporate SQL/AD/Turuncu Hat/Jira, IIS, browser targets | NOT RUN; not authorized or needed for this local milestone |

The full unit run initially exposed two expected baseline assertions (six-role
list and nine-migration count). Both were updated for ResourceCurator/010; the
totals above are the passing rerun. No UI implementation changed.

### Actual Isolated SQL Execution

An existing LocalDB installation was available. A new per-user test instance
`SecureOpsResourcesV1` and new test-prefixed databases were used, without changing
the existing default instance or any existing user database. The final fresh
database was `SecureOps_ResourcesV1_Complete`.

`scripts/powershell/Test-ResourceCatalogueSql.ps1 -DatabaseSuffix Complete -RunTests`
successfully applied 001-008, inserted synthetic predecessor user/Operational
Record rows, applied **unchanged 009 then new 010**, verified preserved manual
review/ineligibility and empty catalogue defaults, re-ran 010's guards, and ran
four SQL tests. They exercised catalogue/personal round trips, concurrent stale
write rejection, owner separation, archive/visibility filtering, transactional
audit failure rollback, append-only enforcement, constraints, and SDM evaluation
persistence/staleness/unchanged-input history idempotency. Offline SQL contract
assertions remain separate evidence, not substitutes for these executed tests.

Initial disposable attempts exposed SQLCMD's required `-I` option and a Windows
PowerShell connection-builder property issue; the harness was corrected and the
entire fresh upgrade succeeded. Earlier test databases are retained for inspection,
not deployed evidence. Only a new test-owned failure-injection trigger was
created/dropped; no existing append-only trigger was disabled or altered.

### Deployment Gates and Repeatable Checklist

The deployment order for this source supersedes the older 001-008 checklist:
verify actual installed schema and backup/recovery point, apply only missing
migrations **001 through 010 in order**, validate 009 SDM evidence and 010 objects,
assign reviewed runtime grants, then deploy separately authorized binaries.
No corporate migration, push, deployment or release packaging occurred here.

- To repeat locally, use the existing isolated instance and a **new** database
  suffix: `powershell -NoProfile -File scripts/powershell/Test-ResourceCatalogueSql.ps1 -DatabaseSuffix Review2 -RunTests`.
  Build Release first. The harness refuses existing database names and nonlocal
  destinations, installs no SQL service, and preserves databases for inspection.
- Corporate SQL execution and least-privileged integrated runtime grants remain
  **NOT RUN**. LocalDB owner-level success does not prove corporate permissions,
  deployment identity, collation/compatibility configuration or production load.
  Revalidate migration upgrade, constraints, concurrency and transactional audit
  under the authorized deployment identity before enabling SQL-backed use.
- Runtime needs SELECT/INSERT/UPDATE on the three resource tables and existing
  append-only audit INSERT permission. No resource DELETE, DDL, trigger override
  or real-user grant is required. Review role seed 7 for collisions.
- 010 is additive and seeds no catalogue data. Older binaries ignore its tables;
  rollback retains them and all audit evidence. No destructive down migration.
- The SDM source baseline remains undeployed/unrevalidated in TEST. Current real
  records remain manual review/ineligible; positive structured category policy
  and approval are pending. Preserve ReadOnlyIntegrationMode=true and
  ControlledTestWritesEnabled=false. No Jira create, source update or BPM close.
- Next UI milestone: implement the resource catalogue, manager forms, private
  favourites/set editor and explicit browser opening/fallback against the committed
  additive contract. No cookie/token forwarding or inferred target authentication.
