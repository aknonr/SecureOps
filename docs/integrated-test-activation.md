# Integrated TEST activation continuation

## Owner exception and baseline

### Remaining-work continuation from 03b0c04

Verified incoming HEAD was `03b0c048d50cf926b5640116533df39c8685a37d`, clean on the
existing delivery branch. The same cumulative owner exception applies; all older
baselines below remain in force. No target D: deployment is accessible here.

The owner identifies the original current envelope as 27,044 bytes, SHA-256
`2FDB1ABB5837BF292F8912D8ED707AAF9342A96A4804EF8A05D88BB0E5CD828A`;
its embedded XLSX is 6,788 bytes, SHA-256
`11ECD7B916A9A6086D934C107936664CFBC999CB956EB7C3564A9145688C40DC`.
The malformed chat paste is a different representation, not evidence that the
original attachment is corrupt. Exact-name Desktop/Downloads search and available
resource inventory did not expose the original attachment in this session.

Catalogue decision: additive 024 will index only integrity-verified archive
metadata, never current names/OR values inferred for a historical report. SQL
search/count/paging requires current InUse.View and InUse.Review before counting.
Original account is frozen for new reports; absent historical accounts stay NULL.
Indexing old versions is an explicit bounded, version-protected operation;
repeating it is idempotent, conflicting metadata fails closed. Search never scans
the archive. Earlier 022/023 and all historical bytes remain untouched.

### Post-rc6.26 continuation

The owner explicitly extends the same task-specific exception to the reported
export, history, draft lifecycle, assignment, source and mail gaps. Inspected
continuation HEAD: `34c8837c2837b9a3ad61a432aa121cf41ac3e0c7`, clean, same branch.
The cumulative baselines below remain unchanged; inherited changes and new files
count. No intermediate release is requested. rc6.26 remains immutable.
The operator reports matched installation, SQL 022/023 and resolved account
lockout. These are not independently verified target observations; do not replay
the migrations or repeat account remediation.

An actual legacy XLSX is now supplied and its SHA-256 was independently verified:
`B3979BBC3A5F58EC7A824F7199F92ACF744D0AAED72243E00B75ABB762AAF2EF`
(12,718 bytes). Earlier statements below about no sample describe the prior
checkpoint only. The original current JSON attachment is not on the inspected
filesystem; the pasted copy has a malformed final `Archived` property. Its rows
and screenshots are owner evidence, not an independently parsed archive file.

The exporter repair preserves the corporate order NMS, CheckList_THY,
CheckList_TEKNIK, Sunucular. The checklists remain empty. New workbook bytes use
readable text-cell styles and widths without changing fields or data placement.
Internal provenance stays in the immutable envelope as separate evidence, not
extra corporate worksheet tabs. Existing envelopes and XLSX bytes are unchanged.
Human-readable download names use the original trusted preparer snapshot plus a
stable actor suffix and UTC preparation time; no current downloader substitution.
This changes neither GUID archive paths nor the `<OR>_InUse.xlsx` remote contract.

### Continuation accounting

Owner exception remains cumulative, including inherited and new/untracked files.
Counts below are the current cumulative Git diff, not sums of overlapping commits.
They include OpenAPI, new files and inherited work; a commit does not reset them.

| Baseline | Files | Additions | Deletions |
|---|---:|---:|---:|
| immediate remaining-work checkpoint 03b0c04 | 48 | 1857 | 60 |
| actual continuation 34c8837 | 74 | 3368 | 139 |
| inherited rc6.24 8c1b58d | 125 | 7687 | 1132 |
| complete post-rc6.22 aa9e4d2 | 159 | 11002 | 1296 |

Scope includes workbook/metadata, local lifecycle/history invalidation, assignment,
source readiness, catalogue/024, reviewed reporter crosswalk, tests and guidance.
No unrelated refactor, corporate migration, grant, activation or release ZIP.

The owner extends the task-specific size exception to integrated OR/SDM, In Use,
OCO and existing management reporting, necessary persistence, tests and delivery.
Verified starting HEAD is `c4a835274cb1ce511a564137ad1c213a79b056f2`, clean, on
`feature/combined-test-delivery-20260915`. Accounting remains cumulative against
`8c1b58d0bfdbb363c451139cd212b08a83be7c86`, including inherited work; retain the
whole post-rc6.22 `aa9e4d2` count. No reset, grant, corporate effect or deployment
is implied. rc6.24 is immutable and does not contain later product changes.

## Implementation decision

Extend `/dashboard` and the existing capability-protected management API. New
workflow reporting uses SQL materialized, bounded report snapshots so metrics,
pages and export share one as-of cut, not successive live reads. Snapshots belong
to the authenticated actor/access version and permitted module scope. Current
authorization is rechecked on every page/export; an access-version change requires
a fresh snapshot. Existing global In Use/OR view policies are retained. OCO remains
owner-only, also for report holders; no implicit cross-owner permission is added.

New additive 023 stores report snapshots/facts and verified archive receipts.
No existing migration is rewritten. Snapshot capture is a bounded serializable
SQL read/materialization; paging and aggregation occur in SQL. Exceeding the
implementation bound refuses a snapshot, never silently truncates it.
Dates are UTC half-open intervals; UTC+03:00 is an explicit display choice, not a
reinterpretation. Backlog is current at capture, not reconstructed historical
backlog. Period events and current states are labelled separately. Unknown dates
are excluded from time-selected events and remain a limitation, not zero-duration.

Archive receipts follow verified envelope commit/read. An authorization audit
before file commit is not an archive receipt. Receipt repair occurs on a verified
re-download; old unindexed envelopes are incomplete historical coverage, not
invented archived counts. Immutable XLSX bytes remain outside webroot. Worker
continues consuming SQL-frozen bytes, not a second filesystem archive.

Safe managed string-cell XLSX export reuses the established writer. No source
query, write, upload or SMTP is triggered by reporting. Metrics count explicit
units, never a sum of independent stages or an employee ranking. Synthetic rows
are excluded by default; synthetic demo inclusion must be explicit and labelled.

## Requirement/evidence tracking

This is the single current remaining-work register. Detailed commands and minimal
masked requests are in [the continuation handoff](post-rc626-continuation-tr.md).
Implemented, local tested, exact-payload tested, installed, configured and remote
verified are separate states; no row below implies full team activation.

| Requirement / class | Implementation and evidence/build | Owner | Concrete input / next action | Target status |
|---|---|---|---|---|
| Report catalogue / local implementation | SQL bounded authorized metadata search, explicit integrity-checked indexing, readable original metadata; `InUseCatalogue` SQL tests and new UI route `/in-use/reports`; requires 024 | Developer / authorized test operator | Current browser/payload gate; apply only reviewed 024 with matched successor after gates | Not in rc6.26; not installed |
| RFC suggestion / missing source contract | Exact scoped crosswalk resolver and explicit UI decision; matched/no-match/ambiguous/ineligible tests; no display-name matching | Source identity owner / access owner | One approved source scope + reporter reference -> existing eligible application GUID, review reference and expiry; then target case | Resolver complete locally; corporate mapping absent |
| Assignment, lifecycle, catalogue, OCO UI / test-runner restriction | Incoming 03b0c04 repairs retained; current browser script includes pointer, focus, themes, reflow and native zoom assertions | Authorized test runner | Run existing host/test procedure; prior tool-policy rejection not bypassed; OCO source journey separate | Current UI screenshot/200% acceptance pending |
| Four-sheet Excel and history / corporate acceptance | Prior real legacy hash/29 labels/22 headings and synthetic Excel acceptance retained; immutable download tests | Evidence owner / TEST operator | Original 27044-byte current attachment unavailable locally (requested once); old/new authorized target download hashes | rc6.26 six-sheet output remains installed; repaired exporter needs successor |
| Draft recovery / corporate acceptance | Versioned reset/discard/restart, retained archive and invalidated suggestions; API/unit/SQL tests | TEST operator | Current UI journey then exact installed successor trial/restart case | New-source behavior not yet installed |
| In Use upload and workflow / missing source contract + unfinished integration | Durable stages and known wire mutation client registered only for real source transport; no guessed readback dispatcher | Source API/workflow owner, then developer | Attachment parent/ID/content readback; keyed dynamic-case + true conditional concurrency; authoritative OR states. Implement full real completion transport against those facts | No confirmed real upload or OR closure; not feature-complete |
| OCO source / target configuration + corporate acceptance | Existing real SCCM/Turuncu Hat adapters, proposals and profiles retained; diagnostic distinctions improved | Integration operator | Sanitized API/normal-start Worker reports; approved profile, provider/queue and exact selected OCO -> terminal job/count/date evidence | Installed rc6.26 owner-reported; source configuration/job unverified |
| Self-test/distribution / target configuration + corporate acceptance | Real SMTP and durable outcomes retained; prior local sink/restart evidence | Messaging owner / operator | Actual relay/TLS/envelope policy, remove false startup overrides only under staged guide; exact self-test preparation then separately reviewed audience | No target send, inbox or Outlook evidence |
| OR to SDM / corporate acceptance + type-specific contract | ServerRequest positive policy; immutable Jira-only/close intent and durable link retained | Jira/source owner / operator | Exact selected OR/destination/actor; verify Jira result; other enum types need reviewed mappings, close needs final-state contract | No new target acceptance; not all types enabled |
| Management reporting / corporate acceptance | rc6.26 SQL 023 snapshot/drilldown/export preserved; discarded-state repair retained | TEST operator | Reconcile exact selected authoritative outcomes with same dashboard snapshot/export | Installation/023 owner-reported; new-result reconciliation pending |
| Collector / corporate acceptance | rc6.26 matched completion-evidence binary present; six package hashes rechecked | Source operator | Existing bounded read-only command; share only masked Evidence, not secrets; cannot discover undocumented attachment API | Not run here corporately |
| Archive and operating window / target configuration | Immutable envelopes; owner-configured D:\SecureOpsData\InUseReports; no move requested | TEST operator / operations owner | Effective API access and original-byte readback; named foreground Worker session/window/restart owner | Screenshots and owner report retained; effective access not independently verified |

## Activation boundaries

The owner must select exact source IDs, Jira project/type and actor for each
controlled case. OCO self-test uses trusted Mail; distribution requires a reviewed
immutable preparation and explicit To/Cc audience. Unknown outcomes stop mutation
replay. Foreground Worker requires a named operator/session and operating window;
this work does not install or promise an unattended Windows Service.

## Consolidated external facts (not repeated installation questions)

| Exact fact / operation | Existing support | Missing evidence and responsible role | Affected action |
|---|---|---|---|
| `SMSS_oRFF.p_emb_dynamic_case_orff` keyed identity; properties 4463/4464 | Script request fields; bounded completion probe; durable execution/wire builder | Source owner: one selected OR's masked SET/KEY shape and documented conditional version/precondition semantics | Real In Use field effects |
| `DataRestSecure.svc/json/uploadattachment` target and bytes | Known `fBase=SMSS_oRFF`, exact `fId`, `fName`, `datastring`, `SessionID`, `TenantId`; response serializer | Source owner: supported attachment identity/list/content readback with exact OR binding and hash/byte evidence, response-loss reconciliation | Confirmed upload; safe restart |
| `BPM_Actvty`, models 103626/103627, status 1/group 68/main-object OR | Known query/update pattern; zero/multiple candidate guards; probe | Source owner: uniqueness/precondition semantics and activity readback; no first-row assumption | Eligible workflow action |
| Authoritative OR state | Distinct closure evidence model; fixture readback; corporate verifier unavailable | Source owner: exact query/select, response representation and closed/further-workflow meanings | In Use and SDM verified source closure |
| SDM type/project/issue fields/reporter/assignee | Exact enum includes ServerRequest, EnvironmentRequest, SoftwareInstallation, ConfigurationRequest, OperationalSupport, NotJiraEligible, NeedsManualReview, ServerRetirement; positive policy only ServerRequest | Jira/source owners: approved type-specific policy and bounded mandatory-field metadata for selected destination. Owner selects OR, actor and intent | No implicit activation of other types |
| Unknown Jira create | Persisted command/link and reconciliation stop | Jira owner: authoritative exact correlation lookup for the selected project or reviewed manual remote identity evidence; empty searches do not prove absence | No duplicate creation after lost response |
| OCO profile/collection/service/date source | Fixture and real SCCM/Turuncu Hat source adapters; selective apply | Integration owner: exact approved OCO/profile and API/Worker effective queue/provider/profile alignment, successful job evidence | Corporate retrieval |
| Relay From/envelope/TLS and recipients | Real SMTP transport, immutable MIME and Worker; local rejection/unknown/revocation evidence retained | Messaging owner: approved relay policy/config revision. Operator: exact preparation, self-test actor Mail, separately reviewed distribution To/Cc; Outlook observation | Controlled mail activation |
| Actual archive and runtime composition | Protected diagnostics report effective path/identity/provider source, not only JSON | TEST operator: effective API path, old/new hash manifests, installed 022, actual runtime identities, named foreground Worker window | Data-preserving activation |

At the rc6.26 checkpoint no actual sample XLSX had been attached. That limitation
is superseded by the verified legacy workbook above; the original current envelope
is still requested, not reconstructed from its malformed pasted ending. The timestamped script SHA remains
`BB07673BCC19FABFB07005E24897810FE9AD91FA6936049E7EB271CABAC0D704`.
Current In Use workbook/reuse/execution trace and deliberate legacy bug corrections
remain in `docs/inuse-v2-followup.md`.

## Reporting definitions and verification bounds

The metric catalog is `WorkflowMetricCatalog.Definitions`; time basis is shipped
with every metric. Current OR/server counts are not filtered as historic backlog.
Preparation/archive use PreparedAt; In Use verified transitions use EvidenceAt;
Jira linkage uses first persisted JiraCreatedAt; final closure uses ObservedAt.
OCO source/send cohorts use SubmittedAt/CreatedAt and their state at capture, not
one count per retry. In Use partial is confirmed attachment with no confirmed OR
closure and a stopped failure/unknown/unconfirmed/blocked operation; it overlaps
its step metric deliberately and must not be summed as a new business record.
UTC intervals are half-open; selected timezone affects presentation/boundaries.

At most 50,000 facts per cut, 100 per page and 92-day period. SQL refuses overflow,
not silent truncation. One-hour access expiry is not deletion. Serializability
protects the retained cut; readiness is separately observed and labelled. Audit
and scope/version guards apply before results are returned. Legacy dashboard
summary panels retain their own documented period/as-of; they are not this
workflow cut or its export and must not be summed with it.

W3C verification references: [contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html),
[error identification](https://www.w3.org/WAI/WCAG22/Understanding/error-identification.html),
[status messages](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html),
[target size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html).
Record actual computed contrast/keyboard/zoom observations, not a screenshot-only
compliance claim. The body-size product target is not a WCAG font-size assertion.

## Executed development checks

Evidence root: `C:\SecureOpsBuild\validation\integrated-activation-20260918`.
These checks are loopback/LocalDB only and precede final ZIP acceptance.

| Check | Executed evidence |
|---|---|
| Release build | Zero warnings/errors after final product changes |
| Normal regression | 1,349 unit and 278 integration passed; 49 opt-ins skipped in normal mode. `final-accepted_net8.0_*.trx`; opt-in results below are separate executions, not an added unique-test total |
| Full format | `format-stable.json` is empty; full verify passed after final product/test changes |
| SQL 001-023 path | Fresh `SecureOps_ResourcesV1_ReportFinal18`; upgrade fixture and all 35 resource/In Use/report SQL scenarios passed |
| SQL report details | `workflow-final.trx`: four tests, owner/module scope, dates, immutable snapshot, revocation, receipts, partial result, export, pagination |
| Query observation | 1,000 new synthetic ORs added to retained fixture: 2,004 unassigned contributing rows; capture 1,864 ms, capture+page+export 1,955 ms. One local observation, not an SLA |
| SMTP/SQL | `mail-sql-accepted.trx`: seven passed. Initial test-sink disposal race fixed only for cancellation shutdown; production SMTP behavior unchanged |
| Source/SQL | `source-sql.trx`: six passed with isolated 001-023 and Hangfire schema 9; no corporate source calls |
| Historical preparation | `fingerprint-accepted.trx`: nullable origin is omitted from old serialized drafts; reload preserves the legacy preparation fingerprint; explicit synthetic origin is distinct |
| Browser | `report-browser3/result.json`: same-cut Excel, filtered drilldown, OCO draft link, denied direct access, synthetic default exclusion, both themes at 1366/1440/390 |
| Native zoom | `report-zoom/result.json`: Chrome native 200%, outer 1366 / inner 674, DPR 2 / visual scale 1, no page overflow, keyboard focus, 16px body |
| Sampled contrast | Body text/solid background: light 13.8000:1, dark 17.8131:1; sampled only, not full WCAG certification |

The first browser attempts exposed an ambiguous test label locator and a test
assertion racing async filtering. Explicit accessible names and a settled-row wait
resolved these; failed evidence is retained, not reported as a pass. Initial
OpenAPI/schema packaging expectations were updated for the actual added contracts
and 023, then normal equality/regression passed. No corporate secrets, sending,
upload, closure, permission change, target SQL or deployment occurred.

## Package acceptance correction

The first candidate rc6.25 (d6b285c) failed the standalone collector payload gate:
its native SkiaSharp dependency published a PDB despite DebugSymbols=false.
No final release metadata was produced. Retain that failed directory; do not install.
The collector now removes only verified files in its fresh tool staging before
the unchanged payload/integrity scan, matching component package handling.
The successor is required for this concrete acceptance failure, not another
intermediate product iteration. Product behavior is unchanged from d6b285c.
