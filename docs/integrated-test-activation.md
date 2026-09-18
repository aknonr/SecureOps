# Integrated TEST activation continuation

## Owner exception and baseline

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

| Result | Current baseline | Remaining work / next evidence | Target |
|---|---|---|---|
| OR to SDM | ServerRequest positive policy; explicit Jira-only intent and durable link | Verify exact types/mapping; selected OR/destination/actor; source closure contract | Not activated here |
| In Use review/history/workbook | rc6.24 packaged synthetic acceptance; no supplied XLSX | Retain parity; receipt/reporting; actual archive setting and preserved re-download | Effective path unverified |
| In Use attachment/closure | Durable fixture executor, tested wire serializer | Actual readback/conditional contracts and real adapter registration | Not corporately verified |
| OCO source/preparation/mail | Source jobs, six original CID assets, v3, optional SMTP; c4a8352 local sink/restart | Final package and target providers/relay/Outlook/selected recipients | No live send authorized by generic scope |
| Management workflow reporting | New SQL 023 frozen metric/detail/export, module capability and OCO owner scope | Final UI/package acceptance and target schema/readiness | Not activated |
| Collector | c4a8352 completion-evidence source, absent rc6.24 binary | Matched standalone delivery and D: private runbook | Read-only operator execution pending |
| Archive move | Immutable envelopes; proposed D:\SecureOpsData\InUseReports | Inventory, pause, backup, hash-preserving copy, exact IIS key, API-identity verification | No corporate filesystem access established |

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

No actual sample XLSX was attached. Pasted rows and script layouts are test inputs,
not workbook-to-workbook acceptance. The timestamped script SHA remains
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
