# In Use and OCO continuation after rc6.24

## Authorization and checkpoint

The owner explicitly approved the remaining In Use/OCO implementation, UI,
executor, contracts, tests, documentation and final matched delivery, including
a task-specific exception to the 1,000-line limit. This is not approval for live
source writes, SMTP, deployment, grants, corporate SQL or Windows Service hosting.
Continuation baseline: `8c1b58d0bfdbb363c451139cd212b08a83be7c86`, branch
`feature/combined-test-delivery-20260915`. Count all changes against that SHA,
including the four inherited pending documentation/browser files and new files;
commits do not reset accounting. Preserve the earlier aa9e4d2-based accounting.
rc6.24 has that build source and product version `0.1.0+8c1b58d0bfdbb363c451139cd212b08a83be7c86`.
Its archives remain immutable and were not deployed by this task. No task-owned
dotnet hosts were found on resumption; previous localhost links are not live evidence.

## Requirements to evidence

The references are the owner's post-rc6.22 and post-rc6.24 handoffs, the inspected
741-line script, and the preserved rc6.24 acceptance in
`release-candidates/2026-09-18-inuse-v2-rc6.24.md`. Local results below are inherited
unless an executed continuation result is explicitly recorded.

| Requirement / reference | Current implementation and package evidence | Remaining code/configuration/external fact | Local acceptance | Corporate acceptance |
|---|---|---|---|---|
| Questions, bulk, history/reuse / script 343,499-520 and owner 2A/2B | InUse.Review, InUseReviewHistory, SQL 022; rc6.24 two-OR journey | Preserve explicit provenance/context guards | Packaged reload/restart | Not rerun on target |
| Workbook values / script 348-635 | InUseWorkbook, 29 rows/22 columns, exact string IDs; accepted InUsePolicy | Personal owner, OS release, KONTROL evidence; actual XLSX not supplied | Values/bytes tested; no workbook-to-workbook claim | Pending |
| Archive/download / owner storage requirement | InUseReportArchive immutable JSON envelope; rc6.24 download/restart hash | Target effective path/ACL evidence | Packaged identical re-download | Target path not inspected |
| Attachment and completion / script 637-732 | Durable SQL intent/leases/Worker plus tested mutation serializer; fixture only | Real readback/conditional target contracts and adapter wiring still incomplete | Enabled fixture, failure/restart; not real contract proof | Not authorized/executed |
| OCO retrieval / mail_sscm and source contract | ConfigurationManager/TuruncuHat collectors, durable source jobs, selective apply | Target providers/profile/queue/offset contract acceptance | Packaged Worker recovery and source tests | Not established by heartbeat |
| Branded preview/MIME / owner OCO layout | oco-table-v3, six original images, immutable preparation | Outlook acceptance | Original-byte preview/CID and long service list passed | Outlook pending |
| Optional mail / owner OCO sending | AnnouncementMail service/store/Worker/MailKit, separate SelfTest/Send; continuation adds frozen dates/count/OCO to final confirmation | Target relay policy; new confirmation is not in rc6.24 | Enabled sink/browser and actual restart passed again | Relay/From/inbox unverified |
| Access guards / resolved owner report | Persisted capabilities/version, separate narrow send/complete rights | Preserve regressions; no SQL regrant | Existing SQL/access evidence | Owner reports approval fixed |
| Diagnostics/runbook | Operations diagnostics plus private collector; continuation adds bounded completion probe and safe mail comparison | Source response/semantics still needed; stale no-mail README corrected | Probe negative cases and live local comparison passed | Operator-only probe, not run here |

## Implementation boundaries

The script proves `p_emb_dynamic_case_orff` is requested, not which expanded
cell is the dynamic-case ID. A new read-only diagnostic uses that exact selector
and the exact eligible BPM filter from the script. It reuses the session/query
transport, emits aliased cell shapes only, checks an exact active 4241/68 root,
and never calls update/upload. It does not invent attachment or final-state
selectors. Missing selectors remain exact external prerequisites, not general
"upload fields unknown" claims.

Mail confirmation now exposes OCO, start/end including seconds/offset, preparation
time and distinct recipient count from the immutable preparation. These are
server-sourced output fields, never new editable send parameters. Existing
protected-token comparison, authorization and immutable MIME remain unchanged.
In Use acknowledgement parsing now rejects contradictory/nonzero/malformed
ErrorNo even when Success=true; unknown remains non-retryable automatically.

Effective diagnostics and the comparison helper also include mail Enabled,
SelfTestEnabled, SendEnabled and the existing nonsecret relay-policy fingerprint.
No relay address, credential, password or authentication username is returned.
Fingerprint equality is not a connectivity/TLS/credential/From-permission test.

## File locations (operator)

| Dosya / sonuç | Konum ve anlam |
|---|---|
| Eski script Excel'i | Scriptin çalıştığı bilgisayarda `C:\InUse\InUse_<OR>_<yyyyMMdd_HHmm>.xlsx` |
| WASAS kalıcı arşivi | API'nin etkin `InUseReports:Directory` dizininde `<WASAS kayıt GUID>/<sürüm>.json`; XLSX baytları, SHA-256, kişi, zaman ve sürüm içeren değişmez zarf. Ayrı XLSX dosyası beklenmez. |
| Arşivden indir | Yetkili uygulama işlemi aynı arşiv baytlarını verir; zarf elle düzenlenmez. Tarayıcının seçtiği indirme konumuna gider. |
| Turuncu Hat eki | Doğrulanmış `SMSS_oRFF` kimliğine `<OR>_InUse.xlsx`; yerel dizin değildir. Ek kimliği ve içerik doğrulaması ayrı kanıttır. |

Kurumsal etkin arşiv yolu bu çalışmada okunmadı; `C:\InUse` varsayılmıyor.
API IIS ortam ayarları JSON'u geçersiz kılabilir. Yetkili yönetici operasyon
tanılamasındaki etkin yolu kontrol eder. API hesabı yalnız özel dizinde mevcut
zarfları/lock dosyalarını okuyup yeni zarf, alt dizin, lock ve geçici dosya
oluşturabilmelidir; atomik yeniden adlandırma da gerekir. UI doğrudan erişmez.
Worker arşiv kopyası üretmez; SQL'deki dondurulmuş baytları kullanır.
Kurumsal ACL değişikliği bu talimatla yapılmaz.

## Release discipline

No new ZIP until independent implementation and acceptance are complete. A final
candidate must distinguish its build source from a later evidence-only closeout.
SQL 022 already belongs to rc6.24; installed 001-021/Hangfire 9 are not replayed.
No new migration is currently required by this continuation.

## Executed continuation evidence

Evidence root: `C:\SecureOpsBuild\validation\rc624-workflow-continuation-20260918`.
These runs used new synthetic LocalDB databases and loopback hosts only. The local
published payload is not a release ZIP and is not the immutable rc6.24 payload.

| Check | Executed result / evidence |
|---|---|
| Release build / full format | Zero warnings/errors; full format passed without a waiver |
| Normal regression / OpenAPI | 1,346 unit and 278 integration passed; 45 explicit opt-ins skipped in normal mode. `final-normal_net8.0_20260918164924.trx`, `final-normal_net8.0_20260918164934.trx`; generated snapshot then normal equality passed |
| Completion probe / acknowledgement | 31 focused unit checks passed, `inuse-focused.trx`: fences, exact identity, row/page bounds, masked shapes, ambiguous activities and contradictory acknowledgements |
| SQL mail / isolated SMTP | 7 passed, `mail-sink-sql.trx`: frozen preview, self-only target, duplicate claim, revocation, audit rollback, lease uncertainty and SMTP outcomes |
| Existing resource/In Use SQL | 31 passed, `resource-sql.trx`: history, executor/revocation/source drift, archive and concurrency regressions |
| Enabled browser mail | `mail-browser/result.json`: 83.912 seconds, four synthetic sink messages; self-test and send, cancel, duplicate confirmation, denied actor, uncertain DATA, original assets, long services and immutable MIME |
| Actual API/Worker restart | `mail-browser/restart-result.json`: 9.716 seconds; same four messages, identical immutable bytes, Unknown preserved, no second send |
| API/Worker composition | `comparison-result.json`: matching exit 0; altered report-copy mail flag exit 2. No runtime setting changed by the negative test |
| Standalone read-only collector | Build passed with zero warnings/errors; no corporate invocation |

Browser checks used both themes at 1440, 1366 and 390 CSS-pixel widths. The new
confirmation was inspected in `mail-browser/self-test-review.png` and
`mail-browser/distribution-review-mobile.png`: immutable OCO, nonzero seconds,
UTC+03:00 and actual recipient counts are visible. Width checks are reflow, not
a new claim of native 200% zoom or measured accessibility compliance. Prior
rc6.24 zoom/history/reuse evidence remains applicable to unchanged In Use code.
Outlook/VDI/assistive technology and corporate services were not exercised.
Overlapping focused runs are not added to normal-suite totals. The remaining
opt-ins were not all rerun; this is not final package acceptance.

The mail-only local host reports archive directory
`C:\SecureOpsBuild\validation\rc624-workflow-continuation-20260918\browser\private-reports`
with state `Missing`: this journey did not create In Use reports there. Earlier
rc6.24 archive/restart evidence is retained separately. Neither path proves the
corporate effective archive directory.

## Exact remaining source contracts

| Operation | Already known | Smallest missing fact / affected behavior |
|---|---|---|
| Correct OR and dynamic-case update | `SMSS_oRFF`, exact selected ID; `p_emb_dynamic_case_orff`; properties 4463/4464; serializer and isolated executor | Keyed representation of dynamic-case identity and supported version/conditional-update semantics. Needed before safe field effects and drift checks |
| Attachment upload and reconciliation | Secure upload route, `fBase`, `fId`, `fName`, `datastring`, `SessionID`, `TenantId`; frozen bytes/hash | Supported attachment listing/identity and content/hash readback, including read consistency. Needed to confirm the exact attachment and reconcile response loss without duplicate upload |
| Eligible activity and final OR observation | Exact script BPM filter/models/status/group/OR; unique selection required; update status 4 | Keyed activity projection plus conditional mutation/affected-target acknowledgement; authoritative OR state selector and closed-state codes. Activity acknowledgement alone cannot prove whole-OR closure |

Use the new [read-only collector instructions](../scripts/diagnostics/InUseEvidence/README.md)
for the known OR/activity projections. It deliberately cannot discover an unknown
attachment API or closed-state meaning. Supply only bounded sanitized response
shapes/API documentation, not a full configuration, HAR or secrets. The production
transport is still not registered for completion: local fixtures and the wire
serializer are not a real Turuncu Hat adapter. Finishing that adapter and its
contract tests remains code work after these facts are established; it is not
merely an operator flag change. Old blocked intents remain historical.

OCO source/preparation and optional mail are implemented and locally exercised;
target source/profile agreement, relay TLS/From policy and separately authorized
self-test/send remain corporate acceptance. SMTP acceptance never means inbox
delivery. No corporate write flag, grant, SQL migration, relay, deployment or
Windows Service was changed. Task-owned hosts/sink were stopped after ownership checks (`host-closeout.json`); rc6.24 ZIP hashes still match its release record.

## Change accounting

Fixed continuation baseline `8c1b58d0`: 26 files, +770/-15 (785 changed lines), including all inherited/new files and this record.
Whole post-rc6.22 baseline `aa9e4d2`: 82 files, +4008/-102 (4110 changed lines); the approved exception remains cumulative, not reset by commits.
