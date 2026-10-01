# rc6.21 Defect Checkpoint, 2026-09-17

## Verdict

Partial local repair, not a release or deployment approval. Fixed task baseline:
`778dca3bc1de6f854c60671a6d1f8dbbd69fbffd`, existing combined-delivery branch.
rc6.21 and its source `5166eaaf7fb43a9fcf58358845b3ef7ca3bfada7` are unchanged.
The reported TEST installation, migrations 001-021 and Worker registration are
accepted as operator evidence, not repeated or promoted to journey acceptance.
The owner subsequently approved a one-time exception for this complete defect
handoff, including the preserved 591-line checkpoint, implementation, tests and
matched release preparation. The fixed baseline is unchanged; commits do not reset
accounting. Corporate changes, external writes and remote push remain excluded.

## Evidence

### Root Causes and Disposition

| Report | Class | Inspected cause and bounded repair | Still not proved |
| --- | --- | --- | --- |
| Admin cannot remove another user's InUseCoordinator | Code / authorization identity | AccessController used Identity.Name; SQL administrative guards require persisted CorporateIdentity. Use the existing validated principal resolver. Real JWT + isolated SQL now removes the role, records canonical actor, removes its effective capability and rejects stale resubmission. | Correlation with the exact corporate request; new specific self/last-Admin explanations. |
| Source failure also says connection closed | Code | AnnouncementSourceReview conflated JobHostUnavailable with Disabled. Only the explicit Disabled code now says closed; other failures say availability could not be established. Specific Turkish source/configuration failure labels added. | Actual TEST failed endpoint/stage, effective API/Worker alignment, no-worker/wrong-queue diagnosis. |
| Original branding unavailable | Configuration / file access, exact TEST cause unknown | Existing original six files render locally; no renderer limit, bytes, filenames or remote dependencies changed. | Effective API bundle mapping, extraction location and reading identity ACL in TEST. |
| Archive/download fails | Code plus unresolved TEST storage cause | JS download exceptions no longer escape after archive success. Retained archive is downloadable again. Storage configuration, permission and integrity failures have safe codes and report-archive stage; audit failures remain separate. | Actual TEST InUseReports:Directory and failure stage. Screenshot alone does not establish ACL failure. |
| Reporter search misses records | Code | SQL previously searched only Code/Title. Parameterized EXISTS over persisted per-server JSON now searches hostname and observed reporter display/reference before count/paging. Explicit Turkish name/title and Latin account/hostname comparisons; no source call. | Corporate-scale query plan/load. Search does not turn a reporter into a reviewer or owner. |
| Raw capability labels / Admin includes everything | Code / copy | Every implemented capability has Turkish name/explanation; inaccurate all-permissions claim removed. | Full access page redesign. |
| Seconds/zone hidden, offsets differ | Representation | Seconds/zone visible; new manual default +03:00. Existing -03:00/+00:00 values and nonzero seconds survive unchanged. Offset conversion preserves the instant. | Complete three-stage layout, explicit mixed-zone preparation explanation, business acceptance. |
| WebSocket fallback / unload warning | Transport, uncorrelated | Not treated as the application error cause. No browser-policy or IIS changes. | Approved proxy/WebSocket trace distinct from API problem evidence. |

### Script Parity

Read all 741 lines of the supplied `in_use_v2_sifresiz.txt`, including update,
upload and BPM blocks. SHA-256:
`9DDBE8383606EC8006118B376E520109C9ED236C001817EB0AD1132CD93A3104`.
The script was neither executed nor copied into Git.

| Script segment | Current implementation / required correction | External fact or acceptance still needed |
| --- | --- | --- |
| 323-333, active OR / 4241 / 68 | Existing separate In Use read path; preserve exclusion from SDM. | Existing scope is not authoritative completeness evidence. |
| 348-370, four sheets | Existing managed XLSX creates Sunucular/NMS plus two intentionally empty technical tabs and provenance. | Empty tabs are not completed checklists. |
| 377-466, service relation 100049 | Existing KEY/SET semantic parser retains independent service/environment evidence, not positional offsets. | Exact [Genel] selector/response and per-service cardinality still needed. |
| 439, service ID before server loop | Script bug: never reuse that stale service/aspect. No equivalent pre-loop inference added. | Mixed-service fixtures must prove independent aspect lookup once implemented. |
| 499-520, three questions and bulk apply | Existing three explicit answers and selected-server differences retained. | Novice hierarchy, keyboard and conflict journeys remain incomplete. |
| 524-555, NMS/alarm defaults | Existing export returns unknown for non-operator checks. Versioned proposal policy still needs implementation; unknown must not become NonProd. | Prod proposal yes/yes/yes/yes; DEV/TEST no/no/yes/no; these are desired alarms, not monitoring verification. |
| 531-608, mixed ServerData | Existing source fields and operator answers share the workbook model, but organizational defaults/aspects are not completed. | Hardcoded country, department, team, contact, OS release and KONTROL require explicit policy/provenance, not fabricated source evidence. |
| 637-657, properties 4463/4464 | Not executed. Last-server environment at index 5 differs from per-server index 4 and is unsafe. | Exact property semantics, mixed-environment aggregation and conditional writes remain external-contract work. |
| 662-695, save/upload | WASAS uses immutable JSON envelopes outside deployment, not loose C:\InUse XLSX files. Browser download is separate. No source upload added. | Exact upload identity/result/correlation/post-state and uncertain-result recovery. |
| 701-730, first BPM item | No first-item mutation added. Local completion intent is not source completion. | Unique approved activity, concurrency, authoritative post-state, partial upload/BPM reconciliation. |

Sunucular's current 29-row layout and NMS's 22-column order were read in
`src/SecureOps.Infrastructure/InUse/InUseWorkbook.cs`; the NMS header order agrees
with the supplied list. Field-level disposition:

| Sunucular rows / NMS fields | Current provenance and remaining correction |
| --- | --- |
| ALAN ADI | Generated column label, not a source observation. |
| ENVANTER_ID / ENV_ID; HOSTNAME; SERVER_TYPE / Hardware_Type; SI_ENVIRONMENT / Server_Type | Existing semantic source fields. NMS Server_Type deliberately follows the script environment position, not an inferred hardware type. |
| CONSUMER_COMPANY; SERVICE OWNER DIRECTORATE | Company/organizational relationship labels; not individual application ownership. |
| SERVICE NAME / ITMC_Turuncu_Sunucu_Listesi_Karsilik; ITMC_Service_ID | Existing per-server service label/reference, checked for conflicting projections. |
| SERVICE ASPECT; ITMC_Servis_Unsuru_ID; ITMC_Servis_Unsuru | Missing verified per-service [Genel] contract; examples 2915/112 are not defaults. |
| NETWORK SEGMENT; IP ADDRESS / IP Address; OS NAME; OS_VERSION; Device_Type; CITY / City; BUILDING / Building | Existing selected KEY/SET fields preserved when returned. Missing cells remain unavailable, not erased by policy. |
| OS RELEASE; COUNTRY / Country; Department; Sub_Department; Contact_email; UY_Owner Mail Address; ITMC_Event_Owner_Group | No verified new mapping or versioned default added in this checkpoint. |
| Outbound Internet; inbound Internet; Microsegmented | Explicit per-server saved operator answers; partial drafts permitted. |
| NMS inclusion; ITMC_MEMORY_Alarm; ITMC_CPU_Alarm; ITMC_UP_DOWN_Alarm; ITMC_Disk_Alarm | Proposal policy still outstanding. Do not relabel these as successful checks. |
| KONTROL | Unknown, never automatically Evet. |

The existing XLSX writer uses inline string cells and no formulas/COM. Exact long
and leading-zero identifier round-trip scenarios requested here remain outstanding;
the source parser's canonical numeric identity contract must not be relaxed merely
to imitate an Excel display such as `1E+06`.

### Executed Checks

Private browser/publish evidence:
`C:\SecureOpsBuild\validation\rc621-defects-20260917`.
These are local test payloads, not release ZIPs. No Worker was started by this task.

| Check | Actual result |
| --- | --- |
| Release solution build | PASS, 0 warnings/errors, 11.17 s reported by dotnet. |
| Full mandatory format | PASS after correcting introduced line endings, whitespace and imports; no baseline waiver. |
| Normal regression / unchanged OpenAPI comparison | 1301 unit + 276 integration passed; 38 explicit opt-ins skipped, not counted as acceptance. |
| Focused recovery/presentation/In Use unit tests | 51 passed, includes archive audit rollback and storage failures. Overlaps normal regression. |
| Real JWT canonical actor + SQL/resource focused suite | 37 passed; separate last-admin guard opt-in initially skipped. Includes persisted search beyond page one, multiple reporters, Turkish casing, assignment filters and stale retention. |
| Separate fresh SQL administrative guards | 1 passed: simultaneous last-admin changes, forged actor, stale definition, audit rollback. |
| Fresh isolated schema harnesses | Three new guarded LocalDB databases, 001-021 and predecessor preservation passed. No corporate migration replay. |
| Original asset browser preparation | PASS: six actual images, 155 services, immutable MIME browser download, history and denial; 1440/390 captures. SMTP disabled. |
| Original asset MIME parser opt-in | 1 passed: six matching CIDs, original bytes/SHA-256/actual MIME formats, Turkish text and all 155 service rows. |
| New rc621-defects browser journey | PASS: visible mixed offsets/seconds, disabled-source draft preservation, forced JS download failure after archive, identical retry/reload XLSX and no duplicate archive, capability labels; 1366x768 captures. |

Failed attempts remain distinct: the new identity regression failed before the
controller fix; first SQL JWT setup lacked SQL session composition; first search
test compile lacked an import/type; first unit wording assertion was corrected.
Browser attempts first preceded host readiness, then correctly hit missing trusted
Mail in a fresh Demo profile. The guarded synthetic profile was supplied; no guard
was weakened. Download fault injection initially replaced a JS function cached by
Blazor; the corrected stable wrapper toggles the injected failure. Passing reruns
do not erase these failed attempts. No corporate failures were claimed reproduced
from blurred support IDs; no support identifier was guessed.

## Blockers

The requested delivery is unfinished. Remaining implementation includes the full
OCO/In Use hierarchy, effective configuration/readiness diagnostics, source-only
denial preservation, worker/queue states, policy-backed NMS proposals, verified
aspect/owner mapping, archive actor presentation and specific access guard reasons.
Remaining acceptance includes before/after target-state captures, both themes,
200% zoom, measured contrast/focus, complete packaged API/UI/Worker recovery and
all required final opt-ins. No new release identifier or package hash exists.
The uncommitted checkpoint totals 591 changed lines (additions plus deletions,
including new tests/docs); the baseline is unchanged. This is not a cap exception.

## Minimal Safe Next Step

The requested task-scoped line-limit exception is now approved. Continue the
remaining cross-layer redesign/parity/release work while preserving this checkpoint.
Local no-send walkthrough: https://localhost:64522/announcements and /in-use.
Task-owned API/UI/denied UI PIDs: 24676/11560/30408, ports 64521/64522/64523.
Confirm command lines before stopping these hosts; no prior host was stopped.

Operatör için: rc6.21'i değiştirmeyin; 001-021 veya Hangfire kurulumunu tekrarlamayın.
API/UI/Worker gizli ayarları ve ayrı kalıcı anahtar dizinleri korunmalıdır. Kaynak
hatası için destek referansı, UTC zaman, uç nokta/problem kodu ve başarısız aşama;
arşiv için yalnız yetkili kayıtta etkin InUseReports:Directory ve API hesabının
erişim sonucu yeterlidir. Tam yapılandırma, parola, token veya HAR paylaşmayın.
Worker kaydı, kaynak işi veya e-posta tesliminin tamamlandığı anlamına gelmez.

## Risks

Substring JSON search is server-side and bounded in returned rows, but still scans
matching persisted data; no corporate load or index plan claim is made. New storage
codes do not repair missing server ACLs. Original assets passing locally do not
prove their TEST deployment. Screenshot review still shows the reported hierarchy
debt. No accessibility conformance is claimed; the
[W3C error-prevention reference](https://www.w3.org/WAI/WCAG22/Understanding/error-prevention-legal-financial-data.html)
supports review/correction for consequential changes, not a dialog on every save.
No send, attachment upload, source update, BPM close, corporate call, permission
expansion, service installation, push or release occurred.
