# Operational Record to Jira Backend Workflow

## Status and Ownership

The backend foundation is implemented for TEST validation. Codex owns API, services, integration boundaries, persistence, SQL, authorization, audit, release packaging, and backend tests. Claude owns all Blazor/Razor/CSS/UI work and does not need to change this module.

Typed Turuncu Hat and corporate Jira adapters are implemented behind the existing boundaries and remain disabled by default. Paired `Simulation` selects the explicit deterministic no-network Development/Demo/Test workflow harness; legacy `Fake` remains for compatibility. The real Turuncu Hat read-only path is deployed and verified in TEST at source `0ec0376`; Jira creation and Turuncu Hat completion remain blocked until their separate write contracts and activation gate are approved. The canonical deployed evidence is recorded in `docs/24-api-test-deployment-readiness.md`.

## Legacy Workflow Replacement

No legacy Operational Record/Jira PowerShell script exists in this repository. Sanitized legacy contract evidence defines only the retained behaviors documented in `docs/integrations/turuncu-hat-jira-legacy-parity.md`; the API never launches PowerShell.

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

The transfer key is SHA256 over source-record identity plus the complete normalized create mapping: mapping version, project, issue type, summary policy, team, labels, requester/watcher field and resolved account, assignee, and reporter. Create/retry also uses the caller `Idempotency-Key` or a deterministic actor/command/target fallback in `ops.CommandExecutions`. SQL permits exactly one transfer row per Operational Record and enforces unique source IDs, transfer keys, command scopes, and Jira issue keys. Once preview fixes a mapping fingerprint for a record, a different mapping or resolved identity fails with `WorkflowConflict`.

Before create/retry, the API atomically acquires a bounded actor claim (`ClaimedBy`, `ClaimedAt`, `ClaimExpiresAt`). Another actor receives `OperationalRecordAlreadyClaimed`; active transition states receive `WorkflowAlreadyInProgress`. Expiry permits recovery after an abandoned client. Immediately before Jira create and again before source close, the source record is re-fetched with an exact validated numeric source-ID filter and checked for existence, open state, and matching explicit version token or deterministic source-state hash. Changed/closed source data aborts before the external write.

The Jira key is committed before the source close/update starts. A close failure therefore retries only the source stage. Concurrent, repeated, or completed create requests cannot call Jira twice. If a Jira call has an uncertain outcome, `ReconciliationRequired` blocks automatic retry. If the process stops while `CreatingJira` has no persisted key, retry also fails closed for manual reconciliation because remote Jira idempotency has not been proven.

A source refresh may update bounded source fields, but classification is reapplied only while the workflow remains in `Imported`, `Classified`, `NeedsManualReview`, or `Eligible`. Refresh cannot regress `Previewed`, create/close transition states, failures, completion, or reconciliation-required state.

## Security Boundaries

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
