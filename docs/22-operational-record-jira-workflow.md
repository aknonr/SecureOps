# Operational Record to Jira Backend Workflow

## Status and Ownership

### Attributed Outcomes And Closure Boundary, 2026-09-17

Migration 021 captures the original persisted human initiator/profile and input
version on a new transfer; subsequent SQL stages retain that identity. Append-only
ops.OperationEvents (020) separates API executor and technical verifier from the
human and optional authoritative source closer. Historical NULL stays unknown.
`GET /api/v1/operations/OperationalRecord/{id}/events` is an authorized, audited,
bounded record history, not an employee report or execution/reconciliation write.
InUse history requires InUse.View; Announcement history stays Drafts/owner-only.
SchemaVersion 1 includes command/record/action/input version/server UTC/outcome,
profile-at-action, executor, verifier, correlation/causation and external reference.
No agent identity/delegation is active. Future callers use the same capability,
version, preview, confirmation and status services, never direct workflow SQL.

SourceClosureObservation binds source ID, OR code, Jira key, attempt, observed UTC,
source version, evidence contract and optional closer. Only matching recent
VerifiedClosed evidence can produce a newly verified Completed state. Acknowledged
BPM update is insufficient. Simulation/Fake can reread their exact source state;
the corporate adapter has no approved authoritative post-state contract, so closing
is blocked BEFORE the update even if a flag is misconfigured. Unknown Closing
after restart cannot blindly retry. Legacy Completed without new evidence is shown
as unverified, not silently promoted. Known Jira keys always block another create.

SourceCloseEnabled=false still suppresses the entire BPM activity operation and
its associated comment. Corporate comment destination remains only m_comments on
the uniquely selected BPM_Actvty, conditional on a future supported close intent;
not a Jira comment or assignment. No native watcher operation exists. Requester
custom field, authenticated reporter policy, project-default/verified-operator
assignee policy and distinct transport credentials remain unchanged. Missing
corporate mapping/correlation/post-state inputs are enumerated in the current
deployment runbook; no request type is promoted to bypass them.

### Local Acceptance Continuation, 2026-09-15

The current exact-record positive ServerRequest policy is implemented under
ADR-0018; older negative-only descriptions below are historical foundation scope.
SoftwareInstallation and ServerRetirement remain mapping-blocked. Detail recovery
must preserve a review declaration on recoverable failure, discard obsolete reads
and commands after record/access changes, and revalidate the capability snapshot.
No automatic external-write retry or corporate activation is introduced.
Only the existing fixed Simulation happy fixture is typed ServerRequest; other
synthetic records and the exact corporate-policy boundary are unchanged. The
numeric policy is independently exercised with real draft/SQL services and
test-only source/Jira/identity substitutes, not corporate HTTP adapters.
Current execution evidence and walkthrough belong to the existing SDM handoff in
`src/SecureOps.Ui/README.md`, not a separate project handoff.

### Independent Source Close (Source Only, 2026-09-07)

`OperationalRecords:SourceCloseEnabled` defaults to false. Global read-only and
controlled corporate-write validation remain authoritative. Jira-only still uses
the real source read provider for freshness; only BPM write configuration is
optional while its independent gate is off. Migration 011 adds default-false
`SourceCloseRequested` on the transfer and its append-only history.
The preview fingerprints close intent; create acquisition persists it atomically
with workflow history before Jira dispatch. Confirmed Jira success stays
`JiraCreated` with its key when intent or the current gate is false. No close
failure or `Completed` is fabricated. Create replay cannot duplicate Jira; retry
and restart cannot turn false intent into true. Existing transfers default false.
An already requested close can resume only on an explicit authorized retry with
the gate enabled. There is no worker/automatic continuation or intent-upgrade API.
rc6.11 and earlier packages do not contain this implementation or migration.

Local verification for the gate increment: Release build 0 warnings/errors;
281 affected unit/UI cases and 37 hosted/controller/SQL cases passed, including
four real isolated LocalDB cases. OpenAPI generation/compatibility passed.
The subsequent request-type increment passed interactive SQL/Simulation browser
journeys at 1440x900 and 390x844. Full integration: 245 passed, including all ten
real LocalDB cases with no skips. Final presentation regressions: 66 targeted unit
and 18 controller/OpenAPI cases passed. Canonical handoff contains exact evidence.

The backend foundation is implemented for TEST validation. Codex owns API, services, integration boundaries, persistence, SQL, authorization, audit, release packaging, and backend tests. Claude owns all Blazor/Razor/CSS/UI work and does not need to change this module.

Typed Turuncu Hat and corporate Jira adapters are implemented behind the existing boundaries and remain disabled by default. Paired `Simulation` selects the explicit deterministic no-network Development/Demo/Test workflow harness; legacy `Fake` remains for compatibility. The real Turuncu Hat read-only path is deployed and verified in TEST at source `0ec0376`; Jira creation and Turuncu Hat completion remain blocked until their separate write contracts and activation gate are approved. The canonical deployed evidence is recorded in `docs/24-api-test-deployment-readiness.md`.

## Legacy Workflow Replacement

### Operator-Declared Request-Type Review

The additive `SourceOpen` presentation category identifies confirmed Jira-only
success without labelling source completion or an in-flight close. List and detail
use this category; the progress display omits a close step that was not requested.

The confirmed choices are ServerRequest (Sunucu Talebi) and SoftwareInstallation
(Uygulama Kurulumu). `POST /{id}/jira-review` uses the existing preview capability,
the loaded record version and the same draft mapping service. It is a review-only
draft, not a saved source classification, approval, or publication preview. The
declaration is audited with its bounded source fingerprint/version, never inferred
from keywords. It does not change evaluation evidence or acquire a create token.
Changing choices discards the local preview/confirmation. Existing blockers plus
CategoryPolicyPending remain; no remote identity result is fabricated. The known
server label mapping can be displayed, but software-installation labels remain
empty with ApplicationMappingPending. Normal create/preview also rejects an
application-installation classification until its mapping is established.
There is no new approval subsystem or path from review choice to eligibility.

The original Operational Record/Jira PowerShell script was supplied outside the
repository and reviewed as source on 2026-09-07. Discovery is resolved; provenance,
the supplied copy's syntax defect and sanitized parity findings are recorded in
`docs/integrations/turuncu-hat-jira-legacy-parity.md`. The raw script is not committed
and the API never launches PowerShell. Historical request construction does not
approve eligibility, current remote field acceptance or a separate approver flow.

| Legacy step | SecureOps backend component |
|---|---|
| Authenticate/query active records | `IOperationalRecordClient` |
| Read bounded source fields | `OperationalRecordSourceItem` and `OperationalRecordService` |
| Operator selects a record | typed GET endpoints; UI deferred |
| Resolve requester and authenticated reporter | `IJiraUserResolver.ResolveExactAsync` |
| Apply fixed Jira mapping | `IJiraIssueDraftService` |
| Review the exact proposed create fields | `POST .../{id}/jira-preview` |
| Create Jira task | authorized `IJiraTransferService` plus `IJiraClient` |
| Persist Jira key | `IOperationalRecordRepository.RecordJiraCreatedAsync` |
| Close/update source record | `IOperationalRecordClient.CloseAsync` after key persistence |
| Retry partial success | `POST .../{id}/retry`; resumes source close when Jira exists |

## Workflow

Corporate refresh now records deterministic SDM evidence under ADR-0018 rather
than repeatedly emitting legacy classification events. Recommendation, human
approval, publication readiness, and external-write eligibility are separate.
V1 has no positive category policy; current corporate rows remain manual review
and publication-blocked. Unchanged evaluation input preserves its timestamp and
adds no evaluation history/audit. Source changes latch stale evidence. Evaluation
never calls Jira, resolves requester/reporter identities, or changes terminal or
reconciliation state. Synthetic workflows remain an explicitly separate harness.
SQL requires additive migration 009; application startup never applies it.

`Imported -> Classified -> NeedsManualReview | Eligible -> Previewed -> CreateRequested -> CreatingJira -> JiraCreated -> ClosingOperationalRecord -> Completed`

Failures persist as `JiraCreateFailed` or `OperationalRecordCloseFailed`. Disabled mode classifies records for manual review. A successfully imported Turuncu Hat record remains `NeedsManualReview` until a separately approved deterministic Jira-eligibility rule exists; source-scope membership alone does not authorize publishing. Synthetic classifiers remain restricted to their explicit synthetic providers, and synthetic rows are unavailable to list, detail, preview, create, and retry paths while the Turuncu Hat provider is active.

## Claims, Idempotency, Freshness, and Retry

### September 2026 local hardening milestone

The task explicitly authorizes cross-layer implementation, normal branch push and a
task-scoped exception to the 1,000-line change cap. Permanent ownership and size
rules remain unchanged. Corporate calls, deployment and external-write activation
are excluded. Script discovery was unresolved at that milestone; the subsequent
source-only review above supersedes that evidence gap, not the activation fences.

Preview fingerprints now bind the source concurrency token and exact summary and
description as well as configured mapping and resolved identities. A refreshed
source cannot silently replace reviewed content. Existing persisted previews from
older binaries fail closed on fingerprint mismatch; no transfer rows are reset.
The Block requester policy also applies when the requester is absent or a resolver
returns an empty identity. Uncertain creates, including interrupted persistence
after Jira success, require reconciliation and must never be described as a safe
automatic retry. No remote lookup/reconciliation command is invented without its
approved contract. Jira creation and source completion remain separate stages.

The transfer key is SHA256 over source-record identity plus the complete normalized create mapping: mapping version, project, issue type, summary policy, team, labels, requester/watcher field and resolved account, assignee, and reporter. Create/retry also uses the caller `Idempotency-Key` or a deterministic actor/command/target fallback in `ops.CommandExecutions`. SQL permits exactly one transfer row per Operational Record and enforces unique source IDs, transfer keys, command scopes, and Jira issue keys. Once preview fixes a mapping fingerprint for a record, a different mapping or resolved identity fails with `WorkflowConflict`.

Before create/retry, the API atomically acquires a bounded actor claim (`ClaimedBy`, `ClaimedAt`, `ClaimExpiresAt`). Another actor receives `OperationalRecordAlreadyClaimed`; active transition states receive `WorkflowAlreadyInProgress`. Expiry permits recovery after an abandoned client. Immediately before Jira create and again before source close, the source record is re-fetched with an exact validated numeric source-ID filter and checked for existence, open state, and matching explicit version token or deterministic source-state hash. Changed/closed source data aborts before the external write.

The Jira key is committed before the source close/update starts. A close failure therefore retries only the source stage. Concurrent, repeated, or completed create requests cannot call Jira twice. If a Jira call has an uncertain outcome, `ReconciliationRequired` blocks automatic retry. If the process stops while `CreatingJira` has no persisted key, retry also fails closed for manual reconciliation because remote Jira idempotency has not been proven.

A source refresh may update bounded source fields, but classification is reapplied only while the workflow remains in `Imported`, `Classified`, `NeedsManualReview`, or `Eligible`. Refresh cannot regress `Previewed`, create/close transition states, failures, completion, or reconciliation-required state.

Interactive UI verification also requires the list and detail state labels to
honour reconciliation-required evidence above a persisted create-failed state.
An unknown outcome is shown as unknown, with caution styling and blocked creation.
Duplicate reconciliation notices are collapsed only when their support references
match; a distinct action reference remains visible.

## Security Boundaries

### In Use V1 Authorization, 2026-09-08

2026-09-10 follow-up: InUse.View + InUse.Review no longer requires current
assignment for draft save. Assignment remains a separate optional action with
InUse.Assign, approved identity and exact version. Review/report audit names the
authenticated application actor; no external completion capability is granted.
Current local scope and remaining implementation are in the canonical UI handoff.

The user explicitly authorized this cross-layer In Use V1 implementation and a
one-milestone exception to the 1,000-line total reviewed diff cap, including
code, migration, tests, OpenAPI and canonical documentation. The permanent rule
is unchanged; the combined diff must be reported, not concealed by split commits.
In Use uses active category 4241/group 68, independent persistence and local
review capabilities. OR-to-Jira continues to exclude 4241. No In Use source
mutation capability exists. Refresh, assignment, draft review and report
preparation cannot upload, update source properties, create Jira or close BPM.
SQL migration 012 adds only local In Use state; no scheduler is required.


- Browser users never provide integration credentials.
- No password, token, authorization header, or raw remote response is stored or returned.
- Requester resolution is exact only. Ambiguous matches always fail closed.
- Unresolved requesters are blocked by default; `ProceedUnassigned` must be an explicit approved policy.
- Jira assignment defaults to `ProjectDefault`. Only an exact deployment-verified SecureOps actor mapping may emit `assignee`; no AD inference or fuzzy match is permitted.
- Jira reporter defaults to `ProjectDefault`. `AuthenticatedOperator` normalizes the server-authenticated actor with the existing exact identity rules, resolves exactly one Jira username, exposes it in preview, and emits it as `reporter` during create. Missing or ambiguous matches fail closed; the Basic-authenticated integration identity and source requester remain separate.
- Corporate preview includes project, issue type/name and ID, summary, description, team field/value, labels, requester/watcher field and resolved account, assignee, and reporter. The Jira adapter consumes this reviewed draft mapping instead of independently re-reading those create fields.
- Jira creation and retry require server-side capability policies.
- The working exact AD/PAM-style identity lookup provider is unchanged.
- Real source close/update is a state-changing external integration and remains configuration-disabled until the external TEST activation gate is approved.

## Authorization

Persisted application roles map to server-side capabilities:

- `CanViewOperationalRecords`: Operator, Lead, Admin, Auditor
- `CanPreviewJira`: Operator, JiraPublisher, Lead, Admin
- `CanCreateJira`: JiraPublisher, Lead, Admin
- `CanRetryJira`: JiraPublisher, Lead, Admin
- `CanViewOperationalRecordDiagnostics`: Auditor, Lead, Admin

## Audit

Import, classification, preview, create request/result, source-close request/result, retry, and completion write append-only audit actions. Entries contain authenticated actor, source/internal IDs, OR code, Jira key when present, workflow state, correlation ID, result, and stable error code. They do not contain full source payloads or requester profiles.

## Persistence and DBA Review

`sql/schema/002-operational-record-jira-workflow.sql` creates the base workflow tables. `ops.OperationalRecords.Description` is `nvarchar(max)` because the application validates source descriptions to at most 8,000 Unicode characters while SQL Server limits sized `nvarchar(n)` declarations to 4,000 characters. Migration 002 creates `[ops]` only when it is absent, which permits the reviewed recovery state where a prior failure left an empty `[ops]` schema; its unguarded table creation remains fail-fast for conflicting objects. Offline migration 003 adds source/claim metadata and `ops.CommandExecutions`; migration 004 adds access mutation versions; migration 006 permits unknown source creation time. The application does not run migrations. DBA approval and execution of applicable ordered migrations are required before selecting `SqlServer`.

Minimum runtime permissions are `SELECT`, `INSERT`, and `UPDATE` on these three `ops` tables; no `DELETE`, DDL, schema-owner, or migration permission is required. Audit-store permissions remain separate.

## Runtime Configuration

Non-secret keys:

- `OperationalRecords:SourceProvider` (`Disabled`, paired `Simulation` or legacy `Fake` only in Development/Demo/Test, or `TuruncuHat`)
- `OperationalRecords:RepositoryProvider` (`InMemory` or `SqlServer`)
- `OperationalRecords:MaxImportCount` (1-500)
- `OperationalRecords:ClaimLeaseSeconds` (30-900)
- `OperationalRecords:ReadOnlyIntegrationMode` (`true` only in `Test` with `TuruncuHat` + `Corporate`)
- `OperationalRecords:ControlledTestWritesEnabled` (defaults `false`; TEST writes require explicit `true`, read-only `false`, and the complete corporate provider pair)
- `CommandIdempotency:ExecutionLeaseSeconds` (30-900)
- `CommandIdempotency:MaxKeyLength` (32-256)
- `Jira:Provider` (`Disabled`, paired `Simulation` or legacy `Fake` only in Development/Demo/Test, or `Corporate`)
- `Jira:ProjectKey`
- `Jira:IssueType`
- `Jira:IssueTypeId`
- `Jira:MappingVersion`
- `Jira:TeamCustomField`
- `Jira:TeamValue`
- `Jira:RequesterWatcherCustomField`
- `Jira:Labels:{n}`
- `Jira:UnresolvedRequesterPolicy` (`Block` or `ProceedUnassigned`)
- `Jira:AuthenticationMode` (`Basic` for `Corporate`)
- `Jira:AssignmentMode` (`ProjectDefault` or `VerifiedOperatorMapping`)
- `Jira:OperatorAssigneeMappings:{n}:SecureOpsActor` and `JiraUsername`
- `Jira:ReporterMode` (`ProjectDefault` or `AuthenticatedOperator`)
- `Jira:SummaryMaxLength` (32-255)

Operator-visible TEST verification uses `Simulation` for both providers. Pairing is mandatory, it is rejected in Pilot/Production, it registers only in-process clients, and responses state that no real Jira issue will be created. Real providers require every validated option in `docs/26-enterprise-turuncu-hat-jira-adapters.md`; unsupported or incomplete selection fails startup and never falls back to synthetic data.

The minimal real-data/no-write gate sets `ASPNETCORE_ENVIRONMENT=Test`, `OperationalRecords:SourceProvider=TuruncuHat`, `Jira:Provider=Corporate`, and `OperationalRecords:ReadOnlyIntegrationMode=true`. Complete Jira field mapping is required at startup even in this mode so preview represents the future create payload. Source authentication/query, exact source re-read, Jira authentication/user search, and preview remain available. Create and retry return `ExternalWritesDisabled` at stage `external-write-fence` before command state changes; both corporate write adapters independently reject dispatch. Operational Record and preview responses expose `readOnlyIntegrationMode=true` and a safe write-disabled notice.

Controlled TEST writes remain disabled by default. A later approved activation requires `OperationalRecords:ReadOnlyIntegrationMode=false` and `OperationalRecords:ControlledTestWritesEnabled=true` together with the complete Turuncu Hat activity-update and Jira create mapping. The TEST-only gate is rejected outside `Test`, with read-only mode, or without the exact `TuruncuHat` + `Corporate` pair.
- `ConnectionStrings:SecureOpsDb` when SQL persistence is selected

Integration authentication values are runtime-only server configuration. Controlled Jira evidence proves Basic authentication; the complete Basic Authorization value remains secret and server-owned. Turuncu Hat authentication scheme remains unproven.

## TEST Validation

Before enabling real external writes: approve the outstanding sanitized HTTP samples, review mappings, apply SQL through the DBA process, configure capability groups, and validate service-account permissions. Write-enabled TEST must exercise preview, one create, partial source-close failure, close-only retry, concurrent submission, audit evidence, and manual reconciliation using synthetic/non-sensitive records.

The simulation source exposes only fixed non-corporate records for happy/idempotent replay, stale-before-create, Jira failure, unknown Jira outcome, and source-close failure/close-only retry. Claim ownership, transfer completion, and reconciliation are durable workflow transitions exercised against those records rather than fabricated source fields. A real source adapter still requires approved base URL and authentication, bounded list/detail schemas, exact requester fields, status mapping, version/ETag semantics, close/update contract, error/retry semantics, and ownership approval.

Automated integration tests replace `IJiraClient` only inside the test host with private scripted or coordinated doubles. These produce synthetic success, safe retryable failure, unknown outcome, and overlap barriers without sleeps or external I/O. No failure-injection setting, route, header, or production service is added. `FakeJiraClient` remains the configured Fake/Test Jira provider and every `FAKE-*` key is synthetic, not evidence of a real Jira issue.

Release packaging must use `scripts/release/New-ApiDeploymentPackage.ps1`; it preserves runtime directories and validates every packaged relative path and SHA256 against the publish tree.
