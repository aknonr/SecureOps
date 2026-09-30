# Integrated TEST activation continuation

## Owner exception and baseline

### Independent SQL gate and pinned Service Accounts integration, 2026-09-30

The approved local integration branch is
`feature/service-accounts-pinned-integration-20260929`, worktree
`C:\SecureOpsBuild\secure-ops-sa-pinned-integration-20260929`.
It starts at published platform `e997c5b68cebcd23716860a9b06fdc25ebbb4493`
and fast-forwards to pinned Claude handoff
`b4fdf8d439990032fb5f4c21486030bdb903a27c` (measured module code
`b8aad7edd68e6e35438ba0b8516ea25dc4e2ad3b`). The new recovery bundle
contains this ancestor; only that ancestor was integrated. The subsequent
`bae78b9..7e227ed` follow-up is NOT incorporated. No old `a3037175` Jira
UI delta was reapplied. Published delivery, original dirty worktrees and
sealed packages are unchanged. Retained SDM preflight documentation is
carried forward here without changing either original worktree.

New Windows evidence at the pinned product content: Release build 0 warnings /
0 errors; unit 1568 passed; normal integration with the fresh module database
316 passed / 61 opt-ins skipped / 0 failed. The 33 module integration results
(30 SQL + 3 HTTP composition) are INCLUDED in 316, not added to it. The first
fresh-database run passed; its safe diagnostics contain only the intentional
audit-rollback error 51091. A separate new platform database ran ResourceSql
49/49, including Jira-only and In Use regression. OpenAPI regeneration passed
1/1; semantic comparison preserves every existing platform path/schema and
adds 44 module paths, with no semantic change versus the pinned snapshot.
These results are not corporate/IIS/browser acceptance. Details and bounded
shared-file review: [Service Accounts integration evidence](service-accounts/INTEGRATION-20260930.md).

The remaining target SQL gate is effective rights in the **normal API SQL
session**: SELECT/INSERT on the six inspected 022/023 objects and UPDATE on
`ops.InUseExecutions`. The prepared permission-only query is
[`Read-SdmApiEffectivePermissions.sql`](../scripts/diagnostics/Read-SdmApiEffectivePermissions.sql).
It has two metadata result sets and never reads corporate records or probes
writes. Do not repeat the completed inventory/contract queries. Neither the
operator session nor `health/persistence` (only SELECT 1) closes this gate.
Inspected rc6.26/current API diagnostics have no in-process permission executor;
the query is prepared, not an available endpoint. An approved, separately
reviewed read-only API-process support operation using the unchanged existing
connection is required before execution. No runas, EXECUTE AS, new credential,
identity/permission change, target helper installation or security-held package
is authorized here. Retain both result sets privately with process/configuration
provenance; all 13 rights must be 1, and NULL/0 leaves the gate open.

| ID | Status / owner | Exact next action and evidence |
|---|---|---|
| SDM-01-SQL | Inspected 022/023 match retained; normal API rights pending / operator + API support owner | Approve the bounded API-process permission query execution path; capture normal-process provenance and two sanitized result sets. No operator impersonation or DML probe |
| DB-01-024 | Conditional change prepared, NOT authorized/executed / SQL execution operator (owner) + change/release owners | API rights gate, accepted matched payload and separate 024-only change approval; verified backup/recovery point, reviewed 024 DDL/narrow API grants, stopped writes. Any 023 drift or partial/existing 024 stops the change |
| SA-INTEGRATION | Pinned module integrated and Windows tests passed locally / integration owner | Review pinned-source limitations and remaining allowed HTTP/UI acceptance; no subsequent follow-up, migration number or pilot activation included |
| SA-FIRST-RUN | Two older isolated failures remain unexplained / Claude | Preserve original missing-message/Unavailable evidence. New first run passed; do not claim that resolves old failures |
| SA-PILOT | Not accepted / module, access and TEST owners | Approved bundles/scope grants, restricted-runtime Windows execution and real authenticated HTTP/UI journey. Temporary access-wrapper browser evidence remains historical limited evidence |

024 execution is authorized only by a later explicit approved change naming
the exact reviewed 024 script/grants and accepted matching component payload,
after the above prerequisites. This task and the read-only results do not
authorize it. In Use IU-05, Service Accounts pilot and SCCM/Falcon remain
independent gates; no flags, installation, corporate writes or new package.

### SDM-01 target inventory and read-only SQL result, 2026-09-29

Operator-supplied read-only IIS output captured `2026-09-29T17:19:02.7571230Z`
identified one started API and one started UI site on the selected TEST host.
The exact site paths and entry-DLL hashes are retained outside this public
repository in the private local evidence record
`C:\SecureOpsBuild\validation\sdm-target-preflight-20260929\iis-entry-evidence.json`.
Their entry DLLs both report
`0.1.0+028cbd2e4ec7068d71a33088d7c651e4df21644a` (rc6.26), not the
matched `deda8486b57c04a23aba203c0e79f96b746102e9` review candidate.
These are two entry-file observations, not full installed-payload hashes.
The IIS script initially could not locate `Microsoft.Web.Administration` by
`Add-Type -AssemblyName`; loading that DLL from its full `inetsrv` path produced
the successful read-only output. No IIS state was changed.

The Worker has no Windows Service registration in the supplied target check and
has historically run in a PowerShell console. An empty `Win32_Service` result
is not a Worker-failure observation. Its current executable/version, console
owner, active process and handover window are not verified by this IIS result;
do not start it for this preflight. Jira-only creation runs in the API, so
Worker service installation is not a prerequisite for the one-OR Jira-only gate.

The operator then supplied screenshots of the three result sets from the
**read-only** `scripts/diagnostics/Read-SdmTargetSqlPreflight.sql`
on the reported TEST database (`CapturedAtUtc=2026-09-29T18:51:01.8521670Z`).
The SQL execution operator confirmed the queried database is the approved TEST
target. `CanViewDatabaseDefinition=1`. All nine
022 and ten 023 expected metadata rows
were `Visible`; the relevant indexes and triggers were enabled. The 022 active
execution index was unique and filtered on `Active=1`; the 023
`SourceSynthetic` column was nullable `bit`. All four 024 catalogue rows were
`NotVisibleOrAbsent`. No possible ledger table appeared in the third result.
The full server/database identifiers and screenshot transcription remain outside
this public repository in the private local evidence record
`C:\SecureOpsBuild\validation\sdm-target-preflight-20260929\sql-object-evidence.json`.
This is operator-supplied screenshot evidence, not verified original-file
integrity or a live query by this agent.

Thus 022/023 object presence is observed in the operator-confirmed TEST
database, while 024 objects are not visible despite database-level definition
visibility. This is not a migration execution record or proof of runtime
permissions. The repository has no migration ledger; historical 022/023 logs
or change notes have not been supplied. Retain them if they exist, otherwise
record unavailable, not a blocking presumed DBA receipt. No migration may be
replayed or applied from this evidence.

The operator then supplied pasted result sets 1-5 from
`scripts/diagnostics/Read-SdmContractDetails.sql` against the approved TEST
database. Compared with the reviewed `sql/schema/022` and `023` files, the
queried columns/types/nullability, PK/UQ/CHECK/FK relationships, index key
order and filters, six trigger definitions and enabled/trusted state match
semantically. SQL Server-generated names for unnamed constraints, normalized
CHECK term order and trigger whitespace differ textually but do not change
the predicates or trigger behavior. No required 022/023 metadata was NULL or
invisible; NULL defaults, non-applicable definitions and unfiltered-index
filters were expected. No 024 catalogue metadata appeared in result sets 1-5,
consistent with the earlier four `NotVisibleOrAbsent` rows. Result set 6 is
explicitly the operator's SQL session, **not API runtime permission evidence**,
even though its account label may resemble an API account. The private summary
is `C:\SecureOpsBuild\validation\sdm-target-preflight-20260929\sql-contract-022-023-evidence.json`;
the pasted text has no original-file integrity verification and was not queried
by this agent. The query did not report collations, identity seed/increment,
FK referential actions or index storage options; no byte-for-byte/full DDL
equivalence is asserted. The match does not prove the scripts ran verbatim or
that the API has effective object rights.

The reviewed 022 contract covers unique server reviews, a filtered unique
active-execution index, state/step checks and immutable review/intent/event
triggers. The reviewed 023 contract adds nullable `bit` `SourceSynthetic` and
immutable workflow snapshots, facts and archive receipts. Current API code
uses SELECT/INSERT on reviews, events and the three reporting tables, plus
UPDATE on executions. The same read-only contract script can check the 024
catalogue after a separately approved change. Its permission result is always
for the current SQL session only. Local syntax/result-shape testing used a
disposable 001-024 database, not the corporate target.

| ID | Status / owner | Exact next input and gate |
|---|---|---|
| SDM-01-IIS | Target entry DLLs verified / TEST IIS operator | Preserve the pasted site/version/hash output; later compare approved full payload, not just entry files |
| SDM-01-SQL | Approved TEST DB confirmed by operator; queried 022/023 contract semantically matched; 024 absent in scoped metadata; API rights unresolved / SQL execution operator | Obtain effective 022/023 SELECT/INSERT and executions UPDATE rights in the normal API SQL context through an approved read-only method. Result set 6 from the operator session does not close this. Retain historical logs if available, otherwise mark unavailable. Only then review a separately approved 024-only change; no replay |
| SDM-01-CONFIG | Effective Jira/pilot settings unknown / API + Jira owners | Redacted effective-value/source comparison in the normal API environment; `diagnostics/operations` does not expose these keys. Keep write gates closed and secrets out of evidence |
| SDM-01-WORKER | Console operating model reported, not service failure / Worker operator | Identify current console owner/process and later handover/rollback responsibility; do not execute Worker or SCCM diagnostics in this preflight |
| SDM-01-TARGET | Corporate one-OR acceptance unexecuted / business + Jira + platform owners | Select one open TEST ServerRequest OR, exact source ID/fingerprint, approved destination/mapping, actor and Jira-only intent; retain one reviewed preview and controlled outcome |
| IU-05 | Separate source-contract blocker / source owner | Retain the three existing In Use transport requests; no inference from SDM SQL or IIS evidence |
| SCCM-SECURITY | Separate Falcon permission gate / Cyber Defense | Exact binary approval and later source journey, not implied by queue or Worker inventory |
| SA-OWNERSHIP | Separate Claude worktree / Claude + integration owner | No Service Accounts merge, migration numbering or activation in this SDM preflight |

The 2026-09-28 public-visibility/push paragraph below is historical: the owner
subsequently authorized a normal public push and reported remote tip
`e997c5b68cebcd23716860a9b06fdc25ebbb4493`. This read-only target work
does not push or change the published product. Conditional deployment and the
first one-OR acceptance remain in `docs/post-rc626-continuation-tr.md`; the
sealed review ZIPs remain review-only, not installation-ready.

### SDM-01 source handoff and target preflight, 2026-09-28

The integration worktree was clean at `9f4f57d00cdb759ac0be888b9727e0baa34e1246`.
`deda8486b57c04a23aba203c0e79f96b746102e9` is its ancestor; the two
intervening commits change only `docs/integrated-test-activation.md`,
`docs/post-rc626-continuation-tr.md` and the pinned UI handoff document.
ProductVersion and the three review ZIP hashes remain those of `deda848`.

**Source publication gate:** an unauthenticated GitHub API query returned
`private=false`, `visibility=public` for `aknonr/SecureOps`; its signed-out web
page also displayed Public. Git advertised the pinned UI branch at `a303717` but
no `feature/sdm-integrated-test-20260928` remote ref. The requested push was
therefore withheld. The owner must make the intended repository private and
verify that state, or supply an approved private destination; inspect any prior
public exposure separately. Do not publish this corporate continuation to the
currently public remote. The new-commit review found 118 changed paths, no
binary/archive/runtime-config/credential paths; three secret-like assignment
matches were synthetic test literals, not credentials. This scan is not a
substitute for private visibility.

**Release guard:** `scripts/release/New-PairedTestRelease.ps1:17` requires the
exact branch `feature/combined-test-delivery-20260915` and line 19 requires a
clean tree. That branch is attached to the original 96-file dirty worktree.
For a later approved numbered release, use an independent local clone and
check out that branch name there at shared base `5c986a9`; fast-forward it to
the *tested product* `deda848` (ancestor check passed) and verify clean status.
The existing release script can then run from the clone without changing either
original worktree or its guard. Running it from documentation tip `9f4f57d`
would stamp a different ProductVersion and require new testing/payload identity.
The exact branch-only preparation, for an unused private local directory, is:

```powershell
git clone --no-local --no-checkout C:\SecureOpsBuild\secure-ops-sdm-integrated-test-20260928 C:\SecureOpsBuild\sdm-release-promotion-review
git -C C:\SecureOpsBuild\sdm-release-promotion-review switch -c feature/combined-test-delivery-20260915 5c986a96f1f6639e47bf1432a87c3ac55e98054d
git -C C:\SecureOpsBuild\sdm-release-promotion-review merge --ff-only deda8486b57c04a23aba203c0e79f96b746102e9
git -C C:\SecureOpsBuild\sdm-release-promotion-review status --porcelain
git -C C:\SecureOpsBuild\sdm-release-promotion-review rev-parse HEAD
```

The clone and script have not been run. The release script exports the docs at
its build HEAD, so its `deda848` operator copy predates the later closeout.
Release owner must bind the separately hash-identified current operator guide
to that exact tested product; a script run at a later docs tip is a new build
requiring its own verification. The review ZIPs are not silently renumbered.
No release script was run in this handoff. Select a release name only after
rechecking inventory (currently through rc6.26), target 022/023/024 state,
payload/security approval and the accepted release scope; `-UpgradeFromRc626`
is conditional on verified 023 and emits only 024 as the delta. Keep the later
operator documentation as a separately identified closeout, not build source.

**Target preflight:** the current Turkish operator entry below contains the
small read-only version/object/configuration checks. Owner-reported rc6.26
entry versions, repaired API/Worker OCO parity and 022/023 installation are
historical reports, not fresh checks. The protected API operations diagnostics
reports effective OCO/mail composition but does not expose OperationalRecords
or Jira pilot/mapping keys. File/web.config inspection is source-layer evidence,
not an effective-runtime readback when other providers/overrides exist. The
normal API owner must reconcile these layers without exposing Authorization,
connection strings or credentials. A NULL SQL metadata result can reflect
insufficient metadata visibility; the SQL execution operator uses the authorized
read-only inspection identity. Historical execution evidence is recorded only
when actually available; it is not a separate DBA approval prerequisite.
No current Jira destination or selected
ServerRequest OR has been inferred.

### SDM-01 combined integration, 2026-09-28 (current)

#### Pinned UI handoff reconciliation

Audited UI snapshot `a3037175bb0bb9ecc7ab5c36c7c28607726469ce` is present
locally and compared by Git blob identity, not by branch name alone. Of its
19 changed paths, 16 code/test files are identical to the integrated source.
`OperationalRecordDetail.razor` differs only by the tested initial access-listener
ordering correction; the README retains that correction, the distinction between
UI-to-API and API-to-Jira transport, and prior In Use work. The remaining new file,
`ui-or-sdm-jira-only-cloud-handoff.md`, is retained verbatim as historical UI-owner
handoff. Its uncommitted-backend and branch-only test descriptions are historical;
the final integrated evidence below remains authoritative. No duplicate patch,
cherry-pick, backend reset or product rebuild was needed.

New focused verification: 49 unit cases passed, zero failed/skipped, using the
unchanged final Release assemblies (`JiraSubmissionTests`, access-provider and
matching transfer tests). Evidence:
`C:\SecureOpsBuild\validation\sdm-ui-pinned-a303717-20260928\ui-pinned-equivalence.trx`.
This is a focused unit run, not another run of the 49-case SQL suite. Product
source, matched ZIP hashes and prior final-source SQL/browser results are unchanged.

Handoff limitations remain explicit:

- One approved operator submission is not a guarantee of one UI-to-API HTTP
  POST. An empty-body transport resend uses the deterministic actor/command/target
  create key and durable command/transfer protection. Retry fallback additionally
  includes record version; this is not a remote Jira idempotency contract. The
  API-to-Jira JSON socket tests are separate evidence, not corporate TLS proof.
- F5 loses circuit-local uncertainty if the API has no persisted execution state.
  Persisted `CreatingJira`/reconciliation still blocks writes. Absence of a key,
  or a new preview button after reload, is not permission to resubmit an unknown
  attempt. Keep the operator stop/reconciliation rule across reloads. F5 during
  an in-flight browser command remains unexecuted, not covered by restart evidence.
- Retry confirmation cannot redisplay the persisted reviewed field preview with
  the current DTOs. No preview, issue URL, command identity or replay field was
  invented. The first corporate acceptance excludes retry: explicit rejection or
  uncertainty stops the window; any subsequent retry needs separate reviewed
  evidence and approval. Existing command/audit evidence requires authorized
  support collection, not an assumed UI field.

#### Final local outcome

Tested product source: `deda8486b57c04a23aba203c0e79f96b746102e9`;
all three ProductVersion values are `0.1.0+deda8486b57c04a23aba203c0e79f96b746102e9`.
The product worktree was clean at publish. A later documentation-only closeout
must not replace this build identity. Cumulative product diff from the shared
base is 117 files, 6,318 insertions and 447 deletions under the retained scoped
completion exception; it includes all inherited work, not just this turn's fixes.

Current matched review candidate:
`C:\SecureOpsBuild\delivery-review\2026-09-28-sdm-integrated-test\final`.
`candidate.json` records ZIP identities/hashes and the SQL inventory;
`payload-manifest.json` records all staged file hashes and component versions;
`manifests/Api.sha256`, `Ui.sha256`, `Worker.sha256` describe the exact ZIP members.
`evidence/verification.json` and its copied TRX/browser results identify the
final local checks. `operator/` contains hash-preserved exported guidance from
the documentation-only closeout recorded separately in `candidate.json`.
Packages are `packages/SecureOps.{Api,Ui,Worker}-deda848-TEST-review.zip`.
Only this `final` directory is current. Earlier 0644141/3a8b637 outputs in the
parent are visibly superseded for concrete browser defects; no new rc number
was assigned. E-05/E-06/E-07/E-08 and the security-held SCCM package are unchanged.

| Component | Entry DLL SHA256 |
|---|---|
| API | `6CA3B302AA3E5B777027B29061928FE1ACE666BAE329C25AF2C551D401E5DE1C` |
| UI | `FA3763E8DD6776671A5E68EC4014A9F6FAD344257E2D03FD9E5825182DB48F60` |
| Worker | `812CF134E3B568A65F13ADCC2D6BC969F347EEA79343328B3D0405A195D6C472` |

Newly executed final-source evidence under
`C:\SecureOpsBuild\validation\sdm-integrated-20260928`:

- Release build: zero warnings/errors. Full format verification passed after
  mechanical corrections; earlier failed reports remain available.
- `tests/delivery-unit.trx`: 1,512 passed, zero failed/skipped.
- `tests/delivery-integration.trx`: 283 passed, 61 opt-ins skipped, zero failed.
  The two actual CorporateJiraClient socket checks are included in 283, not extra.
- `tests/delivery-sql49.trx`: 49 passed, zero failed/skipped, fresh guarded
  `SecureOps_ResourcesV1_OcoSdmDelivery0928F`, test-only 001-024. Those names
  overlap normal-run opt-ins; totals must not be added as disjoint suites.
- `browser-delivery/jira-only-results.json`: real Chrome against the final
  published API/UI and isolated SQL/Simulation. Capability denial, three review
  declarations without type coercion, source-version conflict, keyboard focus,
  confirmation/cancel/double-click, one persisted synthetic key, no source-close
  stage, replay and unknown-result stop passed. Desktop 1440 and mobile 390
  screenshots are retained; this does not claim native 200% zoom or all In Use
  visual gates passed.
- `browser-delivery-restart/restart-results.json`: actual task-owned API stop
  and restart, same persisted key and source-open intent; unknown stays blocked,
  no second create. This is not corporate restart or Worker service acceptance.
- `browser-delivery-fault/negative-results.json`: five scenarios passed on
  fresh `SecureOps_ResourcesV1_OcoSdmFaultDelivery0928G`: another actor publishes
  first, lost response after create, request never delivered, stale source with
  explicit retry confirmation, and unsupported types remaining review-only.
- `tests/access-race-before.trx` failed as expected (two notifications instead
  of one); `access-race-after.trx` passed. Final full suites include the regression.
  Earlier `browser-negative`, `browser-negative-corrected` and `browser-accepted`
  failures are retained, not relabelled as passes. Final runs passed without
  rewriting assertions or suppressing authorization/source-version checks.

The existing normal loopback runner executed without a policy denial in this
turn; no fallback launcher or security override was used. All task-owned hosts
and fault proxies were stopped. Corporate SCCM/Falcon restrictions are unchanged.
Package checks cover forbidden files/secrets/personal paths, API AD dependency
closure, Worker runtime closure and per-entry ZIP hashes. Config/web.config,
credentials, key rings and test databases are not deployment ZIP members.

| ID | Final state / owner | Next action and evidence to close |
|---|---|---|
| SDM-01-COMBINED | Implemented and locally verified / delivery owner | Final source and exact payload evidence above; not installed |
| SDM-01-TARGET | Unexecuted corporate acceptance / business + Jira + platform owners | Supply one approved open ServerRequest OR code/source ID/current fingerprint, Jira project/issue-type/mapping, actor and explicit Jira-only approval; review live preview before the single create |
| SDM-01-RELEASE | Matched review ZIPs prepared; promotion not approved / release owner | Resolve exact-branch guard in the existing release procedure through reviewed promotion, without resetting dirty inputs or weakening the rule; obtain target SQL/payload/security review |
| SDM-01-ROLLBACK | Conditional procedure prepared / SQL execution operator + Jira owner | Use current Turkish entry; stop writes and reconcile unknown outcomes, coordinate component versions, retain audit/intents/keys and additive schema |

No selected corporate OR or real preview was fabricated. The existing API
write gates remain the explicit read-only/controlled-write pair with
`SourceCloseEnabled=false`; the exact single-record policy is an additional
restriction, not an inferred role grant. Corporate issue URL must be retained
from the approved Jira interface because no URL field exists in the DTO.
UI/API deploy together; Worker is matched but Jira-only creation executes in
the API and does not require SCCM or service installation. 024 is the catalogue
delta only after target 022/023 verification; nothing here applies SQL.
In Use's three source contracts and Falcon approval remain separate. Security
receipt/approval of either diagnostic or final binaries is not established.

The historical integration steps below are retained for provenance; this final
outcome supersedes their intermediate source/test states.

The active delivery source is now the isolated worktree
`C:\SecureOpsBuild\secure-ops-sdm-integrated-test-20260928`, branch
`feature/sdm-integrated-test-20260928`. Both input worktrees remain untouched.
The shared base is `5c986a96f1f6639e47bf1432a87c3ac55e98054d`; the private
`C:\SecureOpsBuild\validation\sdm-integrated-20260928\input-manifest.json`
identifies the exact 96 backend and 18 UI changed-file snapshots by SHA256.
The sole overlapping README was merged; no DTO field or route was invented.
The audited UI uses the persisted record key, explicit confirmation, unknown-result
stop and existing preview contract. Optional issue URL/replay metadata requested by
UI remain absent; an operator verifies the saved key in the approved Jira UI.

Initial combined checks: Release build zero warnings/errors; unit 1509/1509;
normal integration 281 passed, 61 opt-ins skipped; ResourceSqlTests 49/49 on fresh
`SecureOps_ResourcesV1_SdmMerged20260928C`. TRX files are under the same validation
root's `tests` directory. SQL results overlap the normal-run skips, not extra
normal tests. Mechanical whitespace corrections are restricted to the 25 reported
files already changed by the input snapshots. Final payload and browser evidence
will be recorded below before this candidate is considered locally verified.

| ID | State / owner | Missing input or next action | Evidence |
|---|---|---|---|
| SDM-01-COMBINED | Local integration / delivery owner | Verify combined published browser journey and package manifests | Current worktree and private input manifest |
| SDM-01-TARGET | Corporate acceptance pending / business + Jira + platform owners | One open TEST ServerRequest OR code, numeric source ID, current source fingerprint; approved destination/mapping and authenticated actor, approval reference/expiry/reason | No corporate candidate selected; do not reuse In Use samples |
| SDM-01-RELEASE | Review candidate only / release owner | Numbered release script requires the original combined-delivery branch (line 17); do not switch/reset either dirty source worktree or weaken the guard | Separate matched review candidate, not deployment approval |
| IU-05 | Separate source-contract dependency / source owner | The existing three upload/readback, conditional-field-update and WASAS-approval response contracts remain outstanding | Existing request below; not resent |
| SCCM-SECURITY | Separate permission dependency / Cyber Defense | Detection detail and approval for the exact submitted binary; package receipt/approval not established | Existing sealed diagnostic package, unchanged |

The current Turkish operator entry below contains the conditional Jira-only
configuration/deployment review. It is not an executable target authorization:
corporate selection, mapping, payload approval and SQL baseline are still open.

Integration follow-up: the UI owner continued editing during this run. The
separately hash-recorded `ui-followup-manifest.json` captures that follow-up
without modifying its worktree. It corrects `WorkflowAlreadyInProgress` to
uncertain, explains saved-key readback after a lost response and improves the
loopback fault proxy. The first negative browser run failed at the lost-response
notice because a one-shot drop let a transparent UI HTTP resend through; that
failed evidence remains in `browser-negative`. The successful initial journey
and restart remain scoped to source `06441416b983f1ce97ea308bcdb68548ff8df074`,
not automatically evidence for the corrected source. No ZIP was issued from it.
`CorporateJiraSocketTests` now tests the actual JSON create client with a consumed
body and aborted response, on fresh and reused loopback connections: 2/2 passed,
one create per attempt, unknown/non-retryable outcome. UI-to-API empty-body
resend and API-to-Jira JSON create are different transports; neither grants
corporate acceptance. The new test initially hit IDE0008 and was corrected
before its successful run.

A second combined browser failure exposed first-load reentrancy. The new
`ConcurrentInitialReads_PublishOneChange_NotOnePerWaiter` test failed before the
fix (expected one access change, observed two). Cached access waiters now do not
publish another change; explicit refresh still publishes. OR list/detail register
their change listeners after receiving the initial snapshot. This is a bounded
integration defect correction, not a new access policy. Source `3a8b637` ZIPs
are retained as superseded review outputs, not the final candidate; new source
and hashes are required after this correction. No corporate writes occurred.

### In Use SQL regression correction, 2026-09-28

The audit-rollback assertion in
`InUse_RoundTripConcurrencyStaleDraftAndAuditRollback` remains whole-record
equality: a failed audited transaction must not change the record. The later
source-unavailable refresh is a successful local state transition, so its old
whole-record equality assertion was obsolete. It now checks `Version` advances
once and `SourceObservationMissing=true`, while `LastSeenAt`,
`ReviewSourceVersion`, draft, review status, assignee, OR identity and server
answer fields remain as previously saved. `LastSuccessfulAt` also remains
unchanged. This matches `InUseState.RetainUnobserved`; no product code or
archived bytes were changed.

Release integration-project build: zero warnings/errors. The focused isolated
SQL case passed 1/1. A full 49-case ResourceSqlTests rerun against the reused
`SecureOps_ResourcesV1_SdmOnly20260928A` fixture passed 47/49: two reporter
tests could not find newly created reviewers in `AssigneesAsync`'s bounded first
50 after prior test runs had accumulated eligible reviewers. This was not
reported as a passing run. The protected fresh-database harness then created
`SecureOps_ResourcesV1_SdmFresh20260928B`, applied test-only 001-024, and
passed the full ResourceSqlTests suite 49/49 with zero skipped. Both databases
are retained locally for inspection; neither is a corporate target. No target
SQL, Jira, source mutation, deployment, flag change or package was performed.

The SDM-01 controlled TEST sequence remains the Jira-only procedure in
`docs/post-rc626-continuation-tr.md`; it requires an approved exact
ServerRequest OR, destination/mapping and actor. This local SQL result does not
establish corporate Jira or source-state acceptance. IU-05's three separate
source-contract blockers remain unchanged.

### SDM-01 Jira-only ServerRequest review, 2026-09-28

The current checkout retains the In Use 4464 correction and its prior 252/252
In Use unit result. IU-05 has exactly three activation blockers, not an
operational corporate transport: (1) attachment identity/content readback and
lost-response reconciliation, (2) keyed 4463/4464 target/value/version plus
source-supported conditional update/conflict semantics, and (3) the unique
eligible WASAS activity update's matched-count/acknowledgement/conflict semantics.
The existing request builders do not close these contracts; the earlier bounded
source-owner request remains the single request and was not resent here.

New local evidence: Release integration-project build succeeded with zero
warnings/errors. Focused Jira transfer/corporate adapter/pilot unit tests passed
125/125. Four isolated LocalDB SDM tests passed 4/4 using the test-owned
`SecureOps_ResourcesV1_SdmOnly20260928A` database (001-024 fixture schema,
not the corporate target). A wider ResourceSqlTests run on that database was
48 passed, 1 failed: the unchanged In Use test
`InUse_RoundTripConcurrencyStaleDraftAndAuditRollback` expected the entire
record to remain unchanged after a source-unavailable refresh, but the retained
E-08 behavior set `SourceObservationMissing=true` and advanced Version from 4
to 5. This SDM task did not alter that In Use behavior or rewrite its assertion;
the wider run is not reported as passing. No browser or corporate TEST acceptance
was executed.

For SDM-01, exact-record ServerRequest policy, reviewed Jira field preview,
source freshness, SQL transfer/command uniqueness, persisted Jira key before any
optional source step, and Jira-only `SourceCloseRequested=false` are already
implemented. Corporate Jira now independently rejects a ServerRetirement draft
as it already did SoftwareInstallation. A retry blocked after an unknown Jira
outcome no longer increments `RetryCount` or emits `WorkflowRetried`; an
interrupted `CreatingJira` still fails at the claim boundary. Neither state
permits a second create. Local focused results and the controlled TEST procedure
are in the current SDM operator handoff. No corporate Jira/source write,
configuration activation, deployment or SQL application was performed. One
selected TEST ServerRequest OR, approved Jira destination/mapping, authenticated
actor and explicit Jira-only intent remain required for corporate acceptance.

### IU-05 supported wire correction, 2026-09-23

The retained legacy script uses `DCM_DynamicCaseProperty` update for 4463/4464,
`uploadattachment` for the OR XLSX, and a pending `BPM_Actvty` query followed by
`m_status=4` update. The application already has one-attempt request builders for
all three, exact archived-byte/hash and OR binding for upload, a stricter
single-activity mutation predicate, durable no-retry evidence, and a bounded
read-only OR/pending-activity collector. The script's positional environment
value is not the accepted OR-wide rule. The standalone mutation client now
checks 4464 against the reviewed `InUseRequiredFields.Environment` result:
any PROD server proposes PROD; all known non-production servers propose TEST;
missing/unknown values block the request. Synthetic unit checks cover mixed
DEV/UAT, mixed PROD/DEV, unknown environment, and a mismatched proposal. The
focused `InUseExecutionContractTests` run passed 29/29; this is local request
behavior, not corporate field acceptance. Existing IU-05 transport registration
and target flags remain blocked.

Source-owner input is limited to part A of the existing Turkish handoff:
attachment identity/content readback and lost-response reconciliation;
4463/4464 keyed target/value/version plus actual conditional-update/conflict
facility and accepted 4464 wire value; unique eligible BPM update matched-count
and acknowledgement/conflict semantics. Existing read-only collector may show
the selected OR and pending activity candidate, but cannot discover an
undocumented attachment read operation or prove atomic update from a GET.
Whole-OR closure, next-team tracking and optional OR date ordering are not
IU-05 transport prerequisites. No corporate read/write, flag activation,
deployment or package creation was performed in this correction.

### Current owner correction: optional date sorting, 2026-09-23

IU-07-LIST chronological ordering is **deferred/optional**, not a delivery blocker
or required source-owner response. Default is stable Code/ordinal record ID before
paging, labelled `OR numarasina gore`. UI date choices are disabled with an
explanation and old date-sort links normalize to code. The implemented explicit
oldest/newest backend path and date tests are preserved for later activation.
This overrides date-default/date-input-required wording in historical entries below.

Inspected existing `_sourceSelects`, In Use discovery and parser, retained keyed
response regression examples in `EnterpriseAdapterContractTests`, the retained
legacy In Use script OR query (323-334), and completion collector root projection.
None establishes a parent creation field/type/timezone. `_activitySelects` includes
`m_created_dt` on BPM_Actvty, not the parent OR; it is not reused. Parser CreatedAt
remains null. No new field or timestamp fallback was invented.

One execution of these same projections would not resolve the missing timestamp:
they do not select it. Do not run the Falcon-held package, expand selectors or start
another diagnostic just for sorting. Optional read-only evidence review, only if
an already retained OR QueryResult from the established query exists: inspect one
selected parent's returned keys and JSON types in the approved server session;
retain only a candidate timestamp/type/offset and a consistent alias for the parent
ID, removing names, descriptions, SessionID, Authorization and all credentials.
Compare its documented semantics to the parent's initial creation, not activity or
update time. No existing supporting field means stop and keep code ordering; no
source-owner response is required to ship. This procedure was not executed here.

IU-05 upload/field/approval work remains independent. Reinspection confirms the
existing exact-byte attachment builder, keyed legacy property request builder,
unique-WASAS mutation predicate and acknowledged/rejected/unknown response handling.
Genuine remaining transport contracts: authoritative attachment identity/content
and lost-response reconciliation; 4463/4464 keyed values/version and supported
conditional-update/conflict semantics; unique eligible activity response/matched
count semantics. No overall-OR closure, creation date or next-team requirement.
The minimal status read contract remains separate from these mutation contracts.
No unsupported transport was enabled and no corporate operation was performed.

Local result: `0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.7f3f3bb077d9`,
product SHA-256 `7F3F3BB077D942F05A3E5E4274BEE58CABE9AC04884F362BBB62C1AAB79BC900`.
Evidence: `C:\SecureOpsBuild\validation\inuse-code-order-20260923`.
Release solution build: zero warnings/errors. `tests/code-order-unit.trx`: 10
focused cases passed (default/query validation, old-link fallback and URL flow),
compiled before final version stamping with the same behavior. Final versioned
build `tests/code-order-integration.trx`: 10 passed, zero failed/skipped, including
memory/SQL code ordering before pagination, preserved explicit date sorting,
activity tracking, approval acknowledgement/unknown/no-retry, source-drift fences,
API authorization and OpenAPI comparison. OpenAPI generation is separate evidence,
not an extra acceptance count. Only retained isolated synthetic LocalDB was used;
no migrations. Prior 244-unit and other historical suites were not repeated.
Source snapshot/input hashes, six nondeployable DLL references and manifests are
retained by the same private recorder. No new package or installed-state claim.
Browser/200% acceptance remains unexecuted; Falcon and corporate write restrictions
are unchanged. E-08, archive fix and all earlier evidence roots are preserved.

### Earlier implementation closeout: 2026-09-23

Continued the existing list implementation without resetting work. HEAD/branch
remain as below. Current product identity is
`0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.c4218a49de92`;
input SHA-256 `C4218A49DE92FF5931DADF583CAA8DF960994B37F812B624FAE98816D34D1DE5`.
The IU-07-LIST / IU-05-STATUS matrix below remains current. Final correction makes
SQL status comparison exact (case and trailing-space sensitive) and provenance
whitespace checks match .NET, including tabs/Unicode spaces. Invalid date shapes
are rejected consistently. Expanded memory/SQL fixtures cover these cases.

New evidence is `C:\SecureOpsBuild\validation\inuse-list-20260923`:
`build-final.log` has zero warnings/errors; `tests/inuse-integration-final.trx`
has 10 passed, zero failed/skipped, on this source with the same six isolated SQL,
two memory, one API and one OpenAPI gates listed below. No migrations or corporate
calls. The initial build's one test-only explicit-type analyzer error was corrected
and retained in `build-initial.log`. The prior 244 passing unit tests remain
historical evidence in the 20260922 root; that unaffected suite was not rerun.
Do not add overlapping runs to the final count. `source-identity.json`, all 644
`product-inputs.json` entries, `source-at-closeout`, six DLL hashes in
`build-reference.json`, `changed-files.json`, cumulative accounting and
`evidence-files.sha256` identify the local result, not a deployable release.
The identical private recorder is reused in this separate root, counted in
`private-scaffolding.json`; it is not new product code. Prior roots/sealed artifacts
and the archive fix remain unchanged. No installed-state or browser acceptance
claim; the precise source-owner request remains part B of the linked Turkish guide.

### 2026-09-22 implementation checkpoint: In Use list

Same delivery checkout and branch; HEAD remains
`5c986a96f1f6639e47bf1432a87c3ac55e98054d`. All inherited work is retained,
including the byte-identical archive-integrity fix. New uncommitted product identity:
`0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.e6c1442c8a6f`.
Product input SHA-256: `E6C1442C8A6FEF48824F1BB0B3E303163FBA3F745E0D133820D0F7DBB9CE595A`.
No commit, release ZIP, sealed-artifact modification, deployment or target action.
E-08 and wip.4179fca3c22c remain separate retained evidence, not this build.

| ID | Status / classification / owner | Change, verification and exact remaining input |
| --- | --- | --- |
| IU-07-LIST | Implemented and locally tested / engineering | Previously SQL and memory ordered by Code then Id. Both now sort by source Creation before paging, oldest default or newest selectable, unknown/invalid/future/offset-free dates last, binary Code + ordinal Id ties. UI date column shows UTC or explicit unavailable status; sort survives page/detail/back/refresh. No FirstSeenAt/LastSeenAt substitution. Real parser currently sets CreatedAt=null and does not populate InUseSource.Creation: source owner must supply the parent-OR creation select/key, type, timezone and sanitized keyed response. Until then affected target dates are explicitly unknown, with deterministic code/Id fallback. |
| IU-05-STATUS | Local integration tested; corporate read contract missing / workflow owner | Existing review/tracking reused; pending/verification views added with provenance checks. Source-backed completion moves an externally completed WASAS activity to tracking while overall OR remains Open and saved answers remain current. Missing/failed discoveries retain answers and prior observation, set SourceObservationMissing, never infer completion. Durable active/uncertain executions stay verification-pending; manual attestation does not promote completion. Missing observation also blocks the next queued mutation; existing eligibility, authorization, archive and duplicate guards remain. Need part B of the Turkish handoff, not next-team/timeline. |
| IU-05 | Existing real transport gaps unchanged / source API and workflow owner | Part A of the existing handoff: authoritative attachment content/lost-response reconciliation, keyed 4463/4464 values/version plus actual conditional-update/conflict facility, unique eligible WASAS approval response semantics. No invented endpoint, source field, status value or target activation. |
| IU-07-UI | Code and component/query checks complete; browser acceptance unexecuted / authorized UI runner | Responsive date column and status filters implemented. Native desktop/mobile/themes/focus/200% checks remain under the recorded runner restriction; no host launch, workaround or browser screenshot claim. |

New evidence root: `C:\SecureOpsBuild\validation\inuse-list-20260922`.
`source-identity.json` and `product-inputs.json` identify all 644 product inputs;
`build-reference.json` hashes six own DLLs in `build-reference` (not a runnable or
deployable payload). `changed-files.json`, `cumulative-counts.json` and
`test-results.json` retain scope and execution evidence. Private recorder line
accounting is separate in `private-scaffolding.json`.

Newly executed final checks against that build:
- `build-final.log`: Release solution build, zero warnings/errors.
- `tests/inuse-unit-final.trx`: 244 passed, zero failed/skipped. Includes ordering
  query validation, activity provenance/fence labels, URL preservation, review
  retention and existing archive/export safeguards; not browser acceptance.
- `tests/inuse-integration-final.trx`: 10 passed, zero failed/skipped: six SQL
  ordering/status/fence/search cases, two memory parity cases, one in-process API
  authorization/binding case and one OpenAPI comparison without regeneration.
  SQL ran only on retained synthetic LocalDB `SecureOps_ResourcesV1_activityfinal20260922`;
  no migrations, corporate database or external provider operations.
- First build found six test-only explicit-type analyzer errors, corrected. The
  first unit run had 241 pass/three obsolete whole-record equality failures after
  the new missing-observation marker; assertions now preserve all other data and
  explicitly verify the one versioned marker change. Initial evidence retained in
  `tests/inuse-unit.trx`; final suite above supersedes it. Earlier intermediate
  passes and OpenAPI generation are not added to these totals.

Corporate reconciliation remains unavailable; fixtures prove only the local
contract path. Falcon/SCM/browser/source/SMTP and selected business acceptance
gates below remain open with their existing owners. The v19 comparison is complete
and was neither repeated nor requested. Next-team/timeline remain deferred.

### Historical guidance-only scope checkpoint after wip.4179fca3c22c

Owner priority: retain only the source-backed WASAS activity status needed to
separate actionable, completed and verification-pending records. Next-team details
and a comprehensive workflow timeline are deferred, not completion/release gates.
Existing observations and historical E-07/E-08 evidence remain intact; their broader
tracking wording is historical where it conflicts with this narrower scope.

| ID | Status / owner | Required next action and boundary |
| --- | --- | --- |
| IU-05 | Required mutation contracts missing / source API and workflow owner | Attachment identity/content/lost-response reconciliation; keyed 4463/4464 values and actual conditional-update/conflict contract; unique eligible WASAS approval and response semantics. Existing authorization, required attachment, concurrency and duplicate fences remain mandatory. |
| IU-05-STATUS | Minimal read contract missing / workflow owner, then engineering | Separate bounded query for the selected OR/WASAS activity identity and status; documented pending/completed meanings, keyed masked examples and zero/multiple/failed-read interpretation. Fresh eligibility plus existing guards permits action; authoritative WASAS completion permits completed tracking; acknowledgement, ambiguous result or unavailable read remains verification-pending, never automatically retried or inferred completed. Manual confirmation retains actor/time and its manual label. This is not mandatory automatic post-approval readback. |
| IU-07-NEXT | Deferred / product owner | Next-team lookup, next-stage details and comprehensive timeline are outside current acceptance. No source-owner request or implementation gate for them in this delivery. Existing evidence is preserved, not expanded. |

The v19 byte/four-server/12-answer investigation remains complete; no sample was
requested or re-inspected. Product inputs still match wip.4179fca3c22c (643 files).
This continuation edits guidance only: IU-05 handoff, controlled acceptance item 6,
collector limits and ADR-0020. No build, suite, package, target action or new source
contract is claimed. Overall OR closure is not an In Use approval prerequisite.

### E-08 follow-up: supplied archive and workbook, 2026-09-22

Active checkout/branch and HEAD 5c986a96f1f6639e47bf1432a87c3ac55e98054d
were rechecked; all 643 sealed E-08 product inputs initially matched. Existing
dirty work was retained. No commit, reset, deployment, target query, SQL, mail,
service start or flag change. E-05/E-06/E-07/E-08 remain sealed; no new package.

| ID | Status / classification / owner | Evidence and exact next action |
| --- | --- | --- |
| IU-07-D | Supplied-sample comparison complete locally; engineering | All three original envelopes pass size/SHA checks; 2,757 XLSX cells equal their JSON Sheets. Downloaded v19 is exactly the archived 7,653 bytes, source version 4, four servers and 12 saved answers. Original bytes/hashes unchanged. Current browser/Excel desktop acceptance is separate, not reopened as a missing sample. |
| E-08-ARCH-01 | Code defect corrected locally; engineering | Corrupt envelopes were blocked but InvalidDataException bypassed the archive-specific error mapping, causing generic PersistenceUnavailable. Include it explicitly in the filter; six synthetic corruption cases check safe error, no callback, no content and no rewrite. E-08 binaries remain unchanged; the correction is uncommitted source plus the separate build below. |
| IU-05 | Source-contract gap; Turuncu Hat API/workflow owner | Known exact-byte upload/property/activity request builders and durable guards already exist. Need authoritative attachment content/reconciliation, 4463/4464 keyed value/version and real conditional-update/conflict semantics, unique activity update acknowledgement/matched-count. Forward part A of the current Turkish handoff; part B is the separate IU-05-STATUS query. No final whole-OR readback prerequisite. |
| IU-07-C | Tracking implementation retained; minimal read contract pending, workflow owner | Collector selects pending status 1/group 68 only; legacy script selects id, then writes m_status=4. Zero pending rows cannot be promoted to completion. Remaining source-backed status is scoped to IU-05-STATUS; next-team/timeline work is deferred under IU-07-NEXT. |
| E-08-UI / QA-01 | Unexecuted test, runner restriction; authorized UI runner | Desktop/mobile/themes/focus/touch/native 200% against matched payload, using existing procedure. No host-policy retry, alternative launcher, synthetic screenshot or browser acceptance claim. |
| OPS-02 | Unexecuted SCM acceptance and corporate permission; Windows runner/operations | Prior local Test/content-root/lock/log/shutdown checks retained. Actual service start/stop/logoff/crash recovery and console handover need the authorized isolated SCM runner, then target permission; no service installed here. |
| OCO-01 / E-06-SEC-01 | Corporate execution permission; Cyber Defense + SCCM owner | Falcon stop before JSON remains unexplained. Need detection details and reviewed binary-specific permission, then one selected device/service/date journey. No false-positive claim or bypass; queue readiness not retested as connectivity. |
| MAIL-01 / QA-02 | Unexecuted payload journey and target inputs; authorized runner + messaging owner | Existing preparation/restart safeguards and separate maintenance-end/restart fields retained. Source/MIME/process/SMTP gates remain separate; approved relay, selected immutable self-test and inbox observation still pending. Sending stays disabled. |
| SDM-01 / RPT-01 | Corporate acceptance/input gap; source/Jira owners + operator | Retain implemented ServerRequest Jira-only guards and persisted-outcome reporting. Select exact destination/actor/intent and compare the same reporting cut. Local intent/manual attestation is not remote closure; no corporate transfer performed. |
| DB-01 / REL-01 | Target schema and release gate; SQL execution operator/release owner | Catalogue 024 objects are not visible in the approved TEST DB; queried 022/023 contract matched semantically. Effective API rights remain open. 022/023 not replayed. Keep E-08 sealed; do not make another ZIP for unchanged blockers. |

Private evidence: `C:\SecureOpsBuild\validation\inuse-archive-20260922`.
`archive-comparison.json` records original/raw/XLSX hashes and sheet-level checks;
no corporate samples were added to repository fixtures. The supplied v19 workbook
SHA-256 is `6B7DB94FE5DA981093B0304A1378CF2CC2D64725EDD4E9F1E72530B240368A33`;
envelope SHA-256 is `5A9EB125D1F9E9AE1E6C8DD6CE8B43C8ED03AA80388705D1DD193DA6795AE337`.
It has the historical six-sheet order, no frozen panes/widths/filter/print setup,
22 NMS columns and 29 server rows. Both checklists are blank, not completed checks.
Each server has InternetOut=No, InternetIn=No, Microsegmented=Yes and raw DEV.
PreparedBy/ReviewedBy are application GUIDs, not SIDs; the envelope has a frozen
preparer label but no account. TechnicalCreator is Unknown/Unverified, not another
actor to resolve. Per-answer origin evidence retains two distinct application
actors; current draft reviewer/preparer identity must not replace those origins.
The archived workbook is not the E-08 four-sheet exporter; historical re-download
preserves its six sheets and identifiers. Only new versions get the new layout
and trusted name/account snapshots. Local evidence does not reverify source facts.

New private compatibility check directly loads actual sealed E-08 assemblies,
then the patched assemblies, against copies of all three supplied envelopes.
Both preserve bytes/preparer/time, derive the original OR/version download name,
project catalogue host counts (2/4/4), and deny a false authorization callback.
The deliberately damaged copies expose E-08's unclassified InvalidDataException;
patched copies return InUseArchiveIntegrityFailed before the callback. Originals
were never mutated. This is library compatibility, not an authenticated browser
or target API test. Initial private runs are retained: storage-under-current-root
was correctly rejected; the following run exposed the error-category defect.

New product build: `0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.4179fca3c22c`;
product-input SHA-256 `4179FCA3C22CA73CCF38D6D22D4954F440123C827494CAAFD4BE547D6D4A99D8`.
`source-identity.json`/`product-inputs.json` identify the dirty source separately
from E-08. Release solution build: zero warnings/errors. Newly executed focused
tests: `tests/inuse-focused.trx` 204 passed, including six new corruption cases;
`tests/inuse-api-focused.trx` one in-process API test passed. No full suite rerun,
SQL run, process/browser or corporate test; historical totals remain separate.
`e08-compatibility-observed/result.json` and `patched-compatibility-observed/result.json`
record actual assembly hashes and the different error categories. This correction
is locally tested source/build only, not a tested delivery payload or installation.

### WASAS activity clarification, 2026-09-22 (IU-07 / E-08)

This section supersedes whole-OR closure terminology for new In Use submissions.
The owner-observed completed WASAS activity followed by a pending next team and
overall Open process is a valid advancement, not a failed OR closure. Screenshots
are business evidence, not an API response or proof of conditional-update support.
No corporate source request, SQL change, flag activation, mail or deployment ran.

| ID | Status and owner | Exact next action / evidence |
| --- | --- | --- |
| IU-07-A | Implemented locally; engineering | New frozen WasasActivityManual intent approves only the eligible WASAS activity. Acknowledged/unknown stops before OR readback, without retry. Verified BPM completion is distinct from verified Closure. Manual events have a new activity-specific code; historical E-07 closure attestations retain their meaning. Active duplicate fence remains after verified activity completion. |
| IU-07-B | Implemented locally; engineering | Full source hash/version still fences execution. Independent review version retains unchanged answers after title/requester/workflow changes. Canonical field/server ordering avoids incidental invalidation. Actual server/context changes remain stale with before/after details and explicit re-review. |
| IU-07-C | Implemented; browser acceptance pending, authorized UI runner | One save meaning, prominent proposal acknowledgement, separate answer-copy preview, visible RFC requester and server/RFC columns, separate WASAS/OR/stage labels. SQL count/page tracking uses verified activity evidence, never local/manual confirmation. Current corporate read adapter does not supply next-stage fields; UI says unavailable. |
| IU-07-D | Implemented; supplied workbook comparison now complete, engineering | E-08 four-server fixture/export checks preserved. New follow-up above verifies the supplied historical v19 XLSX and all three envelopes. New-layout browser/Excel acceptance remains distinct; no longer request the missing sample. |
| IU-05 | External transport contract missing; source owner | Provide one masked example each: attachment parent/ID/content or hash readback and lost-upload reconciliation; keyed dynamic case 4463/4464 target/version/conditional conflict; unique eligible WASAS BPM update matched-count/conflict/acknowledgement. Confirm 4464 TEST/PROD wire representation: legacy script copies a positional raw environment, while approved business rule is any PROD -> PROD, otherwise known non-production -> TEST. Do not request a final OR closure field as an activation prerequisite. |
| E-08-UI | Not executed; authorized UI runner | Actual desktop/mobile/themes/keyboard/touch/200% browser zoom against matched new payload. HtmlRenderer is component coverage, not browser acceptance. Recorded host restriction remains; no alternative launcher/port used. |
| E-06-SEC-01 | External hold unchanged; Cyber Defense/operator | Original diagnostic killed before JSON. Application package not reported supplied or approved. Obtain detection details/decision; E-05 permission would not authorize E-08 binaries. |

Identity inspection: the supplied original v13 JSON SHA-256 still matches
2FDB1ABB...0E5CD828A. Its Provenance ReviewedBy/PreparedBy fields are application
GUIDs, not Windows SIDs. TechnicalCreator is separate source evidence; it is not
relabelled as reviewer/preparer/requester/approver. At E-08 seal the v19 workbook
was absent; the subsequent supplied-file comparison above closes that evidence gap.
New reports resolve the exact saved reviewer GUID through the existing trusted
application repository and freeze name/account beside stable technical IDs. The
current downloader never replaces the reviewer. No directory search, role grant
or source-display-name identity mapping was introduced. Missing identity uses the
known account, then `Kullanıcı adı çözümlenemedi`.

E-08 remains separate from sealed E-05/E-06/E-07. No new schema delta: existing
022 execution/event reads support tracking; 024 catalogue migration remains the
reviewed successor prerequisite, not target-verified installed. API/UI/Worker must
be upgraded together with writes stopped; old serializers can drop new metadata.
The exact new source/artifact/test identities are recorded in the E-08 closeout
below after verification. Prior totals are historical, not added to new runs.

#### E-08 local closeout and artifact identity

One review payload set, not a numbered release or installation authorization:
`C:\SecureOpsBuild\delivery-review\2026-09-22-inuse-wasas-activity`.
Version `0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.f530ba994f6c`.
Product-input SHA-256
`F530BA994F6CB13029862AB2C284C9C4898806155C330CA5C34E49BE84330F10`.
HEAD remains documentation closeout `5c986a96f1f6639e47bf1432a87c3ac55e98054d`,
last committed product `c12abf29a60d2087728cd1e34879c0360a57c1c9`; current work is
uncommitted. `source/product-inputs.json` and `source/identity.json` identify the
build; `source/e08-changes.json`, byte-preserved changed files and closeout patch
identify subsequent documentation separately. Nothing was committed or deployed.

| Artifact under `packages/` | Files | SHA-256 |
| --- | ---: | --- |
| secureops-api-TEST-review.zip | 525 | `3815BDE91C956196FC7FA4E9192B460FC34D39404718799CC0C78FF1DABD8719` |
| secureops-ui-TEST-review.zip | 542 | `4EC046927091E665E8F8B3BEBE2D14B9128D6183CA94A5370D91DC7064DB2669` |
| secureops-worker-TEST-review.zip | 531 | `3416504A7A8CA8F43C94522EAC3DE36B6FBFA49CECD4674A5BEBE68B6E1D3553` |
| secureops-database-024-review.zip | review inventory | `CEFDE6D7219536E75D98AFAB3ADA15F0EB5AA960899A1E2A4CC1E47E62EA37D9` |

Newly executed, final matched build: Release build 0 warnings/errors; formatter
verification passed; 1,447 unit tests and 279 normal integration passes with 59
opt-ins skipped. Separate isolated SQL run: 16 passed, 0 skipped. These 16 match
normal-run opt-in names; do not add overlapping historical/focused totals.
Exact TRX paths relative to the review root:

- `evidence/unit/dmtak_DEMET_2026-09-22_19_51_14.trx`
- `evidence/integration/dmtak_DEMET_2026-09-22_19_51_20.trx`
- `evidence/sql-regression/dmtak_DEMET_2026-09-22_19_53_20.trx`

SQL ran only on `(localdb)\SecureOpsResourcesV1`, fresh test-owned database
`SecureOps_ResourcesV1_activityfinal20260922`, with fixture 001-024 setup. Never
apply that fixture command on the corporate SQL server. Coverage includes
acknowledgement/timeout/rejection/manual attribution/restart/no duplicate,
activity-verified tracking and reporting without OR closure, exact catalogue
paging/metadata and historical archive preservation, trusted identity labels,
source drift, revocation and audit rollback. Unit coverage includes unchanged
answers across parent/stage/inventory STATUS changes versus changed environment,
four-server/12-answer export, fallback identities and component rendering.

Eight updated browser scripts passed `node --check` only. Current browser,
200%-zoom, source/MIME/process/SMTP and corporate journeys remain unexecuted.
No before/after browser screenshots were produced. Frozen panes/filter/print
structure was inspected as XLSX XML, not by claiming an Excel desktop run.
`evidence/review-inputs-and-syntax.json` retains original archive/XLSX integrity
checks without copying corporate samples to fixtures. The four-server workbook
was unavailable at that seal; the new follow-up above compares it without altering E-08.

The preflight builds under `validation/inuse-activity-20260922/preflight-*` were
not packaged or issued. A review corrected inventory STATUS review scope and
primary-action policy gating; an exact-input guard then rejected a concurrent
line-ending normalization. The final build began after formatting ended, with
the new hash above. Prior logs remain retained, not counted as final evidence.
E-05's 531 payload files and E-06/E-07 sealed manifests were reverified unchanged.
`security/worker-comparison.json` distinguishes that diagnostic from this Worker.
Neither payload is reported submitted to or approved by Cyber Defense.

| Surface | Implemented / local evidence | Exact-payload journey | Installed / target configured / corporate / team availability |
| --- | --- | --- | --- |
| In Use review/export/list/identity | Unit, SQL and matching staged build verified | Browser/zoom pending | New behavior not installed; prior rc6.26 observation unchanged |
| WASAS activity approval | Supported legacy request shapes, durable intent/manual/verified categories and guards tested with fixtures | Real attachment/conditional property/BPM response contract pending IU-05 | Disabled; no corporate approval executed |
| Reporting | Verified activity is an OR-deduplicated metric, separate from acknowledgement and Closure/Verified | Corporate reconciliation pending | New metric not installed; no inferred closure or manual success |
| Worker/OCO/mail/SDM | Prior independent fixes preserved in the same matched components | Prior external/security/process gates remain open | No activation, mail, service install or target change in E-08 |

`New-PairedTestRelease.ps1:19` still rejects a dirty tree. Reviewed component ZIPs
are the fullest permitted local candidate, not an exemption from final numbering,
security or acceptance gates. Operator next action: obtain the single masked IU-05
contract response from the source owner; no selected corporate OR is required to
finish local work and no final OR closure contract is requested for this action.

### Historical manual OR verification, 2026-09-22 (IU-06 / sealed E-07)

Owner explicitly defers automatic post-close OR readback. That decision removes
readback alone as a delivery blocker, not attachment content proof, eligibility,
concurrency or authorization. Current source remains 5c986a9 plus preserved dirty
changes; the sealed E-06 review ZIPs and E-05 diagnostic must not be overwritten.
This continuation is not installed behavior or a new numbered release.

Re-inspected private legacy script lines 701-730; its raw SHA-256 matches the
retained BB07673B...C0D704 reference. Supported operation is BPM_Actvty update,
m_status=4 after the uniquely eligible activity lookup. Existing wire adapter
keeps the OR/model/status/group predicate and single-attempt authenticated path.
Neither the legacy script nor its acknowledgement establishes terminal OR state.
The script was read, not executed. No source record URL is established.

New intents freeze Manual verification mode. After acknowledged BPM, existing SQL
state Unconfirmed stops dispatch before automatic Closure readback. Unknown/timeouts
stop without retry; explicit Success=false is failure, contradictory/malformed
responses remain Unknown. Old intents retain SourceReadback semantics and a changed
execution fingerprint prevents silent adoption. Closure/Verified remains the only
system-verified completed result. Existing attachment/content, exact archive,
initiator, source/review checks and duplicate fences remain in place.

UI shows selected OR, saved server count/report version, exact archived hash and
source attachment name before submission. It distinguishes the requested Turkish
acknowledgement/unknown messages. Separate explicit manual confirmation records
current authorized confirmer, label and server UTC time in append-only events/audit.
It rechecks operation/OR/revision and access transactionally. It never sends another
source request, releases the active fence or increments verified closure counts.
No new migration/grant/feature flag: 022 execution tables already support these
states/events; existing successor catalogue delta 024 is unchanged.

IU-05 still needs one bounded source-owner response: keyed dynamic-case target and
real conditional-update/version/conflict contract for 4463/4464; authoritative
attachment parent/ID/content/hash lookup and lost-upload reconciliation; uniquely
eligible BPM target/concurrency response semantics. Automatic terminal OR readback
is deferred by decision, not requested as an activation prerequisite for IU-06.
Until the remaining preconditions can be implemented, the real completion transport
stays blocked. Flags, manual attestation and a selected OR cannot manufacture them.
Corporate closure, final browser/mouse/touch/200%-zoom acceptance and new-payload
security permission remain pending. Do not bypass either recorded host/Falcon hold.

E-07 newly executed local evidence and concrete artifacts:
`C:\SecureOpsBuild\delivery-review\2026-09-22-inuse-manual-verification`.
Matched API/UI/Worker ProductVersion:
`0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.9535a0bb7c48`.
Product-input SHA-256:
`9535A0BB7C4815541DA6AC34FB9BDEDA665A8AAFD524F55F56813CA8102135B7`.
HEAD is unchanged; this is explicitly uncommitted source, not a product commit.
source/identity.json, product-inputs.json, patches and changed-file copies bind it;
source/e07-changes.json separates this task from the preserved E-06 changes.

| Newly executed check | Result / exact evidence relative to E-07 root |
|---|---|
| Full format and matched Release build | Passed; 0 warnings/errors, evidence/format.log and build.log |
| Unit regression | 1,434 passed; evidence/unit/dmtak_DEMET_2026-09-22_16_20_04.trx |
| Normal integration | 279 passed, 58 opt-ins skipped; evidence/integration/dmtak_DEMET_2026-09-22_16_20_09.trx |
| Relevant isolated SQL execution/manual verification/reporting regression | 10 passed, none skipped; evidence/sql-regression/dmtak_DEMET_2026-09-22_16_21_03.trx |
| Publish/ZIP integrity | API 525, UI 542, Worker 531 members; tested entry/Shared/Infrastructure hashes match publishes and common dependencies match across components |
| Preserved artifacts | 86 sealed E-06 files and 531 E-05 files match original manifests; evidence/preserved-artifacts.json; not target integrity verification |

The 10 SQL cases include two new opt-ins skipped in the normal run. Do not add
earlier focused 30-unit/2-SQL or E-06 totals. SQL used fresh test-only database
SecureOps_ResourcesV1_manual20260922a on the guarded per-user LocalDB facility;
existing harness applied 001-024 there only. No corporate SQL was run. New cases
cover acknowledgement, lost response after synthetic closure, explicit rejection,
no final readback/replay, original initiator versus confirmer, stale/wrong OR,
concurrent attestations, revocation, audit rollback, and no InUse.Closed report fact.
Existing execution recovery, source-drift, attachment, lifecycle and report tests
remain passing. Synthetic component rendering and API authorization/OpenAPI passed;
they are not mouse/touch/focus/200%-zoom or corporate acceptance.

| E-07 review ZIP | SHA-256 |
|---|---|
| packages/secureops-api-TEST-review.zip | D21EEAA0BB588A98DAE75BBB828682AB4CE162C0F8784A8B2120B1E675DFA139 |
| packages/secureops-ui-TEST-review.zip | 556553E240EF10D15265F11D5F5218B34844EECE2F43987E6E57A97A13A18F2E |
| packages/secureops-worker-TEST-review.zip | 1D98238B5F19884048E1137200043E09943C7F9C6C0415BF737149BA259620BB |
| packages/secureops-database-024-review.zip | 0E6DD162230C76ACA0A825B000538474170001FCD5B3964D24D8798460A3827C |

DBA archive carries the unchanged applicable 024 scripts plus an E-07 note that
manual verification adds no SQL. API/UI ZIPs exclude server-owned configuration;
Worker has no configuration files. No secret or target flag change. These are
unnumbered review artifacts, not a replacement for sealed E-06 or install approval.
The dirty-tree numbered-release rule remains; final security/browser/target gates
are not waived. Backend/UI/contracts/tests and linked guidance are the only new
scope. Private profile evidence and all previous completed modules are preserved.

### Consolidated review candidate and security hold (E-06 / SEC-01)

Owner update received 2026-09-21: SCCM console installation is reported, but
module compatibility and collection access are unverified. The separate SCCM
diagnostic was run under the normal Worker identity, Test environment and installed
configuration root. It terminated before JSON. Cyber Defense confirmed a Falcon
process-killed event. The security/execution-permission request is open; the
application package has NOT been supplied to the security team. Detection ID,
technical detection details and approval decision are missing. This is an external
execution dependency, not a false-positive conclusion, SCCM read success, or proof
that the earlier collection failure had the same cause. No bypass via launcher,
identity, host, path, policy, exclusion or changed binary is authorized.

Preserve the E-05 diagnostic tree and its original file hashes. A consolidated
Worker has a separate build identity and hashes even when code overlaps. Approval
for the diagnostic does not approve another payload or service mode. Neither
payload is recorded as delivered to or approved by security. Operator target hashes
must be compared with the preserved local manifest; target equivalence is not
established by the reported command alone. Do not rerun the E-05 command pending
an explicit decision covering its exact bytes and execution context.

Current HEAD remains 5c986a96f1f6639e47bf1432a87c3ac55e98054d, last committed
product c12abf2; inherited dirty work is preserved. The owner authorizes consolidated
local review artifacts now, without waiving final release gates. The numbered
tool's `New-PairedTestRelease.ps1:19` dirty-tree rejection remains unchanged.
Prepare unnumbered API/UI/Worker review ZIPs only through the existing lower-level
payload/ZIP validators. Record product-input hash, dirty-file snapshot, build
version, all payload/ZIP hashes, configuration review and 024-only SQL inventory.
These are not an installable numbered successor, security approval or corporate
acceptance. The final source commit and exact-payload gates still apply.

Scope remains native service hosting, same-runspace SCCM diagnostics/dependencies,
system-status presentation, archive catalogue/024, four-sheet export/lifecycle,
OCO profiles/preparation, persisted mail outcomes, supported ServerRequest and
management reporting. Maintenance end is not restart time. Production profiles
remain review proposals. In Use transport still lacks IU-05 contracts; SMTP and
incomplete completion remain disabled. None of these external dependencies blocks
independent building, ordinary isolated tests or package integrity checks.

Reviewed compatibility: API/UI/Worker must have matching Shared/Infrastructure
and entry build identities; existing contracts/OpenAPI and SQL 023/024 boundaries
remain. Test-only configuration and explicit root support survive consolidation.
The same configured data-directory lock applies to new console/service processes,
not rc6.26, another directory or another machine. SQL leases and Unknown recovery
are still authoritative. Host shutdown requests bounded Hangfire disposal; actual
SCM timing, logoff and crash/recovery remain unexecuted. Rollback stops all writers,
coordinates component versions and retains 024, intents, audit, key rings and
archives; old serializers must not overwrite new fields. No target changes.

One narrow local correction found during consolidation: the profile validator
accepts whitespace DescriptionTemplate with a literal Description, but proposal
formatting previously selected the whitespace template. Formatting now uses the
same IsNullOrWhiteSpace rule. Focused tests cover literal/whitespace/template
selection, a distinct manual restart, and separate dynamically retrieved services.
No new source field, production profile approval or external contract is added.

E-06 local delivery result (newly executed, not E-04/E-05 reuse):
`C:\SecureOpsBuild\delivery-review\2026-09-21-consolidated-test`.
Matched ProductVersion for API/UI/Worker:
`0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.9fa51778d1de`.
Full product-input SHA-256:
`9FA51778D1DED284CCECC4427A36FE36A4A95A7565CDA623DE803AF549D9675C`.
This is HEAD plus explicitly uncommitted inputs, not a new commit SHA. The source
directory records the hash recipe, product-input manifest, build-time workspace,
tracked patch and untracked files; later documentation closeout is separate.

New full format verification passed. Matched Release build: 0 warnings/errors.
Unit `evidence/unit/dmtak_DEMET_2026-09-21_04_08_07.trx`: 1,425 passed.
Integration `evidence/integration/dmtak_DEMET_2026-09-21_04_08_11.trx`: 279 passed,
56 opt-ins skipped. The three new proposal cases are included in 1,425, not added.
The earlier 54 isolated opt-in passes remain historical, not current payload
acceptance. No excluded host/process/SCM/browser/SMTP gate was silently enabled.
The local token remains non-elevated; no SCM test or security-blocked process ran.

All three publishes match the tested build entry/Shared/Infrastructure hashes;
Shared and Infrastructure match across components. Existing payload scans, API AD
dependency/ZIP gate, UI asset/ZIP gate and Worker dependency/module/ZIP gate passed.
Payload counts: API 525, UI 542, Worker 531. Server-owned web.config/appsettings
are excluded; staging is NOT the deployment input. Complete manifests are under
manifests/, and candidate-metadata.json binds versions, tests and all ZIP identities.

| Review artifact relative to candidate root | SHA-256 |
|---|---|
| packages/secureops-api-TEST-review.zip | 289E84441EA108D5DA63B50159904D2750948410B614523D6C2D4614A96E8326 |
| packages/secureops-ui-TEST-review.zip | C8F18B4ADF7E54B7CACC5F7AE5591CCE076E45228003595B56054F10ADB5EED6 |
| packages/secureops-worker-TEST-review.zip | 7F5E135CD34BD801A7222D667F373B005D359DDE794A01BAD981271F2EC3D190 |
| packages/secureops-database-024-review.zip | 198035238CECDA98184279CC4C2660CC083A06AF33789E8B50103E9BF3F8F48D |
| security/preserved-diagnostic.zip | 00E8823327492BBDBBF2FD999AF83256DC37CF017A380EDFD76A7A141B90E2D3 |

The last archive is a new transport container around the unchanged 531 local E-05
files, NOT rebuilt diagnostic binaries or evidence of the original transfer ZIP.
Old diagnostic ProductVersion remains `...-sccm-diagnostic-wip`; Worker DLL hash
`6A3C157F345862B474448B5A6698D8CE4B2B1ADA31E95D0473778C0149E588EA`.
Consolidated Worker DLL hash:
`6D05FE7B37DD89311ACFF176427D0F0070AC26F379BD3D5CDB14825EFA9CBCFE`.
security/worker-comparison.json lists five changed payload files, including EXE.
The original E-05 tree/evidence and rc6.26 were read/hash-checked, not overwritten.

Configuration review includes only the new operator-selected WorkerHosting data
directory, explicit Test startup and preserved false mail/completion/PrepareSchema
fences; no guessed relay, production profile, account or source grant. SQL review
contains 024 migration+schema only for verified 023, narrow API SELECT/INSERT,
backup/stop criteria and coordinated rollback; no apply, grants or replay. The
candidate exports the current Turkish entry and service procedure. Review ZIPs
are concrete delivery-preparation artifacts, not numbered release/install approval.
Build SDK is 9.0.317; target remains framework-dependent net8.0, not a .NET 9
server requirement. Diff against rc6.26 confirms only the two 024 SQL files were
added to migrations/schema; 022/023 are unchanged and must not be replayed.
Remaining acceptance and responsible owners stay in SEC-01, OPS-02, QA-01/02,
DB-01, OCO-01/03, MAIL-01, SDM-01, IU-02/03/05 and RPT-01 below.

### SCCM failure continuation, 2026-09-21 (E-05 / OCO-02)

Pasted owner console evidence, not original-file integrity verification: the
normal Worker starts in Test from its installed directory under the reported
runtime account. Collection fails with ActionPreferenceStopException and
AnnouncementSourceCollectionUnavailable. Five distinct JobIds are visible;
their retry/attempt relationships were not supplied. E-04 queue readiness stays
closed. The console does not identify the failed PowerShell operation or underlying
ErrorRecord, so module, permission, CMSite and connectivity causes remain unproven.

Source now separates Import-Module, New-PSDrive, Set-Location and Get-CMDevice
invocations in one runspace and logs bounded error metadata: stage, fixed command,
allowlisted error identifier plus full-ID hash, category, exception types/HResults,
and invocation line/offset. Raw messages, TargetObject, script/position text,
credentials and device payloads are excluded. A selected --sccm-diagnostics mode
performs only that same bounded collection read, without hosting/queue dispatch,
service lookup, SQL, mail or closure. Explicit absolute --contentRoot can reuse
installed configuration without copying secrets; default remains executable root.

Independent local defect: the former engine-only System.Management.Automation
dependency cannot load built-in Microsoft.PowerShell.Management for New-PSDrive
in the isolated in-process host. The real runspace test failed before the fix.
The matching Microsoft.PowerShell.SDK 7.4.18 supplies built-in host modules; Roslyn
workspace/compiler 4.9.2 and JsonSchema.Net 7.0.4 align its dependency requirements.
No execution-policy weakening, module-path guess or corporate permission change.
This is a local payload repair, NOT yet the target incident's proven root cause.
Earlier worker-service-20260921 staging/tests below predate these source changes.
Current local evidence is retained separately in sccm-diagnostics-20260921.

Private legacy script was parsed as UTF-8 without execution. Lines 147-159 import
the console manifest via SMS_ADMIN_UI_PATH, reuse/create CMSite and query the
collection; current provider imports by name in a fresh Restricted runspace,
uses a private site drive, ErrorAction Stop and a bounded collection read. The
legacy script alone does not prove its executing PowerShell version/identity.
Compare the actual module version/availability and runtime with target evidence;
do not infer PowerShell incompatibility from ActionPreferenceStopException alone.

Five private review candidates preserve each legacy mapping, Impact and Checks
(script 122-126 and 225-252). All five bind and validate against retained rc6.26
binaries in nested JSON, flat Worker keys and API environment XML with matching
profile fingerprints. NonProd Scope is owner-approved; four production Scope
texts are explicitly derived review proposals, not legacy verbatim text. Revision
and existing recipients are omitted from the merge to preserve target settings;
the owner must approve a new shared revision for changed content before use.
No corporate values or samples were added to repository fixtures/docs.

The owner-reported email has maintenance end 05:00 and restart 03:15; the raw email
was not independently verified here. Legacy lines 226/233/239/245/251 substitute
EndDate as restart, which is not a valid equivalence. The earlier NonProd template
recommendation below is SUPERSEDED: review candidates label WorkEnd only as
maintenance end and require separate restart review. Structured restart fields
stay manual/NotDerivable, with no invented source selector or template token.
Historical preparations/EML are immutable and are not rewritten.

Dynamic profiles remain configuration-backed but limited to the existing five-name
allowlist and startup options. One protected, reviewed master can generate both
config surfaces and compare revisions/fingerprints at coordinated restart; there
is no atomic hot reload or shared revision-store feature today. A future revisioned
source would need validated atomic snapshots and existing job fingerprint guards,
not a new management UI to unblock this incident. See the current Turkish entry
for the selected diagnostic procedure and private review artifacts.

Current local evidence: full unit `unit/dmtak_DEMET_2026-09-21_01_15_32.trx`,
1,422 passed; integration `integration/dmtak_DEMET_2026-09-21_01_16_09.trx`,
279 passed/56 opt-ins skipped. Earlier focused failures remain retained; corrected
focused tests are included in the unit total, not added. The unnumbered Worker
diagnostic publish identifies `5c986a9...-sccm-diagnostic-wip`, NOT a committed
product source or release. All 531 published files passed the unchanged payload
scanner after standard debug-symbol exclusion. A private checker used the actual
published engine for Restricted FileSystem drive/location/Select-Object operations;
it passed without corporate providers. Both rc6.26 and current published profile
binders passed five-way configuration/fingerprint parity. The published EXE ran
from a different current directory against synthetic Test-only config and an empty
module path: ImportModule / Modules_ModuleNotFound was emitted as sanitized JSON,
exit 2, no host/log/lock created. This tests failure reporting, not SCCM connectivity.
`local-evidence.json` retains exact payload/source hashes and TRX filenames. The
531-file diagnostic folder is an operator investigation input, not a successor ZIP.
Final repository format verification passed. The tightened packaging contract
rerun passed 4 tests (`release-gate/dmtak_DEMET_2026-09-21_01_23_03.trx`), overlapping
the normal unit suite rather than adding to its total. The actual Worker runtime
and PowerShell-manifest gate passed against staging without creating a ZIP.

### Operational delivery, 2026-09-21 (E-04 / OPS-02)

Recovered HEAD 5c986a96f1f6639e47bf1432a87c3ac55e98054d on the same branch;
c12abf2 remains the last committed product. The two uncommitted guidance changes
from the NonProd repair were preserved. New Worker hosting edits are working-tree
source, not installed rc6.26 behavior or a new numbered candidate.

Owner-supplied target observations now identify all three entry DLL ProductVersion
values as 028cbd2 / rc6.26. Full target payload hashes were not reverified. Normal
Worker identity/directory and explicit Test startup were supplied privately; only
appsettings.Test.json is reported present. API/Worker compared settings, including
repaired NonProd, match; API reports queue ready and one matching Worker. This
supersedes the earlier current-configuration gap in E-02/E-03, not their historical
snapshot bytes. No new raw diagnostic timestamp/hash was supplied for E-04.
Queue readiness is not SCCM/TH collection acceptance. Mail and In Use completion
remain disabled. The team uses WASAS; successor-specific and remote-effect
availability still follows the per-module matrix, not a blanket unavailable claim.

Owner now authorizes native Windows Service implementation. Existing WindowsServices
dependency is connected, explicit executable content root preserves Test-only config,
console and diagnostics modes remain, and configured local data directory provides
bounded lifecycle logs and an exclusive process lock. Service mode rejects absent
environment/data directory/job-server configuration. SQL leases/recovery and
uncertain-effect policies are unchanged. Old rc6.26 does not honor the new local
lock: the console must be stopped before service handover. Actual SCM/logoff/crash
acceptance is pending; this local token is not elevated and no service was installed.
The earlier UI host restriction was not bypassed. See the linked service procedure.

External IU-05 contracts no longer block an honestly scoped Worker/fixes successor
indefinitely. They still block real In Use completion itself. OPS-02 service,
QA-01/02 exact-payload/browser/MIME and release validation gates remain required.
Release inventory still ends at rc6.26; no new suffix is reserved or ZIP produced.
SQL review confirms unchanged 024-only catalogue delta for verified 023, no Worker
migration. Target 024 installation/ledger/object state still requires DBA evidence.

Local evidence root: `C:\SecureOpsBuild\validation\worker-service-20260921`.
Release build: zero warnings/errors. Full repository format verification passed;
the final Worker line-ending correction also passed project whitespace verification.
Focused Worker/release tests: 16 passed (included in normal totals, not added).
Final unit TRX: `final-unit/dmtak_DEMET_2026-09-21_00_48_54.trx`, 1,411 passed.
Staged-build integration TRX:
`staged-build-tests/dmtak_DEMET_2026-09-21_00_46_14[1].trx`, 279 passed/56 skipped.
Initial `regression/dmtak_DEMET_2026-09-21_00_42_22.trx` retains one
FileSystemWatcher.Dispose NullReferenceException in API factory teardown (278
passed); a full unchanged integration rerun also passed 279/56. No API workaround
or hidden suppression was added. Opt-in SQL/process/MIME gates were not rerun or
counted as passed by this normal run; earlier isolated evidence stays historical.

The three unnumbered staging components identify
`0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-worker-service-wip`, explicitly a
working-tree label, NOT a committed product SHA. All three payload scans passed
(248 API / 265 UI / 254 Worker files); Worker runtime closure has 247 assets.
The final published EXE diagnostics ran from another current directory using only
a synthetic Test config: file binding verified, source disabled, host not started,
data directory not created. Diagnostics preserves read-only behavior; this is not
an actual service/process interruption test. `local-evidence.json` records payload
and changed-source hashes, exact TRX filenames and the initial failure.
No numbered release, target ACL/SQL/service change, corporate job or mail send.

### Recovered checkpoint, 2026-09-20

Current locally tested product/build: `c12abf29a60d2087728cd1e34879c0360a57c1c9`.
The subsequent closeout changes only this register and the current Turkish entry;
its Git identity is documentation-only, not the built product. No successor ZIP
or installation claim. The b596058/69a9b839 recovery identities below are retained.

Recovered the existing clean worktree at
`C:\SecureOpsBuild\secure-ops-combined-test-delivery-20260915`, branch
`feature/combined-test-delivery-20260915`, HEAD
`69a9b839614d97aaed5ea9f600cc4841cf01d24d`. The Desktop checkout is another
worktree at `81f5757`; it was not switched or edited. Process inventory showed
this Codex process tree and no other Codex/Claude/dotnet writer. No target D:
drive was available. The later supplied API snapshot is recorded below; Worker
diagnostics and direct live target access remain unavailable.

Incoming tested product source is `b596058fa0e1f82b278511f9a9e1c032c1130045`.
The diff from product source to incoming HEAD contains only this register and
`continuation-b596058-evidence.md`. All three retained Release DLL versions/hashes
matched that evidence at recovery. The later system-status correction changes UI
source; the preserved b596058 staging does not contain it. Guidance export and
browser changes remain in scope. New local test evidence does not close the
unexecuted browser or exact-successor-payload gates.
rc6.26 source `028cbd2e4ec7068d71a33088d7c651e4df21644a` and all six package
sizes/hashes match retained metadata. Release inventory ends at rc6.26; rc6.27 is
the next unused suffix as observed, not reserved or packaged. Recheck at delivery.

The supplied original v13 envelope is now present outside Git/webroot. Raw and
decoded hashes/size match the owner manifest. Its six historical sheets remain
unchanged; the legacy four-sheet sample has the same ordered 22 NMS headers and
29 Sunucular labels, and both checklists are empty. Different OR values were not
compared for equality. The missing-original statements below are historical and
superseded by this read-only verification; no original is requested again.

### Supplied API snapshot and system-status correction, 2026-09-20

E-02: received API evidence, not an agent live observation or Worker report.
Private original `wasas-api-operations.json` SHA-256
`6B2A46EC39A496EBC84E2A5C4BE8AA9CD77308093CF3BB9CE8939C7FC3B08B57`.
CapturedAt `2026-09-20T00:54:02.5421853+00:00`; Source.CheckedAt
`2026-09-20T00:54:21.8864262+00:00`. Capture is startup composition, not a
replacement for the later source-check timestamp. Original stays outside Git and
webroot. No product SHA/version is present. API runtime identity matches the
previously reported API account; no Worker identity or availability is inferred.

| Evidence / effective setting | Proven scope | Remaining action / owner |
|---|---|---|
| Announcements:Enabled=True, environment provider | Module enabled only | Source and sending remain separate / operator |
| Hangfire:Enabled=False; PrepareSchema=False, Default | Queue disabled in API composition, not SQL outage/schema absence | Normal-start Worker report, matching comparator and schema/queue verification / API + Worker operators |
| AnnouncementSource:Enabled=False, State=Disabled; both providers Disabled | OCO collection off; SiteCode, ProviderMachineName and three source selectors blank in reported values | Approved source/profile/collection/template values and selected terminal OCO job / source owner |
| Missing=[]; WorkerState=NotChecked, count=0, heartbeat=null | Disabled path returns before completeness/heartbeat checks | Neither complete configuration nor stopped Worker proven; obtain Worker evidence / operator |
| All three AnnouncementMail flags False, Default | Self-test/distribution disabled; no relay attempt | Approved relay/TLS/envelope/domain policy, API/Worker comparison, selected self-test then separate inbox observation / messaging owner |
| Six assets Validated, main format png | Individual bytes/formats validated; preserve PNG inside main.jpg | Bundle PresentNotValidated remains partial; exact MIME/Outlook acceptance / test operator |
| ReportState=ReadableWriteNotTested at the existing owner-configured archive | API archive read check passed | Writes and original-byte target download not tested / operator |
| InUseCompletion Enabled=False, Provider=Disabled | Source completion off, real adapter/contract blocker unchanged | IU-05 source-owner contract, then adapter and controlled acceptance |
| TuruncuHat:InUseAspectLookupEnabled=False, Default | Effective aspect lookup off | Does not diagnose every missing service-owner field or establish mapping correctness / source owner |

Default identifies reported fallback/provider, not an explicit operator disable
override or proof a key is absent from all files. Profile entries are not shown;
do not infer the entire dictionary is empty or accepted. No live source, SMTP,
archive-write, SQL migration or business completion action was performed.

UI-01: bounded `/admin/system-status` presentation uses the existing authorized
read-only endpoint and unchanged JSON download contract. One aligned card has
explicit disabled/unchecked/partial results, UTC capture/check times, stale-result
warning, progress/single-flight protection and collapsed authorized details.
Page refresh remains integration-only and mutually disables the check while busy.
No polling, new probe, setting write, mail or closure was added. Focused synthetic
tests and current build results are recorded at closeout below. Current visual,
mobile, keyboard/touch and real 200% zoom acceptance remains QA-01; the earlier
host-policy denial was not bypassed. The named screenshot was not available in
the inspected attachment paths, so no before/after image pair is claimed.

### NonProd configuration review, 2026-09-20 (E-03 / OCO-01)

Incoming product c12abf2 / documentation 5c986a9 verified clean. This narrow
review changes guidance only; product/build remains c12abf2 and rc6.26 remains
owner-reported installed. These two guidance edits are in the working tree at
5c986a9; no new commit, product build, release or target configuration change.

Compared rc6.26 source 028cbd2 with c12abf2: AnnouncementSourceOptions,
MaintenanceProfileCatalog, MaintenanceProfiles, AnnouncementValidation,
AnnouncementSourceService, OperationsDiagnostics and Worker Program are unchanged.
The relevant DI Configure<AnnouncementSourceOptions> registration is unchanged;
later DI differences concern In Use, not this binder. Six inspected rc6.26
Shared/Domain/Infrastructure/Worker/binder/JSON-provider DLL hashes match its
retained manifest. The packaged binder is byte-identical in current staging.

The reported Worker configuration has source/Hangfire enabled and a NonProd
collection/fingerprint, but only CollectionId/Label are supplied. With that
shape, exact profile failures are Scope, Impact, Checks, Description; Revision
defaults to string "1". Global diagnostics emits AnnouncementSource:Profiles
when no allowlisted profile is Configured; it fingerprints bound profiles before
that validation. Reading the configured collection ID is not reading SCCM.
Worker JSON has now been supplied as pasted chat evidence, not an original file
with verified byte integrity. Capture: 2026-09-20T02:14:40.6917339+00:00;
source check: 2026-09-20T02:14:40.8524852+00:00. Exact state is
ConfigurationMissing, stage configuration, Missing [AnnouncementSource:Profiles].
NotChecked/0/null does not establish a negative heartbeat check. The interactive
diagnostic identity is not proof of the normal Worker identity or availability.
Empty Worker asset/archive fields do not invalidate API E-02 observations or
require adding API archive/branding configuration to this Worker.

Private legacy mail_sscm.ps.txt SHA-256
`4A0F86D26C729CCEADFE7F7CE1D8715CD8F78E14196662DD5BD28F1376B0C3EA`
was parsed as UTF-8 data, never executed. NonProd collection maps at line 122;
Impact, template and Checks come from lines 225-227. Only the two date expressions
were translated into supported WorkStart/WorkEnd tokens, with no invented dates.
Scope is dynamic deduplicated services at lines 75-86/291-292, not an approved
fixed profile value. The current code proposes profile.Scope separately from
the service list. **The owner has now approved the literal NonProd Scope.**
The earlier empty-Scope review block remains immutable historical evidence, not
the current merge candidate. No recipients or credentials
were added to repository/fixtures. The current Turkish entry gives exact types,
defaults, bounds, field provenance, precedence and operator steps.

Evidence root: `C:\SecureOpsBuild\validation\nonprod-profile-review-20260920`.
An isolated non-hosting checker loads the retained rc6.26 and c12abf2 option/catalog
DLLs using the packaged binder (SHA starts BEAC12F7); 22 assertions per baseline
passed with the same outcomes. Sparse supplied shape: 4 missing fields; the
legacy-derived candidate: Scope only; an in-memory synthetic Scope positive
control: Configured. The synthetic Scope is not saved in the operator candidate.
Checks include defaults, bounds, template tokens, optional/invalid recipients,
flat/nested parity, duplicate JSON keys and env/CLI leaf precedence. No host,
corporate queries, SQL/ACL changes, queue execution, SMTP or acceptance action.
Do not add these assertions to the retained normal regression test totals.

Approved repair evidence: `C:\SecureOpsBuild\validation\nonprod-config-repair-20260920`.
The four-leaf Worker merge and 23-entry API environment merge are private inputs,
not replacement configuration files. The installed-baseline and current DLLs
each passed 15 isolated assertions: approved profile Configured, no missing fields,
flat Worker/API profile fingerprint parity, SMTP false and PrepareSchema=false.
The pasted original profile fingerprint matches the sparse profile with defaults,
including Revision="1". No host/provider/SQL was started. Assertions are not
additional normal regression tests or target acceptance.
The existing Compare-OperationsReadiness.ps1 returned exit 2 and 11 differences:
source/Hangfire enablement, both providers, site/machine, three selectors and two
NonProd entries absent from API. DB/TH/mail-policy fingerprints match; this does
not prove connectivity. API E-02 remains disabled; no target change was performed.
Next: operator records normal startup identity/overrides, reviews pending queue
work before any normal Worker restart, backs up and merges the approved leaves,
then collects paired diagnostics using the existing comparator. Profile repair
may advance read-only diagnostics to SQL heartbeat SELECT. Named foreground
ownership and an expressly selected terminal OCO job remain acceptance gates.

### Historical remaining-work continuation from 03b0c04

Verified incoming HEAD was `03b0c048d50cf926b5640116533df39c8685a37d`, clean on the
existing delivery branch. The same cumulative owner exception applies; all older
baselines below remain in force. No target D: deployment is accessible here.

The owner identifies the original current envelope as 27,044 bytes, SHA-256
`2FDB1ABB5837BF292F8912D8ED707AAF9342A96A4804EF8A05D88BB0E5CD828A`;
its embedded XLSX is 6,788 bytes, SHA-256
`11ECD7B916A9A6086D934C107936664CFBC999CB956EB7C3564A9145688C40DC`.
The malformed chat paste is a different representation, not evidence that the
original attachment is corrupt. Exact-name Desktop/Downloads search and available
resource inventory did not expose the original attachment in that earlier session.

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
filesystem at that checkpoint; the pasted copy has a malformed final `Archived` property. Its rows
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
The current list implementation updates this table; E-07/E-08 counts remain in their sealed copies.
They include OpenAPI, new files and inherited work; a commit does not reset them.
Private, non-deliverable profile checkers are additional validation scaffolding:
earlier 112 C# + 11 project lines; approved repair 64 C# + 4 project lines outside
Git. Neither is hidden in the repository diff or counted as product implementation.
Current five-profile/published-host checker adds 57 C# + 4 project lines outside
Git, plus a private evidence recorder; these are validation tools, not shipped code.
E-06 adds 294 private PowerShell orchestration lines for source capture, existing
package gates and review manifests; no release rule or permanent instruction changed.
E-07 reuses private orchestration in a new root with separate manifests and preserved
artifact verification; its script and private handoff line counts are recorded in
the E-07 candidate evidence. No duplicate copied payload/guidance is counted as new
product implementation; all new source/tests/OpenAPI and this guidance remain in
the cumulative repository counts below.

Post-E-08 private comparison/harness scaffolding is counted separately in
`validation/inuse-archive-20260922/private-scaffolding.json`; it is not shipped code.

| Baseline | Files | Additions | Deletions |
|---|---:|---:|---:|
| this continuation 69a9b839 | 100 | 4791 | 374 |
| immediate remaining-work checkpoint 03b0c04 | 124 | 6606 | 358 |
| actual continuation 34c8837 | 137 | 8102 | 422 |
| inherited rc6.24 8c1b58d | 182 | 12526 | 1520 |
| complete post-rc6.22 aa9e4d2 | 206 | 15790 | 1633 |

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

| Stable ID / requirement / class | Implementation and evidence/build | Owner | Concrete input / next action | Target status |
|---|---|---|---|---|
| IU-01 Report catalogue / local implementation | SQL bounded authorized metadata search, explicit integrity-checked indexing, readable original metadata; `InUseCatalogue` SQL tests and new UI route `/in-use/reports`; requires 024 | Developer / authorized test operator | Current browser/payload gate; apply only reviewed 024 with matched successor after gates | Not in rc6.26; not installed |
| IU-02 RFC suggestion / missing source contract | Exact scoped crosswalk resolver and explicit UI decision; matched/no-match/ambiguous/ineligible tests; no display-name matching | Source identity owner / access owner | One approved source scope + reporter reference -> existing eligible application GUID, review reference and expiry; then target case | Resolver complete locally; corporate mapping absent |
| QA-01 Assignment, lifecycle, catalogue, OCO UI / test-runner restriction | Incoming 03b0c04 repairs retained; current browser script includes pointer, focus, themes, reflow and native zoom assertions | Authorized test runner | Run existing host/test procedure; prior tool-policy rejection not bypassed; OCO source journey separate | Current UI screenshot/200% acceptance pending |
| IU-03 Four-sheet Excel and history / corporate acceptance | Prior real legacy hash/29 labels/22 headings and synthetic Excel acceptance retained; immutable download tests | Evidence owner / TEST operator | Original 27044-byte envelope and embedded 6788-byte XLSX now verified; next: old/new authorized target download hashes | rc6.26 installation is owner-reported; its historical six-sheet output is preserved; repaired exporter needs successor |
| IU-04 Draft recovery / corporate acceptance | Versioned reset/discard/restart, retained archive and invalidated suggestions; API/unit/SQL tests | TEST operator | Current UI journey then exact installed successor trial/restart case | New-source behavior not yet installed |
| IU-05 In Use upload and WASAS approval / missing source contract + unfinished integration | Durable stages and known mutation client; E-08 activity scope, manual verification retained | Source API/workflow owner, then developer | Required upload/property/approval contracts; separate minimal status query in IU-05-STATUS. Next-team/timeline deferred under IU-07-NEXT. Complete real transport against verified facts | No confirmed real upload or WASAS activity completion; not feature-complete. Overall OR closure is not the acceptance criterion |
| IU-06 Manual post-close verification / local implementation | Exact-OR confirmation, acknowledged versus unknown/rejected states, attributed manual audit; no replay or system-verified count | Developer / authorized TEST operator | E-07 local evidence below; controlled activation still requires IU-05 preconditions, matching payload/security and browser gates | Source-only until separately delivered/accepted; no corporate closure |
| OCO-01 OCO source / target configuration + corporate acceptance | Existing adapters/proposals retained; E-04 reports repaired profile/API/Worker parity and ready queue | Integration operator / source owner | One selected OCO/profile job, reviewed devices/services and dates, terminal result | Queue/profile gap closed by supplied target observations; SCCM/TH journey pending |
| OCO-02 SCCM collection failure / blocked by SEC-01 | E-05 staged diagnostic/dependency repair; E-06 reports console installed but process killed before JSON | Worker operator / SCCM owner / developer | First obtain exact-payload execution approval; only then one selected read with stage/ErrorRecord metadata and verified console compatibility | Five distinct jobs, no inferred retries; device/service counts unverified |
| SEC-01 Falcon execution review / external dependency | E-06 owner update: confirmed process-killed event, request opened; package not supplied, no detection details or decision | Cyber Defense / security reviewer / operator | Operator supplies preserved diagnostic and separate consolidated manifests via approved channel; obtain detection ID/time/hash/technical details and explicit per-payload/context decision | No false-positive claim, no bypass or rerun; security receipt and approval both pending |
| OCO-03 Five profiles and restart semantics / local review candidates | Legacy per-profile Impact/Checks retained; EndDate-as-restart recommendation superseded | Business owner / API and Worker operators | Approve production Scope, corrected description and shared revision; merge only reviewed leaves, compare all five fingerprints; separately verify restart time | Private rc6.26 binder evidence; no target profile activation or dynamic-profile redesign |
| MAIL-01 Self-test/distribution / target configuration + corporate acceptance | Real SMTP and durable outcomes retained; prior local sink/restart evidence | Messaging owner / operator | Actual relay/TLS/envelope policy, remove false startup overrides only under staged guide; exact self-test preparation then separately reviewed audience | No target send, inbox or Outlook evidence |
| SDM-01 OR to SDM / corporate acceptance + type-specific contract | ServerRequest positive policy; immutable Jira-only/close intent and durable link retained | Jira/source owner / operator | Exact selected OR/destination/actor; verify Jira result; other enum types need reviewed mappings, close needs final-state contract | No new target acceptance; not all types enabled |
| RPT-01 Management reporting / corporate acceptance | rc6.26 SQL 023 snapshot/drilldown/export preserved; discarded-state repair retained | TEST operator | Reconcile exact selected authoritative outcomes with same dashboard snapshot/export | Installation/023 owner-reported; new-result reconciliation pending |
| SRC-01 Collector / corporate acceptance | rc6.26 matched completion-evidence binary present; six package hashes rechecked | Source operator | Existing bounded read-only command; share only masked Evidence, not secrets; cannot discover undocumented attachment API | Not run here corporately |
| OPS-01 Archive / target configuration | Immutable envelopes; existing archive; E-02 API reports ReadableWriteNotTested | TEST operator / operations owner | Authorized archive-write and original-byte readback acceptance; service handover under OPS-02 | Read check verified; writing pending; E-04 supplies normal Worker operation |

## Activation boundaries

### Current module evidence states

This table is a view of the same requirements above, not a second work register.
"Reported" means owner-supplied evidence, not this agent's live target observation.
E-04 entry versions verify rc6.26 source identity but not complete payload integrity.
c12abf2 and b596058's results below remain historical committed-product evidence.
E-06 now supplies matched review ZIPs and normal local tests for implemented rows;
their full journeys remain unexecuted wherever the next column says pending.

| Requirement | Implemented | Exact successor payload tested | Installed | Configured | Corporately verified | Available to team |
|---|---|---|---|---|---|---|
| SDM-01 ServerRequest Jira-only | Yes; positive policy and persisted link | Pending | rc6.26 reported | Unknown | Selected OR/destination/actor missing | Not established |
| SDM-01 Transfer-and-close / other types | Closure contract missing; other type mappings unapproved | Blocked for unsupported effects | No supported closure claim | Gate remains off | No | No |
| IU-01/03/04 Review, four-sheet export, reset, catalogue | Yes; catalogue requires 024 | Current browser pending | Successor not installed | 024 target not verified | Download/lifecycle/catalogue acceptance pending | Successor unavailable |
| IU-02 RFC account proposal | Resolver implemented, empty by default | Current UI pending | Successor not installed | Reviewed mapping absent | No | Manual/unassigned path remains implemented |
| IU-05 Real attachment and WASAS activity approval | Partial; corporate transport incomplete | Blocked on attachment/conditional-update contracts, not final OR readback | Not available | Enabling a flag cannot unlock it | No | No |
| IU-06 Manual post-close verification | Implemented E-07; automatic final readback deferred | Matched local build/unit/SQL; browser pending | Not installed | No new setting; real transport still IU-05-gated | No corporate closure/attestation trial | Not yet available |
| OCO-01 Source and review | Real adapters/proposals; E-06 literal-template correction included | New source process/UI pending | rc6.26 entry versions E-04 | Repaired NonProd/API/Worker parity E-04; console installed E-06, compatibility unverified | SEC-01 blocks diagnosis; terminal OCO/device/service/date journey pending | Team uses WASAS; collection acceptance not established |
| MAIL-01 Self-test / distribution | Real transport and durable state implemented | SMTP/restart/MIME pending | rc6.26 entry versions E-04 | API/Worker mail disabled; approved relay/actor Mail pending | No send or inbox observation | Disabled |
| RPT-01 Persisted workflow reporting | Snapshot/drilldown/export implemented | Current outcome reconciliation pending | rc6.26/023 reported | Target 023 not independently observed | Same-cut target comparison missing | Not established |

### Additional gate records

| Stable ID / status | Owner | Precise input and next action | Evidence/build |
|---|---|---|---|
| E-01 Original evidence / closed locally | Developer | None for file integrity; target re-download remains IU-03 | Original manifest hashes and in-memory ZIP/XML comparison, 2026-09-20; originals unchanged |
| E-02 API snapshot / historical limited target evidence | API operator | Retain original capture; current settings superseded by E-04, archive write still pending | Private original hash and both UTC times above; snapshot has no product version |
| E-03 Worker profile repair / closed for reported configuration | Worker/API operator | No repeat repair; preserve approved profile. OCO terminal acceptance remains OCO-01 | Earlier 22/15 isolated assertions retained; E-04 now supplies target parity and normal Worker provenance |
| E-04 Target version/config/queue / received, limited verification | API/Worker operator | Full payload hashes, selected terminal workflow and new raw paired reports at service handover | Owner-supplied entry ProductVersions 028cbd2, Test Worker identity/path, repaired profile parity, ready queue/one Worker; no new raw capture timestamp |
| UI-01 System-status usability / local source correction | Developer / authorized test runner | Current payload visual/keyboard/touch/themes/200% evidence under QA-01; no release for this change alone | Narrow Razor/scoped style/presentation tests; installed UI unchanged |
| QA-02 Process/MIME/SMTP / runner restriction | Authorized test operator | Matching staged publish, fresh synthetic DB/Hangfire 9; run distinct procedures in current Turkish entry; return payload hashes, TRX, browser/zoom/source/SMTP evidence | 54/56 skipped names matched to separate passes; source process and browser MIME still pending |
| DB-01 Catalogue upgrade / target prerequisite | SQL execution operator + release owner | Accepted successor, effective API 022/023 rights, backup and stopped writes; review only packaged 024 and API SELECT/INSERT delta. Keep historical execution notes if available, otherwise record unavailable | Operator confirmed approved TEST DB; 022/023 queried metadata matches reviewed DDL semantically; 024 not visible; result set 6 is operator-only permissions, not API evidence |
| REL-01 Scoped matched successor / E-06 review artifacts prepared; final gated | Developer + release owner | Review four matched application/024 ZIPs and manifests; close SEC-01, OPS-02, QA-01/02 and final committed-source gates before numbering/install approval | IU-05 stays disabled without indefinitely blocking independent delivery. rc6.26 and E-05 diagnostic remain immutable |
| OPS-02 Persistent Worker / implemented locally, SCM acceptance pending | Developer / authorized Windows runner / operations owner | Approved data directory and service-account rights; isolated SCM start/stop/logoff/crash and exact-payload recovery, then controlled console handover | Native service source with unchanged durable effect guards; local non-elevated token, no SCM installation or target change |

If the source owner confirms that conditional update or authoritative readback is
unsupported, record the dated answer against IU-05 rather than repeatedly asking
for a nonexistent API. The current operator entry describes manual source-UI or
source-side conditional-operation alternatives. Neither silently weakens the
accepted concurrency contract or turns manual evidence into a verified WASAS
completion. A changed business contract requires the owner's explicit decision.

The owner must select exact source IDs, Jira project/type and actor for each
controlled case. OCO self-test uses trusted Mail; distribution requires a reviewed
immutable preparation and explicit To/Cc audience. Unknown outcomes stop mutation
replay. The old foreground Worker needs a named session until OPS-02 service
acceptance and controlled handover. Native source support is not installed service
availability, and this agent has not installed a service.

## Consolidated external facts (not repeated installation questions)

| Exact fact / operation | Existing support | Missing evidence and responsible role | Affected action |
|---|---|---|---|
| `SMSS_oRFF.p_emb_dynamic_case_orff` keyed identity; properties 4463/4464 | Script request fields; bounded completion probe; durable execution/wire builder | Source owner: one selected OR's masked SET/KEY shape and documented conditional version/precondition semantics | Real In Use field effects |
| `DataRestSecure.svc/json/uploadattachment` target and bytes | Known `fBase=SMSS_oRFF`, exact `fId`, `fName`, `datastring`, `SessionID`, `TenantId`; response serializer | Source owner: supported attachment identity/list/content readback with exact OR binding and hash/byte evidence, response-loss reconciliation | Confirmed upload; safe restart |
| `BPM_Actvty`, models 103626/103627, status 1/group 68/main-object OR | Known query/update pattern; zero/multiple candidate guards; probe | Source owner: conditional approval, matched-count/conflict and acknowledgement semantics; no first-row assumption | Eligible WASAS approval; IU-05 required mutation contract |
| Minimal WASAS activity status | Pending-only collector; source/manual evidence categories retained | Workflow owner: selected OR/activity identity, bounded keyed status response and pending/completed/unknown meanings; IU-05-STATUS | Actionable/completed/verification-pending separation; next-team/timeline deferred |
| Authoritative overall OR state | Distinct historical closure evidence model; fixture readback; corporate verifier unavailable | Source owner: exact final-state contract only for separately approved SDM transfer-and-close | Not an In Use WASAS approval or minimal-tracking gate; historical closure evidence retained |
| SDM type/project/issue fields/reporter/assignee | Exact enum includes ServerRequest, EnvironmentRequest, SoftwareInstallation, ConfigurationRequest, OperationalSupport, NotJiraEligible, NeedsManualReview, ServerRetirement; positive policy only ServerRequest | Jira/source owners: approved type-specific policy and bounded mandatory-field metadata for selected destination. Owner selects OR, actor and intent | No implicit activation of other types |
| Unknown Jira create | Persisted command/link and reconciliation stop | Jira owner: authoritative exact correlation lookup for the selected project or reviewed manual remote identity evidence; empty searches do not prove absence | No duplicate creation after lost response |
| OCO profile/collection/service/date source | Fixture and real adapters; E-04 confirms API/Worker profile/queue alignment | Integration owner: exact selected OCO/profile, successful terminal device/service/date evidence | Corporate retrieval |
| Relay From/envelope/TLS and recipients | Real SMTP transport, immutable MIME and Worker; local rejection/unknown/revocation evidence retained | Messaging owner: approved relay policy/config revision. Operator: exact preparation, self-test actor Mail, separately reviewed distribution To/Cc; Outlook observation | Controlled mail activation |
| Actual archive and runtime composition | E-02 archive read; E-04 runtime identity, entry versions and queue parity supplied | TEST operator: full target payload hashes, archive write/readback, verified schema ledger; OPS-02 service handover owner | Data-preserving activation |

At the rc6.26 checkpoint no actual sample XLSX had been attached. Both sample
availability gaps are now closed by the original-file verification above;
neither is reconstructed from a chat paste. The timestamped script SHA remains
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

### 2026-09-20 independent continuation

Private runner input: `C:\SecureOpsBuild\validation\completion-20260920`.
`payload/api`, `payload/ui`, `payload/worker` were published with
`-c Release --no-build --no-restore`. Product source/build inputs were checked
unchanged from b596058 before publishing; all six project DLLs reported that
version. The three staged entry DLLs retain the exact recorded b596058 hashes.
This is unnumbered runner input, not a successor ZIP or a host-tested payload.
`staged-payload-manifest.json` records 767 paths/hashes, including four non-secret
default configuration files that release packaging excludes. Eighteen debug
symbols were removed only from this fresh staging tree, as the release script
already does. All three existing payload/secret/personal-path scans passed.

Re-read committed TRX results: 1,382 unit passes, 279 integration passes; actual
result nodes contain 56 NotExecuted opt-ins. 54 names match separate passing
SQL/access/preparation/mail TRX entries. No totals were added or tests relabelled.
These are retained executions, not new suite runs. Product code/SQL did not
change, so a full regression was not repeated. Current browser/MIME/process/SMTP
acceptance was not run, and no denied host action was retried.

Changed PowerShell parses and browser `node --check` pass. Isolated evaluation
of the actual packaging selection expressions yields 024 for rc6.26, 023-024
for rc6.24, 022-024 for rc6.22, and 019-024 for verified 018; the legacy rc6.21
switch and conflicting baselines are rejected. This tests delta selection without
invoking publish/package or SQL execution. The guidance smoke export copied five
files with identical hashes, resolved all nine local Markdown links, rejected
relative paths and repeated output, and preserved existing output hashes. Its
snapshot is in `guidance-smoke/`; later register/count updates are source records,
not edits to that retained export. This is not full release acceptance.

Historical release archives/manifests and supplied original evidence are unchanged.
No SQL database was created or queried here; no target configuration, IIS, service,
grant, source mutation, mail, team message, commit, push or deployment occurred.

### System-status local verification, 2026-09-20

Private evidence root: `C:\SecureOpsBuild\validation\system-status-20260920`.
Release solution build passed with zero warnings/errors. Normal local runs:
`tests/system-status-unit-accepted.trx`: 1,399 passed, including 17 new
presentation/render/interaction cases; `tests/system-status-integration.trx`:
279 passed, 56 opt-ins not executed. Do not add earlier overlapping passes.
The two earlier unit TRX failures are retained: a release contract assertion
still named the archived operator guide and old runbook heading. The assertion
now verifies the current five-file exporter and preserves installation gating.
Scoped C# formatting and browser syntax are checked separately from UI acceptance.

New local results are not an installed or exact-successor-payload claim. The
retained b596058 stage predates this UI change and must not be used for UI-01
acceptance. Before/after screenshots and actual zoom/theme/touch evidence still
require the already documented authorized runner. No host-policy workaround,
new release ZIP, target mutation, mail or team announcement was performed.

Committed source c12abf2 was rebuilt using the existing release flags
`ContinuousIntegrationBuild=true`, `PathMap=<repo>=/_/`, `DebugType=None` and
`DebugSymbols=false`: zero warnings/errors. Final matching-build results are
`tests/mapped-committed-unit.trx` (1,399 passed) and
`tests/mapped-committed-integration.trx` (279 passed, 56 NotExecuted).
Scoped `dotnet format --verify-no-changes` and `node --check` passed.
The first ordinary-build staging failed the personal-path scanner; it remains
private at `payload/` and is not runner input. Corrected `payload-mapped/` has
three no-build publishes, all 12 own DLLs byte-matching the tested build and
version `0.1.0+c12abf29a60d2087728cd1e34879c0360a57c1c9`. Three payload scans
passed. Nine debug symbols were removed only from that fresh mapped tree.
`staged-payload-manifest.json` covers 767 files (including four nonsecret config
defaults excluded by release packaging), SHA-256
`2FDAE4BF45797A00A6E47E80A54D1B0BC85E80525FBBB6962263B3DD7542DF09`.
This is current unnumbered runner input, not browser/source/SMTP acceptance.

### Retained earlier integrated checks

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
