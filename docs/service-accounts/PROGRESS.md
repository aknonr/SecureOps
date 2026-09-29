# Service Accounts — Progress, Verification and Handoff

Single progress record for the module (design: [README.md](README.md), rules: [SPEC.md](SPEC.md)).
Branch `feature/service-accounts-20260928`, temporary baseline `a3037175bb0bb9ecc7ab5c36c7c28607726469ce`
(verified UI handoff commit). The integrated branch was published afterwards:
`feature/sdm-integrated-test-20260928` at `e997c5b68cebcd23716860a9b06fdc25ebbb4493`, tested product
`deda8486b57c04a23aba203c0e79f96b746102e9` (an ancestor of that head). `a3037175` is **not** an
ancestor of the integrated head, so this branch needs **reconciliation by Codex, not replacement**;
it has not been merged or rebased here. Not deployed, no release package, no live flag, no SQL
activation, no corporate SQL/source/Jira/AD/SMTP call.

## Pilot-readiness iteration 2026-09-29 (current state)

Integration source, results and Windows commands: [HANDOFF.md](HANDOFF.md) (supersedes the
`55ab73e` bundle). Windows runner: [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md) — **not executed**.
This round: pooled isolation leak fixed (`cd51dbf`); stale reminder job no-op, unused grants removed,
reverse gate test (`4c27bbb`); HTTP composition tests through the real `Program` (`b8aad7e`). At
`b8aad7e`: build 0/0, unit 1566/1568 (2 baseline), integration failing set identical to `e997c5b`,
module 33/33 on a new database under the restricted role. The two first-run failures remain
unexplained (see below). The tables in the next section describe the previous round.

## Continuation 2026-09-29 (sole implementation owner)

Codex stopped Service Accounts edits; this module is continued by one owner. Final platform
integration remains with Codex. Everything below is local only: no push, merge, deployment,
package, flag, migration number, corporate SQL/source/Jira/AD/SMTP call.

| Item | Value |
|---|---|
| Handoff source | branch `feature/service-accounts-sdm-integration-20260929` at `e997c5b68cebcd23716860a9b06fdc25ebbb4493` + WIP patch (SHA-256 `4C1B5F50…D276D`, bundle `99AB894D…6ADD`, both verified) |
| Restore | fresh clone of the bundle; patch applied after CRLF→LF normalization (lossless); 127 paths = handoff inventory (OpenAPI intentionally absent) |
| Continuation branch | `feature/service-accounts-continuation-20260929` (new, local) |
| Checkpoint commit | `c63d1bcd413189705e3b4caf8c3d90111e639492` (WIP exactly as handed over) |
| Codex evidence carried as prior results (not rerun) | combined build 0/0; Worker composition 3/3; SA SQL 20/20 on two fresh DBs; injected install failure left no module objects |

Changes after the checkpoint (each commit below the 1 000-line review cap, OpenAPI excluded):

| Commit | Change |
|---|---|
| `53641a7` | VerifyAction root cause (deadlock 1205 with import commit) fixed by a shared write gate; deterministic reproduction test |
| `8c2e995` | Explicit import coverage (Unknown/Partial/Complete + validated population); absence only from a complete list |
| `bca5375` | Participant write boundary (visibility ≠ authority); manual accounts stay provisional; safe failure origin logging |
| `fbe4cb7` | Entry work summary ("Takibinizdeki işler") on the list and team work pages; OpenAPI regenerated from the combined app |
| `d42ede5` | Least-privilege runs as synthetic API/Worker role members; gate error 51312 |
| `444b7f7` | Persisted-access composition test without access wrappers |
| `761cb20` | Monthly/custom report periods; scoped, capped, audited, rate-limited account-list XLSX export |
| `faa0815` | On-demand directory observation tab reusing the platform component and permission |

Actual results in this container (Linux, SDK 10.0.112 with `-p:LangVersion=13`, SQL Server 2022 container):

| Check | Result |
|---|---|
| `dotnet build SecureOps.sln -c Release` | 0 warnings, 0 errors |
| Unit (all) | 1563/1565; the 2 failures (`AuditConfigurationValidatorTests.Validate_WhenProductionFailOpen_Throws`, `SccmFailureEvidenceTests.StagedInvocations…` PowerShell on Linux) also fail on clean `e997c5b` |
| Module unit | 55/55 (incl. Worker composition 3/3) |
| Integration (all) | 299 pass / 61 skip / 12 fail; failing set identical to clean `e997c5b` on this host (DPAPI key ring, image codec) |
| Module SQL | 28/28 on fresh `SecureOps_SaCont2`, first run, repository connected as a member of `svcacct_api_runtime` only; negative control with one denied grant fails with SQL 229 |
| Worker role | Worker statements succeed as a synthetic `svcacct_worker_runtime` member; History read denied |
| OpenAPI | regenerated; semantic diff vs `e997c5b`: 0 removed/changed, 44 module paths added |
| Not run | Windows toolchain, LocalDB/Integrated Security, IIS, HTTP pipeline with real auth, browser journey against the real composition, desktop Excel |

The persisted-access test composes the production registrations (`AddSecureOpsInfrastructure` with
`Access:RepositoryProvider=SqlServer`, SQL audit, `ApplicationAccessService`) and the module; roles are
reviewed bundles created by a synthetic platform administrator, scope comes from module grants, and a
bundle change is effective on the next call. It does not exercise HTTP or Integrated Security: the API
host refuses SQL logins at startup and Linux has no Windows authentication. The earlier browser
harness is not reused as authorization proof.

Shared changes needing Codex coordination: regenerated `docs/contracts/secureops-api-v1.openapi.json`;
two `src/SecureOps.Ui/README.md` route descriptions. No change to `Program.cs` files, platform
policies, rate-limit policies, In Use, SDM/OR, OCO, SCCM, Windows Service lifecycle or process locking.
The export rate limit is a new module-owned policy registered from `AddServiceAccountsApi`.

Backlog decisions (not implemented, by design):

- **Custom fields** (admin-defined, for accounts and requests): only `ServiceAccounts.Administer`
  defines them; values are data, never authority or ownership. Needs a schema addition in the
  unnumbered candidate and a review slice of its own.
- **ML.NET / "yapay zeka önerisi"**: first explainable, rule-based suggestions (overdue, no reply
  after N days, missing owner, plan without date), each showing its rule. ML.NET only through an
  ADR on the Phase 7 track, local models on synthetic/approved data, no external AI service, and a
  suggestion never changes data by itself.

### Reconciliation preview (read-only, nothing merged)

- Merge base with the integrated head: `5c986a96f1f6639e47bf1432a87c3ac55e98054d`; the integrated
  side has 7 commits after it.
- Both sides changed the Jira-only UI files because this branch starts after the three Jira UI
  commits (`6075ca8`, `db3be19`, `a303717`) that the integrated branch carries in its own form.
  Those files are not Service Accounts work and must follow the integrated side.
- `git merge-tree --write-tree HEAD e997c5b` reports conflicts only in
  `docs/contracts/secureops-api-v1.openapi.json` (generated; regenerate after reconciliation with
  `SECUREOPS_UPDATE_OPENAPI=1`) and `src/SecureOps.Ui/README.md` (six added route rows).
- `src/SecureOps.Worker/Program.cs` changed on both sides without a textual conflict; the
  `AddServiceAccountsWorker` line must be re-checked against the integrated composition.

### Push status

`git push -u origin feature/service-accounts-20260928` was refused: `The requested URL returned
error: 403` (GitHub App access for the repository is not granted to this session). Push attempts
are stopped; the branch is preserved in the session workspace and in a verified Git bundle outside
the repository.

## Stages

| Stage | Result | Commit |
|---|---|---|
| S0 design note, sanitized spec | Done | `291c7f0` |
| S1 SQL candidate, domain rules, scope | Done | `291c7f0` |
| S2 import pipeline + API | Done | `0e71d3f` |
| S3 accounts, workflow commands, evidence | Done | `486fd32` |
| S4 reports, immutable snapshots, XLSX/PDF | Done | `746a2fa` |
| S5 reminder outbox, drafts, Hangfire schedule | Done | `571196e` |
| S6 Blazor UI | Done | `db1444f` |
| S7 verification fixes (harness browser run, reconciliation) | Done | `502e66c`, `f32515c`, `cf83a3e` |

Defects found by verification and fixed on this branch: case-colliding Dapper parameters in
scope-grant insert; READPAST claim failing on pooled SERIALIZABLE connections; handover cohort
double count (source row + handover row); legacy projection using confirmed persons only; UI
list fetch dropped by a concurrent access refresh; missing accessible names on MudBlazor 6
fields; import provenance autocomplete not binding; spec-inconsistent UI labels ("OR required"
for any deletion report, "date required" for performed reports).

## Verification (Linux container, 2026-09-28)

Toolchain: .NET SDK 10.0.112 building `net8.0` with `-p:LangVersion=13` (the SDK's C# 14 default
changes `Reverse()` overload resolution in existing code; the Windows toolchain should be checked
by Codex). SQL: disposable SQL Server 2022 container, databases created by
`tests/sql/service-accounts/sa-sql-harness.sh` (001–024 + candidate, replay refused).

| Check | Result |
|---|---|
| `dotnet build SecureOps.sln -c Release` | 0 warnings, 0 errors |
| Unit tests (all) | 1477/1478; the 1 failure (`AuditConfigurationValidatorTests.Validate_WhenProductionFailOpen_Throws`) also fails on the baseline |
| Module unit tests | 49/49 (domain, parser, metrics, reminders, export, UI transport/wording) |
| Integration tests (all) | 286 passed, 56 skipped, 12 failed; the failing set (DPAPI key ring, image codec) is identical to the baseline run on this host |
| Module SQL tests (`SECUREOPS_SA_SQL_TEST_CONNECTION`) | 19/19 in the recorded runs; one earlier run had 3 failures. Cause established on 2026-09-29 as an import-commit/VerifyAction deadlock and fixed (see below) |
| OpenAPI snapshot | regenerated with `SECUREOPS_UPDATE_OPENAPI=1`; purely additive (`git diff --histogram`: +10 558 / −0) |
| Browser journey `tests/browser/service-accounts.cjs` — **temporary harness, not the production API composition** | 13/13 steps against the harness below, Chromium, 1440 px / 390 px, dark scheme, 200 % zoom (720×450 @2x), no page errors, no unnamed module fields. Not yet run against the real Demo/Test API |
| Private reconciliation (supplied package + workbook, private DB, results outside the repository) | see scenarios 1–5 below |

### Temporary browser harness (not production composition)

The API refuses SQL logins (`Integrated Security` is enforced) and the Linux container has no
Windows authentication, so the real API could not be started against SQL here. The browser run
used an **uncommitted** scratch host (kept outside the repository, source included in the handoff
folder, `DemoHost.cs`):

1. `WebApplicationFactory<Program>` of the real API, additionally bound to Kestrel on
   `http://localhost:5000`, environment `Demo`, `DemoAuth:Enabled=true`,
   `Access:DemoCompatibilityEnabled=true`, `Audit:Provider=InMemory` (platform access and sessions
   stay in memory), `ServiceAccounts:Provider=SqlServer` with a disposable database connection
   supplied through the `SA_DEMO_DB` environment variable.
2. `IApplicationAccessService` is wrapped: for `demo:platform-admin` it adds all seven
   `ServiceAccounts.*` actions, for `demo:team-lead` View/Work/Assign/Verify/Report; the user id is
   mapped to (or inserted as) a `security.Users` row in the disposable database.
3. `IAccessRepository.GetUserAsync(identity)` is wrapped to return that persisted id, so scope
   grants reference the same user.
4. UI: two Demo UI hosts on `https://localhost:5100` (`DemoMode:ApiDemoActor=platform-admin`) and
   `https://localhost:5101` (`team-lead`) with a local development certificate.
5. Database: `SA_PASSWORD=… tests/sql/service-accounts/sa-sql-harness.sh <container> SecureOps_SaDemo`,
   then `sa-demo-bootstrap.sql -v Identity="demo:platform-admin"` after one API call as that actor.
6. `node tests/browser/service-accounts.cjs <playwright> https://localhost:5100/ https://localhost:5101/
   http://localhost:5000/ <evidence dir>` — all names are generated (`SYN_*`, suffix per run).

What this does **not** prove: the production access store, role bundles and approval flow, API
session cookies against a persisted session store, Integrated Security SQL, IIS hosting. Target run:
normal Demo/Test API with `Access:RepositoryProvider=SqlServer`, a reviewed role containing the
module actions, and the bootstrap fixture — not done.

### SQL test failure: VerifyAction deadlock (cause established 2026-09-29)

History: one early run of the module SQL tests had 3 failures without captured messages
(`SecureOps_SaTest2`), and Codex later saw 18/19 on a fresh LocalDB with
`MultipleRequests_ActionVerification_AndClosureRules` returning `ServiceAccountPersistenceUnavailable`
at VerifyAction. Passing reruns were never treated as a fix.

Evidence: the SQL Server `system_health` session of the disposable container retained one
`xml_deadlock_report` (synthetic database `SecureOps_SaTest2`, 2026-09-28 19:50:31 UTC). Victim:
VerifyAction's closure statement `UPDATE svcacct.Accounts SET LifecycleState = 'ClosureVerified' …`
(READ COMMITTED, holding U/X on the account key). Other side: the import commit (SERIALIZABLE)
`UPDATE a SET LastObservedOn … FROM svcacct.Accounts a JOIN svcacct.AccountObservations o …`,
holding RangeS-S from its in-transaction re-plan and requesting RangeS-U on the same key. Error
1205 was mapped to `ServiceAccountPersistenceUnavailable`. xUnit runs the import and workflow test
classes in parallel on one database, and on a small fresh table the commit's re-plan reads ranges
that cover other tests' synthetic accounts — so the failure is timing-dependent, and it is also a
production defect (any account write overlapping an import commit could deadlock).

Cause: the import commit takes the exclusive application lock `svcacct:import-commit`, but no other
module write took that lock, so they could hold row locks inside the commit's serializable range.

Fix: `BeginWriteAsync` takes the same lock in **Shared** mode as the first statement of every other
module write transaction on tables the commit reads or writes (account/work mutations, communications,
administration, import staging and re-plan). Consistent lock order means a write waits for a running
commit before locking any row, and a commit waits for running writes. No retry, sleep or weakened
assertion. A gate wait above 25 s throws 51312 (distinct from the install-time 51311) and is reported as persistence unavailable with nothing
written. Reminder outbox, report snapshots and audit-only reads touch disjoint tables and are unchanged.
`sp_getapplock` needs only `public`; the role scripts are unchanged.

Proof: `ClosureVerification_DuringImportCommit_WaitsInsteadOfDeadlocking` reproduces the recorded lock
order deterministically (synthetic data). Without the fix: 3/3 runs fail with error 1205. With the fix:
3/3 pass, and the full module SQL filter passes (21/21). The early 3-failure run and Codex's 18/19 run
kept no deadlock graph, so they are **consistent with** this cause but not proven identical. The test
fixture now also records the failure type of non-SQL exceptions that are reported as persistence
unavailable.

### Other single failures after a fresh build (still unexplained, not reproduced)

Two single failures occurred, each on the first module SQL run right after a build (2026-09-28):

1. `SentSnapshotNeverChanges_LiveReportPlacesLateActionInItsWeek_ExportsReconcile` — message lost
   (my console filter dropped it; no TRX was written). No retained evidence beyond that fact.
2. `ParticipantTeam_WorksOnlyOnItsOwnRequest_AndLosesAccessWhenItCloses` — retained TRX
   (`participant-c2b.trx`, 23:39:06 UTC): a participant's `CreateRequest` returned
   `ServiceAccountPersistenceUnavailable` instead of `AccessDenied` on a path that only reads before
   denying. The exception type and operation were not captured (the origin logging came later).

What is known: `system_health` recorded no deadlock at either time, so neither is the 1205 cause
above. The service reports SQL errors and `DbException`/`IOException`/`InvalidOperationException`/
`TimeoutException` as "persistence unavailable"; the module log now names SQL number/state/class or
failure type plus a safe `Origin` (type and method only), and the SQL fixture writes every such entry
to `SECUREOPS_SA_SQL_DIAGNOSTICS`.

Reproduction attempts (2026-09-29), all with TRX and diagnostics retained under the session evidence
folder: 5 cycles of *new database* (harness + both role scripts) → `dotnet build --no-incremental` →
first module SQL run: 28/28 each; earlier 8 warm runs and 10 rebuild-then-run cycles: 24/24 each. The
only diagnostic ever captured is the intentional 51091 of the audit-rollback test. **Not reproduced;
cause unknown; no fix is claimed for these two failures.**

Demonstrated defect found while investigating (fixed, not claimed as the cause): SqlClient pools by
connection string and a pooled session keeps the isolation level of its last transaction. The
module's SERIALIZABLE transactions (import commit/re-plan, administration, communications) used the
platform's `ConnectionStrings:SecureOpsDb` pool, so the next platform or module user of that session
ran autocommit reads at SERIALIZABLE (and READPAST queries would be rejected). Fix: the module derives
its own pool (`Application Name` + " / Service Accounts") and sets READ COMMITTED on every open.
`ModuleSerializableWork_DoesNotLeakIsolationIntoPlatformPooledConnections`: without the fix 3/3 fail
(next platform session isolation 4 = SERIALIZABLE), with the fix 3/3 pass. DBAs will see the module's
sessions under that program name.

## Acceptance mapping (SPEC scenarios)

| # | Requirement | Evidence | Result |
|---|---|---|---|
| 1 | 390 accounts after first migration; earlier accounts preserved | Private reconciliation: package → 390 accounts; `LegacyPackageThenWorkbook_…` | Passed (357/1 split needs the unavailable previous baseline — not claimed) |
| 2 | Re-import does not increase counts; older observation never moves latest back | Reconciliation: workbook 933 same / 0 new, package replay detected, counts unchanged; `CoordinationList_NewPeriodObservations_…`, `LegacyPackageThenWorkbook_…`, `Absence_IsInferredOnlyFromAValidatedCompleteList` (absence only from a declared complete list) | Passed |
| 3 | Handover flag OK → exactly 81; no acceptance/gMSA without evidence | Reconciliation: cohort 81, reported 81, accepted 0, gMSA completed 0; `FindingsAndHandover_AreNotCompletedWork_…` | Passed |
| 4 | Linux cohort: 8 dated password plans (30 Sep–30 Dec 2026), 2 closure reviews; completed totals unchanged | Synthetic: `LinuxCohort_EightPlansAndTwoClosureReviews_…` (open state) and `LinuxCohort_TwoClosureReviews_ReachSeparateOutcomes_WithoutClosingAccounts` (evidence review verified, both review requests completed, ownership confirmed only by its own decision, deletion stays planned, 0 verified closures, 0 performed password/deletion); `OnlyDeletionOrGmsaConversion_CanBeAVerifiedAccountClosure`. Private reconciliation: 8 open password plans, verified closures 0 | Synthetic passed; the two real reviews in corporate data are not identified here (no names) |
| 5 | Ownership stays proposed; mail senders never become owners | Reconciliation: 321 proposals, 0 confirmed; `ConfirmingOwnershipInImport_RequiresAssignCapability`; harness journey ownership step | Passed |
| 6 | Turkish case variants of one verified person map to one; same name + different UPN stay two | `LabelKey_TurkishCase…`, `LabelKey_AccentFolded…`, `Identity_DomainsAreDistinct_AndSameNamedVerifiedPeopleAreAmbiguous` | Passed |
| 7 | Same name in two domains → two AccountIds; no automatic merge | `IdentityKey_SameNameInTwoDomains…`, `Identity_DomainsAreDistinct_…`; reconciliation doubled-letter variant needed an explicit decision | Passed |
| 8 | Blank source value never erases; ownership conflicts are audited decisions | `ConcurrentUpdates_OneWins_…` (blank does not erase), import decision flow tests | Passed |
| 9 | Several open requests visible; closing one leaves others | `MultipleRequests_ActionVerification_AndClosureRules`; harness journey | Passed |
| 10 | Week boundaries, timed Sunday, cutoff; undated counted separately; no 1900-01-01 | `WeekBoundaries_UseIstanbulMondayToMondayAndCutoff`, `LateHistoricalAction_…`, `InvalidBusinessDate_IsARowError_…`; reconciliation placements reconcile | Passed |
| 11 | Performed→Verified same identity; deletion without OR not a verified closure; verification before action rejected | `Verification_OnSameAction_…`, `DeletionClosure_WithoutOr_…`, `PerformedThenVerified_IsOneAction_…`; harness journey (undated verify refused, deletion closure verify refused) | Passed |
| 12 | One mail → N accounts counts once; provider ID dedupes; same-subject mails kept | `OneMailLinkedToTenAccounts_…`, `OneMailManyAccounts_CountsOnce_…`; harness journey (one mail, three accounts) | Passed |
| 13 | Findings / failed scans are not completed work; gaps visible | `Plans_AwaitingDates_Overdue_Handover_AndFindingsDoNotCountAsWork`, `FindingsAndHandover_…` | Passed |
| 14 | Other team refused on account/API/export/attachment; backend-scoped filters; visibility through an assigned request is not account-wide authority | `OtherTeam_CannotReadUpdateListOrDownload`, `ReportFiltersAreBackendScoped_…`, `ScopeIsEnforced_…`, `ParticipantTeam_WorksOnlyOnItsOwnRequest_…`, `AccountListExport_IsScopedFilteredAndAudited`, `PersistedRoleBundlesAndScopeGrants_…`; harness journey (temporary harness) | Passed |
| 15 | Concurrent rowversion: one wins, other 409; commit/job twice → one result | `ConcurrentUpdates_OneWins_…`, `StalePreviewAndDecisionConflicts_…`, `RepeatedRunsCreateEachReminderOnce_…`, `ConcurrentClaims_…`; reconciliation repeated commits | Passed |
| 16 | Invalid rows visible; failed transaction leaves nothing half-written; file/formula-injection tests | `AuditFailure_RollsBackTheWholeCommit`, parser rejection tests (macro, ratio, signature, missing header), `Xlsx_…NeutralizesFormulaText…` | Passed |
| 17 | Sent snapshot unchanged; late action in the correct live week | `SentSnapshotNeverChanges_LiveReportPlacesLateActionInItsWeek_ExportsReconcile` | Passed |
| 18 | XLSX opens in desktop Excel without repair; PDF and XLSX reconcile | Structure (no formulas/macros/links, date serials, deterministic), PDF xref and value reconciliation tests; harness journey downloads (`PK`, `%PDF`) | Structural checks passed; **desktop Excel open not run** (no Excel on Linux) |

Legacy ownership control: the labelled projection now reproduces 40 named accounts / 7 people +
13 follow-up fallback = 53 accounts / 9 people on the supplied package; confirmed ownership is 0
until someone confirms.

## Shared touchpoints (all additive; `git diff a3037175 --histogram` shows no deletions)

| File | Change |
|---|---|
| `src/SecureOps.Api/Program.cs` | `using` + `AddServiceAccountsApi(configuration)` |
| `src/SecureOps.Infrastructure/Access/AccessActionCatalog.cs` | spread of the 7 module actions (no role changed or seeded) |
| `src/SecureOps.Worker/Program.cs` | `using` + `AddServiceAccountsWorker(configuration)` |
| `src/SecureOps.Ui/Program.cs` | one `AddSecureOpsApiClient<ServiceAccountApiClient>` registration |
| `src/SecureOps.Ui/Shared/NavMenu.razor` | one entry gated on `ServiceAccounts.View` |
| `src/SecureOps.Ui/Services/UiProblemFactory.cs` | one delegation line to `ServiceAccountProblems` |
| `src/SecureOps.Ui/Pages/_Host.cshtml` | label binding limited to `.sa-page` fields (accessibility) |
| `src/SecureOps.Ui/README.md` | six route rows |
| `docs/contracts/secureops-api-v1.openapi.json` | regenerated snapshot, additive |

Not touched: In Use, SDM/OR, OCO/announcements (including mail send intents), SCCM, release
scripts, historical packages, security validators, the canonical integrated delivery register,
migrations 001–024.

## SQL candidate

`sql/pending/service-accounts/SA-001-service-accounts.sql` — unnumbered, outside release discovery.
Requires 001; refuses replay; one new schema `svcacct`; no existing object/role/grant/row changed;
no seed of real users. Runtime grants are listed at the end of the file (DBA applies to the
existing approved principal; DELETE only on uncommitted `ImportRows`). Rollback: disable the module
(`ServiceAccounts:Provider=Disabled` → every endpoint 503, no SQL touched); the schema and its data
are retained, there is no down script. **Codex must reserve a migration number** (025 is not
assumed) before promotion.

Configuration (all off by default): `ServiceAccounts:Provider` (`Disabled`/`SqlServer`),
`MaxImportBytes`, `MaxEvidenceBytes`, `Reminders:Enabled`, `Cron` (UTC), `PlanEndLeadDays`,
`NoReplyAfterDays`, `MaxAttempts`, `LeaseSeconds`, `HolidayCalendar` (empty keeps business-day rules off).

## Review size

About 18 900 hand-written lines plus the generated OpenAPI snapshot — far above the 1 000-line
review cap. **No exception has been granted** and the quality rule is unchanged. Proposed bounded
review slices (each below 1 000 added lines, each with its own tests; counts from
`git diff a3037175 --histogram --numstat`):

| # | Slice | Paths (under the module folders unless shared) | Lines |
|---|---|---|---|
| 1 | Specification and design | `docs/service-accounts/SPEC.md`, `README.md`, `PROGRESS.md` | ≈ 520 |
| 2 | SQL candidate and SQL fixtures | `sql/pending/service-accounts/SA-001…`, `tests/sql/service-accounts/*` | 667 |
| 3 | Domain identity, vocabulary, scope, calendar | `ServiceAccountVocabulary/Text/Scope`, `ReportCalendar` + `ServiceAccountDomainTests` | 723 |
| 4 | Domain rules, metrics, reminders | `ServiceAccountRules/Metrics/Report`, `ReminderRules` + metrics and reminder rule tests | 747 |
| 5 | Contracts, capabilities, persistence types | `Shared/Contracts/ServiceAccounts/*`, `ServiceAccountAccessActions`, `ServiceAccountPersistence`, `ServiceAccountOptions`, shared `AccessActionCatalog` line | 751 |
| 6 | Safe spreadsheet reading | `Import/SpreadsheetReader`, `ImportValues`, `ImportHeaders`, `StagedRow` + unit `SyntheticWorkbook` | 899 |
| 7 | Import parser | `Import/ImportParser` + `ServiceAccountImportParserTests` | 752 |
| 8 | Import planning | `Import/ImportPlan`, `ImportContext`, `ImportPlanner`, `.Resolve`, `.Rows` | 871 |
| 9 | Import records and import service | `Import/ImportPlanner.Records`, `ServiceAccountService.Import` | 697 |
| 10 | SQL repository core and import persistence | `SqlServiceAccountRepository`, `.Import`, `.ImportWrite` | 822 |
| 11 | Import API and SQL tests | `ServiceAccountImportsController`, `ServiceAccountsApiModule`, `ServiceAccountImportSqlTests`, fixture, synthetic legacy/workbook | 789 |
| 12 | Accounts and work persistence | `SqlServiceAccountRepository.Accounts/.Detail/.Work`, `ServiceAccountService`, `ServiceAccountsModule` | 844 |
| 13 | Account commands and records | `ServiceAccountService.Accounts/.Records`, `SqlServiceAccountRepository.Records` + `ServiceAccountWorkflowSqlTests` | 917 |
| 14 | Administration and account API | `.Admin` repository/service, admin and accounts controllers, `ServiceAccountAdminSqlTests`, shared API `Program.cs` lines | 755 |
| 15 | Reports and exports | `Reporting/*`, `.Reports` repository/service, reports controller + export and report SQL tests | 963 |
| 16 | Reminders and Worker schedule | `.Reminders` repository/service/controller, `ServiceAccountReminderJob`, Worker module, shared Worker `Program.cs` lines + reminder SQL tests | 536 |
| 17 | UI foundation and shared UI touchpoints | UI client/page base/problems/text/commands, `SaProblem`, `SaModuleLinks`, `SaPersonPicker`, shared UI `Program.cs`, `NavMenu`, `UiProblemFactory`, `_Host.cshtml`, UI README rows + `ServiceAccountUiTests` | 727 |
| 18 | UI list and administration | `ServiceAccountList`, `ServiceAccountAdmin`, create/communication/people components | 866 |
| 19 | UI account detail: work and ownership | `ServiceAccountDetail`, edit form, requests, actions, ownership panels | 805 |
| 20 | UI records, reminders page, reports page | findings, handover, evidence, sources panels, `ServiceAccountWork`, `ServiceAccountReports`, `SaReportView` | 814 |
| 21 | UI import wizard | `ServiceAccountImports`, import rows/mapping/result components | 662 |
| 22 | Browser journey (harness evidence) | `tests/browser/service-accounts.cjs` | 340 |
| 23 | Generated OpenAPI snapshot | `docs/contracts/secureops-api-v1.openapi.json` — review by regeneration and semantic diff, not line by line | generated |

Slices 2→16 follow the dependency order (SQL, domain, contracts, infrastructure, API); UI slices
17→21 depend on 5; slice 22 depends on all. Whether this slicing, a split into several branches or
another approach is acceptable is the reviewer's decision.

## Outstanding inputs (actionable, not invented)

- Migration number and final platform integration (Codex); corporate role bundles and first scope grants.
- Role bundles containing the module actions and the first scope grants (corporate decision; the
  demo fixture is for local testing only).
- Verified directory identities for people/accounts; approved evidence retention.
- Approved holiday calendar and reminder periods; any future mail sender and recipient scope.
- Team return (PAAS) file sample — generic mapping until then.
- Windows host run: LocalDB/SQL with Integrated Security, IIS, desktop Excel open, Windows
  toolchain build.

## Proposed register entry (for Codex to place in the canonical register)

> Service Accounts module (continuation branch `feature/service-accounts-continuation-20260929`,
> checkpoint `c63d1bc` on `e997c5b`): VerifyAction deadlock cause fixed (shared write gate),
> explicit import coverage, participant write boundary, provisional manual identity, entry work
> summary, monthly/custom periods, scoped export, on-demand directory observation. Linux: build 0/0,
> unit 1563/1565 (2 baseline env failures), integration failing set identical to baseline, module SQL
> 28/28 on a fresh DB under the least-privilege API role, persisted-access composition test passing.
> Pending: migration number, corporate role bundles/scope grants, Windows/LocalDB/IIS/HTTP/Excel
> checks, browser journey on the real composition, review of the bounded slices (no size exception).
