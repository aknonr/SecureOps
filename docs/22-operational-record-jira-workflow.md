# Operational Record to Jira Backend Workflow

## Status and Ownership

The backend foundation is implemented for TEST validation. Codex owns API, services, integration boundaries, persistence, SQL, authorization, audit, release packaging, and backend tests. Claude owns all Blazor/Razor/CSS/UI work and does not need to change this module.

Real Operational Record and Jira adapters are intentionally deferred. Local and current default runtime providers are fakes and perform no external network calls.

## Legacy Workflow Replacement

No legacy Operational Record/Jira PowerShell script exists in this repository. The known behavior was supplied as requirements and is source material only; the API never launches PowerShell.

| Legacy step | SecureOps backend component |
|---|---|
| Authenticate/query active records | `IOperationalRecordClient` |
| Read bounded source fields | `OperationalRecordSourceItem` and `OperationalRecordService` |
| Operator selects a record | typed GET endpoints; UI deferred |
| Resolve requester | `IRequesterResolver.ResolveExactAsync` |
| Apply fixed Jira mapping | `IJiraIssueDraftService` |
| Review proposed fields | `POST .../{id}/jira-preview` |
| Create Jira task | authorized `IJiraTransferService` plus `IJiraClient` |
| Persist Jira key | `IOperationalRecordRepository.RecordJiraCreatedAsync` |
| Close/update source record | `IOperationalRecordClient.CloseAsync` after key persistence |
| Retry partial success | `POST .../{id}/retry`; resumes source close when Jira exists |

## Workflow

`Imported -> Classified -> NeedsManualReview | Eligible -> Previewed -> CreateRequested -> CreatingJira -> JiraCreated -> ClosingOperationalRecord -> Completed`

Failures persist as `JiraCreateFailed` or `OperationalRecordCloseFailed`. No approved classification rules exist, so the implemented classifier always selects `NeedsManualReview`. No endpoint automatically promotes a record to `Eligible`.

## Claims, Idempotency, Freshness, and Retry

The transfer key is SHA256 over source-record identity plus mapping version. Create/retry also uses the caller `Idempotency-Key` or a deterministic actor/command/target fallback in `ops.CommandExecutions`. SQL permits exactly one transfer row per Operational Record and enforces unique source IDs, transfer keys, command scopes, and Jira issue keys. Once preview fixes a mapping version for a record, a different mapping/version pair fails with `WorkflowConflict`.

Before create/retry, the API atomically acquires a bounded actor claim (`ClaimedBy`, `ClaimedAt`, `ClaimExpiresAt`). Another actor receives `OperationalRecordAlreadyClaimed`; active transition states receive `WorkflowAlreadyInProgress`. Expiry permits recovery after an abandoned client. Immediately before Jira create and again before source close, the source record is re-fetched and checked for existence, open state, and matching explicit version token or deterministic source-state hash. Changed/closed source data aborts before the external write.

The Jira key is committed before the source close/update starts. A close failure therefore retries only the source stage. Concurrent, repeated, or completed create requests cannot call Jira twice. If a Jira call has an uncertain outcome, `ReconciliationRequired` blocks automatic retry. If the process stops while `CreatingJira` has no persisted key, retry also fails closed for manual reconciliation because remote Jira idempotency has not been proven.

## Security Boundaries

- Browser users never provide integration credentials.
- No password, token, authorization header, or raw remote response is stored or returned.
- Requester resolution is exact only. Ambiguous matches always fail closed.
- Unresolved requesters are blocked by default; `ProceedUnassigned` must be an explicit approved policy.
- Jira creation and retry require server-side capability policies.
- The working exact AD/PAM-style identity lookup provider is unchanged.
- Real source close/update is a state-changing external integration and remains disabled until separately approved.

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

`sql/schema/002-operational-record-jira-workflow.sql` creates the base workflow tables. Offline migration 003 adds source/claim metadata and `ops.CommandExecutions`; migration 004 adds access mutation versions. The application does not run migrations. DBA approval and execution of migrations 001-004 are required before selecting `SqlServer`.

Minimum runtime permissions are `SELECT`, `INSERT`, and `UPDATE` on these three `ops` tables; no `DELETE`, DDL, schema-owner, or migration permission is required. Audit-store permissions remain separate.

## Runtime Configuration

Non-secret keys:

- `OperationalRecords:SourceProvider` (`Fake` only now)
- `OperationalRecords:RepositoryProvider` (`InMemory` or `SqlServer`)
- `OperationalRecords:MaxImportCount` (1-500)
- `OperationalRecords:ClaimLeaseSeconds` (30-900)
- `CommandIdempotency:ExecutionLeaseSeconds` (30-900)
- `CommandIdempotency:MaxKeyLength` (32-256)
- `Jira:Provider` (`Fake` only now)
- `Jira:ProjectKey`
- `Jira:IssueType`
- `Jira:MappingVersion`
- `Jira:UnresolvedRequesterPolicy` (`Block` or `ProceedUnassigned`)
- `Jira:SummaryMaxLength` (32-255)
- `ConnectionStrings:SecureOpsDb` when SQL persistence is selected

Future integration authentication must use approved server-side enterprise identity/secret facilities. No credential shape is defined in source.

## TEST Validation

Before enabling a real adapter: review source and Jira contracts, approve field mappings/classification rules, apply SQL through DBA process, configure capability groups, validate service-account permissions, and prove a deterministic Jira reconciliation mechanism. TEST must exercise preview, one create, partial source-close failure, retry, concurrent submission, audit evidence, and correlation IDs with synthetic/non-sensitive records.

Release packaging must use `scripts/release/New-ApiDeploymentPackage.ps1`; it preserves runtime directories and validates every packaged relative path and SHA256 against the publish tree.
