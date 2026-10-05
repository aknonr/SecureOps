# Service Accounts — Integration Handoff for Codex (2026-09-29)

Single source for integrating the Service Accounts module. It **supersedes** the earlier
`service-accounts-20260928-55ab73e-full.bundle` (branch `feature/service-accounts-20260928`,
`55ab73e`) and the intermediate continuation bundle at `57b4761`. Do not apply `55ab73e` or its
commits: their intended delta is already contained here, reconciled on the integrated baseline.

Nothing here is deployed, packaged, activated or accepted for pilot. No corporate SQL, source, Jira,
AD or SMTP call was made; no flag was enabled; no migration number was assigned.

## Source identity

| Item | Value |
|---|---|
| Branch | `feature/service-accounts-continuation-20260929` |
| Code HEAD verified by the results below | `b8aad7edd68e6e35438ba0b8516ea25dc4e2ad3b` |
| Branch tip | the docs-only commit adding this file (reported with the recovery bundle) |
| Integrated baseline (ancestor) | `e997c5b68cebcd23716860a9b06fdc25ebbb4493` (tested product `deda8486b57c04a23aba203c0e79f96b746102e9`) |
| Restored Codex WIP (checkpoint) | `c63d1bcd413189705e3b4caf8c3d90111e639492` = Codex's `service-accounts-integrated-wip.patch` (SHA-256 `4C1B5F50…D276D`) on `e997c5b`, applied unchanged after CRLF→LF |
| Merge base with integration | `e997c5b`; the branch contains no other platform change |

Codex's SDM, In Use, OCO, SCCM, Windows Service lifecycle and process-lock work is untouched
(see the shared-file list: all changes are additive module wiring).

## Commits after the checkpoint (each under the 1 000-line review cap, OpenAPI excluded)

| Commit | Slice |
|---|---|
| `53641a7` | Shared write gate: every module write takes `svcacct:import-commit` Shared; fixes the VerifyAction/import deadlock (1205) |
| `8c2e995` | Explicit import coverage; absence only from a validated complete list |
| `bca5375` | Participant write boundary; manual identity stays provisional; safe failure origin logging |
| `fbe4cb7` | Entry work summary; OpenAPI regenerated from the combined app |
| `d42ede5` | Least-privilege test runs; runtime gate error 51312 |
| `444b7f7` | Persisted-access composition test (DI level, no access wrapper) |
| `761cb20` | Monthly/custom report periods; scoped, capped, audited, rate-limited list export |
| `faa0815` | On-demand directory observation tab (platform component and permission reused) |
| `57b4761` | Continuation record |
| `cd51dbf` | Module uses its own connection pool and READ COMMITTED on open (pooled isolation leak) |
| `4c27bbb` | Stale reminder job activates as no-op after disable; unused grants removed; reverse gate test |
| `b8aad7e` | HTTP composition tests through the real `Program`; Windows runner procedure and LocalDB harness |

## Shared files changed versus `e997c5b` (all additive, 0 deleted lines)

| File | Change |
|---|---|
| `src/SecureOps.Api/Program.cs` | `using` + `AddServiceAccountsApi(configuration)` (+2) |
| `src/SecureOps.Worker/Program.cs` | `using` + `AddServiceAccountsWorker(configuration, jobsConfigured)` (+2) |
| `src/SecureOps.Ui/Program.cs` | one API client registration (+1) |
| `src/SecureOps.Infrastructure/Access/AccessActionCatalog.cs` | spread of the 7 module actions; no role changed or seeded (+1) |
| `src/SecureOps.Ui/Shared/NavMenu.razor` | one entry gated on `ServiceAccounts.View` (+8) |
| `src/SecureOps.Ui/Services/UiProblemFactory.cs` | one delegation line (+1) |
| `src/SecureOps.Ui/Pages/_Host.cshtml` | label binding limited to `.sa-page` (+16) |
| `src/SecureOps.Ui/README.md` | six route rows (+6) |
| `docs/contracts/secureops-api-v1.openapi.json` | regenerated; semantic diff vs `e997c5b`: 0 removed/changed, 44 module paths added |

After merging, regenerate the snapshot on the merged tree with `SECUREOPS_UPDATE_OPENAPI=1` and
re-check the semantic diff rather than resolving it by hand. The module's rate-limit policy
(`ServiceAccountExport`, 3/min per user) is registered from `AddServiceAccountsApi`; no platform
policy changed.

## SQL candidate and role grants (unnumbered; outside release discovery)

| File | SHA-256 |
|---|---|
| `sql/pending/service-accounts/SA-001-service-accounts.sql` | `af270a05ce8a1063a0eab7c3591e647d4ad6e04d1476f3d2322d2d1cc879ed5e` |
| `sql/pending/service-accounts/SA-API-permissions.sql` | `b813cdcd923bfe28938f0d1da09b517de6e7b4b094b4252eb0e531db319a206e` |
| `sql/pending/service-accounts/SA-Worker-permissions.sql` | `b1dbf17999a79cbf519dd0358eaaffc56fb927553ff69ad4626080ea911a2b5d` |

- Candidate: schema `svcacct`, requires 001, one transaction from schema creation to the last
  trigger (Codex fix), refuses replay, no seed, no down script. Codex reserves the migration number.
- `svcacct_api_runtime`: SELECT/INSERT/UPDATE on Organizations, Teams, People, ScopeGrants,
  Accounts, OwnershipAssignments, Handovers, WorkRequests, ActionEvents, Findings,
  IdentityTransitions, ImportBatches, ReminderOutbox; SELECT/INSERT on Communications,
  PersonAliases, AccountAliases, ExternalRecords, ExternalRecordLinks, CommunicationAccounts,
  AccountObservations, Evidence, ReportSnapshots, History; SELECT/INSERT/DELETE on ImportRows (DELETE
  limited to uncommitted rows by trigger); SELECT on `security.Users`; INSERT on `audit.AuditLog`.
  `svcacct.TeamMemberships` gets nothing (unused). `sp_getapplock` needs only `public`.
- `svcacct_worker_runtime`: SELECT on Accounts and WorkRequests; SELECT/INSERT/UPDATE on
  ReminderOutbox. Hangfire permissions are separate and unchanged.
- Neither script assigns a member. The DBA maps the approved API/Worker principals.
- DBAs will see module sessions with program name `<configured Application Name> / Service Accounts`
  (separate pool, READ COMMITTED on open).

## Test results (Linux container, SDK 10.0.112 with `-p:LangVersion=13`, SQL Server 2022)

| Check | Result at `b8aad7e` |
|---|---|
| `dotnet build SecureOps.sln -c Release --no-incremental` | 0 warnings, 0 errors |
| Unit (all) | 1566/1568; failing: `AuditConfigurationValidatorTests.Validate_WhenProductionFailOpen_Throws`, `SccmFailureEvidenceTests.StagedInvocations_…` — both fail identically on `e997c5b` here |
| Module unit | 58/58 (incl. Worker composition 5/5) |
| Integration (all) | 304 pass / 61 skip / 12 fail; failing set identical to `e997c5b` on this host (DPAPI key ring, image codec) |
| Module integration | 33/33 on new database `SecureOps_SaFinal2`, first run, repository as a member of `svcacct_api_runtime` only (30 SQL + 3 HTTP composition) |
| Worker role | Worker statements succeed as a `svcacct_worker_runtime` member; History read denied |
| Reproduction | 5 cycles of new DB + `--no-incremental` build + first run: 28/28 each |

Unresolved (not claimed fixed): two single first-run failures from 2026-09-28
(`SentSnapshotNeverChanges_…`, message lost; `ParticipantTeam_…`, `CreateRequest` returned
"persistence unavailable" — TRX retained, exception type not captured). No deadlock was recorded at
those times; they did not reproduce. The next occurrence is diagnosable: the module logs SQL number
or failure type plus a safe `Origin`, and the fixture writes them to `SECUREOPS_SA_SQL_DIAGNOSTICS`.
See PROGRESS-HISTORY.md (dated records) and PROGRESS.md (current state).

Not run here: Windows toolchain, LocalDB/Integrated Security, IIS, allowed HTTP/UI journeys with the
SQL access store, browser journey on the real composition, desktop Excel.

## Windows acceptance commands (unexecuted; full procedure in `WINDOWS-ACCEPTANCE.md`)

```powershell
git rev-parse HEAD
dotnet build SecureOps.sln -c Release
dotnet test tests\SecureOps.Tests.Unit -c Release --logger "trx;LogFileName=unit.trx" --results-directory .\evidence
powershell -NoProfile -File tests\sql\service-accounts\sa-sql-harness.ps1 -DatabaseSuffix Pilot01
$env:SECUREOPS_SA_SQL_TEST_CONNECTION = 'Server=(localdb)\SecureOpsResourcesV1;Database=SecureOps_SaPilot01;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15'
$env:SECUREOPS_SA_SQL_DIAGNOSTICS = "$PWD\evidence\sa-diagnostics.log"
dotnet test tests\SecureOps.Tests.Integration -c Release --no-build --logger "trx;LogFileName=integration.trx" --results-directory .\evidence
```

Then the API/Worker journey of `WINDOWS-ACCEPTANCE.md` sections 4–5 (persisted role bundles, module
scope grants, 14-row allow/deny matrix, stale-job no-op), capturing the listed evidence.

## Remaining for Codex / owners

- Merge onto the integration line, regenerate OpenAPI, run the platform's own Windows regression.
- Reserve the migration number; DBA review of the candidate and both role scripts.
- Corporate role bundles and first scope grants (people decision; none seeded).
- Execute `WINDOWS-ACCEPTANCE.md` and record evidence; pilot acceptance is not claimed.
- Out of scope this iteration by decision: ML.NET and manager-defined custom fields.
