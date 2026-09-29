# Service Accounts — Windows Acceptance Runner (NOT EXECUTED)

Status: **prepared, not executed.** It was written in a Linux container that has no Windows
authentication, LocalDB or IIS. Nothing here has been run, and none of it counts as pilot acceptance
until a Windows runner records the evidence listed at the end. No corporate server, identity, flag or
data is used; everything is synthetic and local.

Source: branch `feature/service-accounts-continuation-20260929` (HEAD recorded in `HANDOFF.md`).

## Why a Windows runner

- The API host enforces Integrated Security for the platform's SQL stores
  (`SqlPersistenceConfigurationValidator`). Role bundles that carry `ServiceAccounts.*` actions exist
  only in the SQL access store, so allowed HTTP journeys need Windows authentication to SQL.
- On Linux these parts ran: module SQL tests (least-privilege runtime role), a DI-level
  persisted-access composition test, and HTTP denial tests through the real `Program`
  (`ServiceAccountApiCompositionTests`). The allowed HTTP/UI journey below did not run.

## 1. Prerequisites

- Windows host with the pinned .NET SDK (`global.json`), `sqlcmd`, `SqlLocalDB`, and the isolated
  per-user instance `SecureOpsResourcesV1` (same instance the platform's SQL harnesses use).
- A clean clone at the recorded HEAD. `git status` must be clean.

## 2. Build and non-SQL tests

```powershell
git rev-parse HEAD            # must equal the HEAD in HANDOFF.md
dotnet build SecureOps.sln -c Release
dotnet test tests\SecureOps.Tests.Unit -c Release --logger "trx;LogFileName=unit.trx" --results-directory .\evidence
dotnet test tests\SecureOps.Tests.Integration -c Release --logger "trx;LogFileName=integration-nosql.trx" --results-directory .\evidence
```

Expected: build 0 warnings/0 errors. On Linux 2 unit failures and 12 integration failures are
environment-specific and identical to `e997c5b`; on Windows record the actual set and compare it
with `e997c5b` built on the same host.

## 3. Fresh module database and SQL tests

```powershell
powershell -NoProfile -File tests\sql\service-accounts\sa-sql-harness.ps1 -DatabaseSuffix Pilot01
$env:SECUREOPS_SA_SQL_TEST_CONNECTION = 'Server=(localdb)\SecureOpsResourcesV1;Database=SecureOps_SaPilot01;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15'
$env:SECUREOPS_SA_SQL_DIAGNOSTICS = "$PWD\evidence\sa-diagnostics.log"
dotnet test tests\SecureOps.Tests.Integration -c Release --no-build --filter "FullyQualifiedName~ServiceAccounts" --logger "trx;LogFileName=sa-sql.trx" --results-directory .\evidence
```

Expected: all module tests pass on the first run (33 in this source: 30 SQL tests plus the three
HTTP composition tests that need no database). The diagnostics file should contain only the
intentional `Number=51091` entry of the audit-rollback test. Any other entry is the evidence that
was missing for the two unexplained first-run failures: keep the TRX and the log.

Optional least-privilege rerun: map a second local Windows principal to a database user that is only
a member of `svcacct_api_runtime`, set `SECUREOPS_SA_SQL_RUNTIME_CONNECTION` to a connection that runs
as that principal, and rerun the filter on a *new* database. If no second principal is available,
record "NOT RUN" (the Linux run covered it with a SQL login).

## 4. Real API host with persisted access (allowed and denied journeys)

Run the API from the build output against `SecureOps_SaPilot01` with the platform's SQL stores and
Integrated Security. Configuration (environment `Test`; values are local and synthetic):

```text
ASPNETCORE_ENVIRONMENT=Test
ConnectionStrings__SecureOpsDb=Server=(localdb)\SecureOpsResourcesV1;Database=SecureOps_SaPilot01;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15
Access__RepositoryProvider=SqlServer
Audit__Provider=SqlServer
ServiceAccounts__Provider=SqlServer
ServiceAccounts__Reminders__Enabled=false
DemoAuth__Enabled=true                 # identity bridge only (Test/Demo/Development); it grants no rights
Access__DemoCompatibilityEnabled=false # no automatic role bootstrap
```

Identities. Preferred: the platform's Test OIDC provider with four **synthetic** test accounts
(names below). Fallback smoke: the identity bridge's two fixed actors (`platform-admin`,
`team-lead`), which covers rows 1–6 only.

| Test identity | Platform role | Module bundle (created in step 4b) | Module scope grant |
|---|---|---|---|
| `SA-T-ADMIN` | `Admin` (seeded like the platform's own access tests) | `sa-pilot-admin`: View, Administer | none |
| `SA-T-COORD` | none | `sa-pilot-coord`: View, Work, Assign, Verify, Import, Report | Organization `SYN PILOT ORG` |
| `SA-T-MEMBER` | none | `sa-pilot-member`: View, Work | Team `SYN PILOT TEAM` |
| `SA-T-OUTSIDER` | `ReadOnly` | none | none |

Steps:

a. Seed `SA-T-ADMIN` with the platform `Admin` role only (same seeding as
   `SqlAccessTestActors.AdminAsync`); every other right goes through the API.
b. As `SA-T-ADMIN`: `POST /api/v1/access/roles/preview`, then `PUT /api/v1/access/roles` with the
   preview token for each bundle above; approve each pending user via
   `POST /api/v1/access/requests/{id}/approve` with the bundle code and `RoleVersions`.
c. As module administrator (bundle `sa-pilot-admin` assigned to a second admin identity, or to
   `SA-T-ADMIN` if the platform allows it): `POST /api/v1/service-accounts/organizations`,
   `.../teams`, then `.../scope-grants` for coordinator and member.
d. As coordinator: create account `SYNPILOT_A1` in `SYN PILOT ORG`, a request targeted at
   `SYN PILOT TEAM`, stage/preview/commit a synthetic coordination list with coverage `Complete`.

Expected results (capture status code and response body for each):

| # | Caller | Call | Expected |
|---|---|---|---|
| 1 | anonymous | `GET /api/v1/service-accounts/me` | 401 |
| 2 | `SA-T-ADMIN` before bundles | any module route | 403 (no platform role carries module actions) |
| 3 | `SA-T-OUTSIDER` | `GET .../accounts`, `.../accounts/export` | 403 |
| 4 | `SA-T-MEMBER` | `POST .../scope-grants` | 403 |
| 5 | `SA-T-COORD` | `GET .../accounts` | 200, only `SYN PILOT ORG` accounts |
| 6 | `SA-T-COORD` | `GET .../accounts/export` | 200 XLSX; audit `ServiceAccount.AccountsExported`; 4th call within a minute → 429 |
| 7 | `SA-T-MEMBER` | `GET .../work-summary` | 200, `TeamOpenRequests` = 1 |
| 8 | `SA-T-MEMBER` | `GET .../accounts/{A1}` | 200, `permissions.basis` = `Participant` |
| 9 | `SA-T-MEMBER` | update own request notes | 200 |
| 10 | `SA-T-MEMBER` | retarget own request / edit account / create request | 403 |
| 11 | `SA-T-MEMBER` | account of another org by id | 404 (indistinguishable from missing) |
| 12 | `SA-T-COORD` | import commit twice with the same `Idempotency-Key` | same result, no duplicates |
| 13 | `SA-T-ADMIN` | remove Work from `sa-pilot-member` (preview + apply), then row 9 again | 403 on the next call |
| 14 | any | module routes with `ServiceAccounts__Provider=Disabled` and a bundle holder | 503 `ServiceAccountsNotConfigured` |

UI (optional in this round): with the UI pointed at this API, `SA-T-MEMBER` opens
`/service-accounts` and sees "Takibinizdeki işler" first; the account detail shows the participant
notice and only the allowed controls.

## 5. Worker check

With `Hangfire:Enabled=true`, start the Worker once with `ServiceAccounts__Reminders__Enabled=true`
(the recurring job `service-accounts:reminders:v1:<queue>` appears), then restart with
`...Reminders__Enabled=false` and trigger that job from the dashboard. Expected: the job succeeds as a
no-op (no failed or retried jobs, no module SQL), and other recurring jobs are unchanged. The
recurring job is not removed automatically; removing it is an operator decision.

## 6. Evidence to capture

- `git rev-parse HEAD`, `dotnet --info`, SQL Server/LocalDB version.
- All TRX files, `sa-diagnostics.log`, the API and Worker logs for the run.
- For each row in the table: request, status, response body (redact nothing: all data is synthetic).
- `SELECT Action, COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.%' GROUP BY Action;`
- `SELECT program_name, transaction_isolation_level FROM sys.dm_exec_sessions WHERE program_name LIKE '%Service Accounts%';`
  (module sessions use their own pool.)
- A short list of anything that differed from the expected column.
