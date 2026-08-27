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

The `:r` directives require SQLCMD mode and resolve files under `sql/schema`. Migrations 003-007 require successful prerequisites. None assumes empty tables, but 001 and 002 require the target object names to be absent. Existing rows are supported by defaults in 003 and 004; adding non-null columns can lock populated tables while SQL Server backfills defaults. Migrations 005 and 007 can add indexes and must be scheduled and reviewed by the DBA.

There are no down migrations, migration-history table, encompassing transaction, or automatic rollback. A failure after a `GO` can leave a partially applied database. Before execution, the DBA must inventory schemas, tables, indexes, triggers, constraints, and seeded `RoleId`/`RoleCode` values, take an approved backup or recovery point, and stop on any collision. Do not re-run a failed batch without a DBA-authored corrective plan.

## Database Contract

- `audit.AuditLog`; indexes `IX_AuditLog_OccurredAt`, `IX_AuditLog_CorrelationId`; append-only trigger.
- `security.Users`, `Roles`, `RoleAssignments`, `AccessRequests`, `AccessRequestHistory`; active-role and pending-request unique indexes; status checks; no-self-approval and append-only triggers.
- `ops.OperationalRecords`, `JiraTransfers`, `OperationalRecordWorkflowHistory`, `CommandExecutions`; source/Jira/idempotency uniqueness; rowversion, source token, validation time, actor lease fields, status/claim checks, and workflow-history append-only trigger.
- `reporting.ManagementAuditEvents`, `ManagementWorkflowEvents`, and `ManagementOperationalStatus`; limited read views plus reporting indexes on underlying tables.
- `security.ApplicationSessions`; authoritative lifecycle timestamps/reasons, authentication method, access version, and active-session indexes. `reporting.ManagementSessionStatus` exposes limited aggregate fields.

The DBA migration identity needs controlled DDL authority to create schemas/tables/views/indexes/triggers/constraints and DML authority for role seeds. The runtime identity needs only: `INSERT` on `audit.AuditLog`; `SELECT, INSERT, UPDATE` on `security.Users`, `security.RoleAssignments`, `security.AccessRequests`, and `security.ApplicationSessions`; `SELECT` on `security.Roles`; `INSERT` on `security.AccessRequestHistory`; `SELECT, INSERT, UPDATE` on `ops.OperationalRecords`, `ops.JiraTransfers`, and `ops.CommandExecutions`; `INSERT` on `ops.OperationalRecordWorkflowHistory`; and `SELECT` on the three reporting views read by current code. It needs no direct `SELECT` on base audit/history tables, unused `reporting.ManagementSessionStatus`, `DELETE`, DDL, schema ownership, `db_owner`, or `db_ddladmin`. Exact grants are in the release-candidate grant-only script. View/trigger ownership chaining must be verified by the DBA.

## Bootstrap Administrator

The bootstrap is configuration-based with persisted SQL access state. Negotiate principals are stored as the trimmed `ClaimsPrincipal.Identity.Name`, normally `DOMAIN\\account`, in `security.Users.CorporateIdentity`; matching is case-insensitive in application code. Future OIDC uses an opaque SHA256 of issuer plus subject.

On first authenticated access, an unknown principal becomes `Pending` and receives one access request. An exact value in `Access__BootstrapAdministrators__N` is approved with `Admin` by `system:configured-bootstrap`. Demo/Test compatibility separately maps only `demo:platform-admin` to Admin and `demo:team-lead` to Lead when both Demo flags are enabled. Bootstrap approval and role assignment are audited.

An empty database with no configured bootstrap principal and Demo compatibility disabled is locked out. For the first Windows-authenticated TEST start, configure exactly one approved placeholder-replaced corporate principal and keep `Access__AutoCreateRequest=true`. After at least two reviewed, persisted Admin assignments exist, the bootstrap array can be removed and the app restarted; persisted roles remain. Disabled users are never re-bootstrapped.

## Exact TEST Environment Variables

Values in angle brackets require controlled deployment input. All booleans are lower-case strings in IIS environment variables.

| Classification | Environment variable | TEST value |
|---|---|---|
| REQUIRED | `ASPNETCORE_ENVIRONMENT` | `Test` |
| REQUIRED, TEST-ONLY | `Swagger__Enabled` | `true` |
| REQUIRED, TEST-ONLY | `DemoAuth__Enabled` | `true` for current compatibility; `false` for Windows-auth bootstrap validation |
| REQUIRED, TEST-ONLY | `DemoAuth__HeaderName` | `X-SecureOps-Demo-Actor` |
| REQUIRED | `Access__RepositoryProvider` | `SqlServer` after migrations 001-007 |
| REQUIRED | `Access__AutoCreateRequest` | `true` |
| REQUIRED, TEST-ONLY | `Access__DemoCompatibilityEnabled` | same enablement decision as `DemoAuth__Enabled` |
| CONDITIONAL REQUIRED | `Access__BootstrapAdministrators__0` | `<DOMAIN\\approved-bootstrap-account>` when validating first Windows Admin against an empty database |
| PRODUCTION-FUTURE | `Access__OidcSubjectClaimType` / `Access__OidcIssuerClaimType` | `sub` / `iss`; unused until approved OIDC |
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
| REQUIRED | `Jira__Provider` | `Disabled`, paired `Simulation` for operator-visible synthetic TEST, legacy `Fake` for automated compatibility, or contract-gated `Corporate` only after separate approval |
| REQUIRED | `Jira__ProjectKey` / `Jira__IssueType` / `Jira__MappingVersion` | `<approved TEST project key>` / `<approved issue type>` / `<reviewed mapping version>` |
| REQUIRED | `Jira__UnresolvedRequesterPolicy` / `Jira__SummaryMaxLength` | `Block` / `255` |

| REQUIRED | `Audit__Provider` / `Audit__FailClosed` / `Audit__RequirePersistentStoreInProduction` | `SqlServer` / `true` / `true` |
| REQUIRED | `Audit__Queue__Enabled` / `Audit__Queue__Capacity` / `Audit__Queue__FullBehavior` / `Audit__FlushIntervalSeconds` | `true` / `1000` / `FailClosed` / `1` |
| REQUIRED, RUNTIME-ONLY | `ConnectionStrings__SecureOpsDb` | `Server=tcp:<SQL_FQDN>,<SQL_PORT>;Database=<DATABASE_NAME>;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;Application Name=SecureOps.Api;Connect Timeout=15` |
| REQUIRED CURRENT | `ReverseProxy__ForwardedHeaders__Enabled` | `false` until exact API proxy behavior and source IPs are confirmed |
| CONDITIONAL | `ReverseProxy__ForwardedHeaders__TrustedProxyIps__0` | `<exact trusted API proxy IP>` only when forwarding is explicitly enabled |

`Simulation` source records and `SIM-*` Jira keys are synthetic TEST evidence only. The provider has fixed scenarios, performs no network I/O, must be selected on both sides, and fails startup outside Development/Demo/Test. `Fake`/`FAKE-*` remains a legacy automated-test compatibility path.

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

Exact order: verify ZIP SHA256 and payload manifest; take database recovery point; DBA preflight and run 001, 002, 003, 004, 005, 006, 007; verify objects/seeds/triggers/views; grant runtime permissions; provision and ACL the server-owned Data Protection key ring; preserve server `web.config` and `appsettings*.json`; back up current application payload; apply reviewed IIS environment-variable delta; replace application payload without flattening directories; start/recycle only in the approved window; run the read-only smoke script.

Application rollback restores the prior binaries and prior server configuration while leaving additive database objects in place. Database rollback has no scripted path: stop deployment and use the DBA-approved restore/corrective-migration process. Never drop audit or history data as an application rollback step.

## Deployed TEST Evidence Record

This section is intentionally blank until an operator completes a deployment. Package creation, build output, or a release-candidate directory is not deployment evidence.

| Evidence | Operator-recorded value |
|---|---|
| API source SHA | `<not deployed/recorded>` |
| UI source SHA | `<not deployed/recorded>` |
| API package SHA256 | `<not deployed/recorded>` |
| UI package SHA256 | `<not deployed/recorded>` |
| Deployed timestamp (UTC) | `<not deployed/recorded>` |
| Server / environment | `<not deployed/recorded>` |
| Server-owned configuration baseline ID | `<not deployed/recorded>` |
| Rollback package / baseline | `<not deployed/recorded>` |
| Read-only smoke-test result and evidence reference | `<not deployed/recorded>` |
