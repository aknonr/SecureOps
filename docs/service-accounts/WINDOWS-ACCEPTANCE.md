# Service Accounts — Windows Acceptance Runner (NOT EXECUTED)

Status: **prepared, not executed.** The pilot journey (section 4) is **BLOCKED** until approved
TEST identities exist. It was written in a Linux container that has no Windows
authentication, LocalDB or IIS. Nothing here has been run, and none of it counts as pilot acceptance
until a Windows runner records the evidence listed at the end. No production server, flag or data
is used; all business data is synthetic. The only non-local input is the set of approved TEST
identities of section 4.

Source: the pinned handoff `b4fdf8d` (`HANDOFF.md`) plus the follow-up branch recorded in
`FOLLOWUP-20260930.md`; run this procedure at the follow-up HEAD.

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

Expected: all module tests pass on the first run (36 at the follow-up HEAD: 33 SQL tests plus the three
HTTP composition tests that need no database). The diagnostics file should contain only the
intentional `Number=51091` entry of the audit-rollback test. Any other entry is the evidence that
was missing for the two unexplained first-run failures: keep the TRX and the log.

Optional least-privilege rerun: map a second local Windows principal to a database user that is only
a member of `svcacct_api_runtime`, set `SECUREOPS_SA_SQL_RUNTIME_CONNECTION` to a connection that runs
as that principal, and rerun the filter on a *new* database. If no second principal is available,
record "NOT RUN" (the Linux run covered it with a SQL login).

## 4. Pilot journey: normal authentication and module authorization

This is the only journey that can count toward module acceptance. It uses the API's normal
authentication (OIDC), persisted approval, role bundles and module scope grants — nothing else.

**Precondition — approved TEST identities.** Four identities issued by the organization's
approved TEST OIDC provider, each with its real claims (issuer, subject, login name), approved for
this test: a platform administrator, a module coordinator, a team member, and an outsider. Do not
create them, share their credentials or put their names in the repository; record only the
role each played.

**If these identities are not available, this section is BLOCKED.** Record "BLOCKED: approved TEST
identities unavailable" in the evidence and stop here. The local smoke test in section 4A never
replaces this section.

Configuration: the platform's normal Test/pilot settings from
`docs/25-real-user-pilot-management-reporting-and-dotnet10.md` (OIDC enabled, `DemoAuth__Enabled=false`,
`Access__DemoCompatibilityEnabled=false`, SQL access/session/audit stores with Integrated Security,
first-admin bootstrap only through the documented `BootstrapAdmin__*` gate), plus the module:

```text
ConnectionStrings__SecureOpsDb=<Integrated Security connection to the database from step 3>
ServiceAccounts__Provider=SqlServer
ServiceAccounts__Reminders__Enabled=false
```

Rights are given only through the product:

a. The platform administrator is the documented first OIDC Admin (bootstrap gate) or an already
   approved administrator. No role assignment by SQL.
b. The administrator creates the module bundles with `POST /api/v1/access/roles/preview` and
   `PUT /api/v1/access/roles` (preview token), and approves each pending identity with
   `POST /api/v1/access/requests/{id}/approve` giving the bundle code and its reviewed version:

   | Pilot role | Bundle | Actions |
   |---|---|---|
   | module administrator | `sa-pilot-admin` | View, Administer |
   | coordinator | `sa-pilot-coord` | View, Work, Assign, Verify, Import, Report |
   | team member | `sa-pilot-member` | View, Work |
   | outsider | none (platform `ReadOnly`) | — |

c. The module administrator creates `SYN PILOT ORG` and `SYN PILOT TEAM`
   (`POST /api/v1/service-accounts/organizations`, `.../teams`) and the scope grants
   (`POST /api/v1/service-accounts/scope-grants`): coordinator → Organization `SYN PILOT ORG`,
   member → Team `SYN PILOT TEAM`.
d. The coordinator creates `SYNPILOT_A1` in `SYN PILOT ORG`, a request targeted at
   `SYN PILOT TEAM`, and imports a synthetic coordination list with coverage `Complete`.

Expected results (capture request, status and response body for each):

| # | Caller | Call | Expected |
|---|---|---|---|
| 1 | no token | `GET /api/v1/service-accounts/me` | 401 |
| 2 | platform administrator without a module bundle | any module route | 403 (no platform role carries module actions) |
| 3 | outsider | `GET .../accounts`, `.../accounts/export` | 403 |
| 4 | team member | `POST .../scope-grants` | 403 |
| 5 | coordinator | `GET .../accounts` | 200, only `SYN PILOT ORG` accounts |
| 6 | coordinator | `GET .../accounts/export` | 200 XLSX; audit `ServiceAccount.AccountsExported` with the row count; 4th call within a minute → 429 |
| 7 | team member | `GET .../work-summary` | 200, `TeamOpenRequests` = 1 |
| 8 | team member | `GET .../accounts/{A1}` | 200, `permissions.basis` = `Participant` |
| 9 | team member | update own request notes | 200 |
| 10 | team member | retarget own request / edit account / create request | 403 |
| 11 | team member | account of another organization by id | 404 |
| 12 | coordinator | import commit twice with the same `Idempotency-Key`, then re-stage the same file | same result; re-stage returns `replay: true`; no duplicates |
| 13 | platform administrator | remove Work from `sa-pilot-member` (preview + apply), then row 9 again | 403 on the next call |
| 14 | coordinator | report a `Review` action with record kind `Closure` | 400 `ClosureKindNotAllowed` |
| 15 | coordinator | weekly live report, sent `Manager` snapshot, month and date-range reports, snapshot XLSX/PDF | 200; snapshot unchanged after a late entry; creator shown |
| 16 | any bundle holder | module routes with `ServiceAccounts__Provider=Disabled` | 503 `ServiceAccountsNotConfigured` |

UI (same identities): the team member opens `/service-accounts` and sees "Takibinizdeki işler"
first; the account detail shows the participant notice and only the allowed controls.

## 4A. Local smoke test with the identity bridge (NOT acceptance)

Purpose: check that a local host starts and routes before TEST identities are available.
**It is not corporate or pilot authorization acceptance and must be reported as "local smoke".**
The Test-environment bridge (`DemoAuth__Enabled=true`, `Access__DemoCompatibilityEnabled=false`)
authenticates only two fixed local actors and grants nothing; rights still come from bundles and
module grants as in 4b–4c. Only rows 1–6 of the table can be exercised. The automated equivalent of
rows 1–2 runs without SQL in `ServiceAccountApiCompositionTests`.

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
