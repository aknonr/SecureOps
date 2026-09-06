# API TEST Deployment Readiness

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
