# tests/

Build policy (G-30, moved to .NET 10 on 2026-10-02): exact SDK 10.0.401 (`global.json`, no roll-forward),
C# 14.0, analyzer level 10.0, `net10.0`. Build/test without a LangVersion command-line override. DPAPI key persistence uses WindowsFact/WindowsTheory;
Windows runs must execute these cases, not skip them. Portable ephemeral and
configuration rejection cases run everywhere. No other platform failures are
waived. See docs/26-ui-backend-contract-gaps.md, G-30.

Test stack (2026-10-03): xunit v3 via `xunit.v3.mtp-off` + `xunit.runner.visualstudio` 4 (VSTest; test
projects are `Exe`), FluentAssertions 7.2, NSubstitute 6, Test SDK 18, coverlet 10. `xUnit1051`
(pass `TestContext.Current.CancellationToken`) is enforced in both test projects as of 2026-10-05.
Ordinary calls with omitted optional tokens now use the current test token; dedicated cancellation
inputs remain explicit. The build has no replacement suppression for this rule.

Numbered-release SQL selection has a focused Windows runner at
`release/Test-PairedReleaseSqlSelection.ps1`. A new private evidence directory
is mandatory; optional `-VerifySqlCmd -DatabaseSuffix <unique suffix>` exercises
the exported include tree, missing024 rejection and transaction rollback ONLY
on the hard-coded disposable LocalDB instance. No packaging or corporate action.

Two test projects mirroring `src/`.

Post-E-08 archive follow-up: six synthetic corruption cases in `InUseTests` verify
the exact `InUseArchiveIntegrityFailed` category for byte/hash, size, identity,
preparer, retention and malformed JSON errors, no content/callback and no rewrite.
Focused In Use and in-process API runs are recorded separately in the canonical
register. Supplied corporate archives/workbooks remain private review evidence,
not public fixtures. No full-suite, browser or SCM rerun is claimed for this fix.

Manual In Use verification (IU-06/E-07) adds component rendering and real-client
fake HTTP coverage, plus isolated SQL acknowledgement/timeout/rejection, concurrent
manual confirmation, audit rollback/revocation and no verified report count tests.
The 10 current SQL regression cases run against the matched E-07 build; normal
integration still skips opt-ins without explicit LocalDB configuration. Exact TRX
paths and counts are in integrated-test-activation.md. No browser or corporate
closure is implied. The sealed E-06 and E-05 diagnostic remain unchanged.

## Post-rc6.26 Remaining Gates

AnnouncementSourceProposalTests covers literal/whitespace description fallback,
separate manual restart time and dynamic service proposals without SQL/providers.
E-06 matched review artifacts and new test identities are in the canonical register.

WorkerHostingTests exercises explicit binary content root/Test-only JSON, CLI
precedence, diagnostics composition without writes/host start, service config
guards, local exclusive-lock release, durable structured lifecycle logs and the
same job-server start/stop delegation. It does not install an SCM service or test
logoff/crash recovery. Native acceptance uses worker-service-operations-tr.md and
the single OPS-02 register entry. No existing UI host restriction is bypassed.

Current catalogue SQL, reporter crosswalk, route authorization, immutable metadata
and release delta contracts are covered by the continuation tests. The isolated
resource harness includes 024 only in fresh task-owned databases; it never upgrades
corporate installations. Source SQL tests additionally require the published
Hangfire schema 9 installed explicitly into that fresh test database, never runtime
schema preparation. Existing API draft tests require fresh fixtures on every run.
See `docs/post-rc626-continuation-tr.md` for exact opt-in accounting and the current
operator browser procedure. `browser/inuse-rc626-repair.cjs` now tests catalogue,
focus return, themes and `WASAS_NATIVE_ZOOM=1` separately from mobile reflow. Its
current assertions are not claimed passed until that permitted runner completes.
Do not retry a rejected UI-host launch using another command/port/executor.
The same permitted journey now includes the system-status card before/after each
explicit check, both themes, mobile touch, keyboard focus, native zoom and unchanged
repeat JSON download/timestamps. Page refresh must leave the first-check state
untouched. These browser assertions remain unexecuted. Local synthetic
`OperationsReadinessPresentationTests` covers disabled/unchecked/partial mapping,
single-flight requests, stale failure retention, denial clearing and download.
The current script requires a fresh evidence directory, records Chrome's actual
zoom setting plus a 100% baseline, and covers explicit save/unassignment as well
as cancellation. Escape retains the dialog by its existing explicit-cancel policy;
keyboard cancellation uses the Cancel button. The launcher binds the API to HTTP
loopback and UI to HTTPS; use those exact schemes in the operator procedure.

Announcement commands, isolated SQL setup and actual results are recorded in
`docs/contracts/planned-announcements-v1.md`, including the isolated published
`browser/announcements.cjs` UI journey. No corporate/VDI/SMTP acceptance is implied.

Current coverage includes Phase 1A IdentityLookup, audit hardening, Operational Record/Jira classification and workflow behavior, authorization, SQL contracts, and release packaging. Phase 1 diagnostic, Hangfire, and JEA tests are still planned.

Access lifecycle coverage proves one Pending request per unknown principal, protected-capability denial, configured bootstrap Admin authorization and audit, Admin approval, capability assignment, immediate disable denial, non-admin denial, and idempotent canonical role replacement.

## In Use V1

The optional-review follow-up tests unassigned/other-assigned review capability,
actual actor attribution, concurrent saves and non-overlapping scope refresh.
SQL tests include independent repository instances and PUBLIC-only application
lock access. The existing mapped browser mode now reviews without an assignee;
after mode also confirms explicit conflict comparison preserves local edits.
RFC collector tests use synthetic direct selectors only: id/code lookup without
active scope, deduplication, differing/missing/duplicate references, zero/multiple
matches, identity mismatch and denied/malformed results. This is not corporate
mapping, production ownership, template acceptance or source closure evidence.

`InUseServiceItemParserTests` uses synthetic 27-cell rows with reordered/missing/
duplicated keys, conflicting references, per-server differences and strict limits.
`ResourceSqlTests.InUse_SemanticProjection_PersistsForPublishedReview` retains a
four-server `OR-MAPPED-` fixture in a fresh approved isolated DB. Browser `mapped`
mode consumes that single fixture through published local Demo API/UI, preserving
the three-answer workflow and checking XLSX archival. Run it before/alongside the
existing `after` regression journey. No corporate evidence file is a test fixture.

`SecureOps.Tests.Unit/InUse`, `SecureOps.Tests.Integration/Api/InUseHostedTests.cs`
and `SecureOps.Tests.Integration/Sql/ResourceSqlTests.InUse.cs` cover independent
4241/68 discovery, strict semantic response parsing, retained data on failed/partial
refresh, explicit ownership, concurrent assignment/save, draft versions, direct API
capabilities, audit rollback, text-only XLSX mappings and provenance. Existing SDM
category exclusions/write-fence and Resources ownership suites remain required.
`scripts/powershell/Test-ResourceCatalogueSql.ps1 -DatabaseSuffix <isolated-suffix> -RunTests`
now applies 001-012 to the approved isolated LocalDB instance. Never use a corporate
connection or an existing application database for this harness.

`browser/in-use-workspace.cjs` accepts installed playwright-core, published loopback
UI/API URLs, evidence directory and before/after/denied/unavailable mode. Before
uses preserved rc6.13 copies; after uses Demo/Simulation with synthetic isolated SQL.
Denied uses a separate local UI process with Demo actor team-lead. In unavailable
mode, stop only the local test API after READY, then replace the evidence directory's
`unavailable-continue.signal` content with `continue`. This explicit barrier precedes
searching persisted records. Browser requests are restricted
to loopback. No source script runs,
corporate data, external writes or release packaging belong to these tests.

## SecureOps.Tests.Unit

Fast, isolated, no I/O. Mock everything external.

Subfolders mirror `src/` namespaces:
- `Domain/` — entity behavior, value object equality.
- `Application/` — application service tests with mocked infrastructure.
- `Shared/` — DTO, contract, utility tests.
- `Diagnostic/` — per-module tests using `MockPowerShellRunner`.
- `Audit/` — audit writer behavior.
- `Mocks/` — shared mock implementations.

Target coverage:
- Domain: 90%+
- Application: 80%+

## SecureOps.Tests.Integration

Slower, broader. Default coverage uses the in-process Web API host via `WebApplicationFactory` and deterministic backend fakes. Resource SQL tests additionally support an explicitly selected isolated LocalDB database. No SQL container or UI test dependency is active.

Subfolders:
- `Api/` — webhook flow, controller routing, authorization.
- `Worker/` — Hangfire job orchestration.
- `Sql/` — opt-in isolated LocalDB execution; offline SQL asset assertions remain separate unit tests.
- `Security/` — JEA whitelist enforcement, forbidden-cmdlet rejection.
- `Performance/` — concurrent webhook load, diagnostic timing.
- `Mocks/` — mock SolarWinds, PAM, Teams.

Target coverage:
- Infrastructure: 60%+

## Running

```bash
# Unit only (fast)
dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj

# All
dotnet test SecureOps.sln

# With coverage
dotnet test SecureOps.sln --collect:"XPlat Code Coverage"
```

## Rules

See `docs/agent-guides/090-testing-quality.md`:
- No `Thread.Sleep` waits.
- No real external services in unit tests.
- No hard-coded user paths.
- No asserting on log message text.
- Each test isolated (no shared mutable state).
## Current Test Boundary

Current OR-to-SDM replay uses `browser/sdm-jira-only.cjs` with its existing
Playwright/UI/API/fresh-database/evidence arguments. Default mode performs the
three-declaration, actual SQL conflict, denied-role, explicit refresh/stored paging,
ServerRequest confirmation and uncertain-result journey. Run the guarded 001-013
harness and SQL opt-ins first. Use a fresh task database; never reset old transfers.
`--verify-presentation` runs after stopping/restarting only the task API/UI against
that same database; it reads stored records, verifies source-open linkage and rejects
unknown-result recreation. `--prepare-walkthrough` uses a different fresh task DB,
verifies SIM-OR-100 preview/focus/cancel and leaves create for the owner. The numeric
exact corporate-policy branch is separately tested by
`ExactPilot_RealDraftSqlAuditAndRestart_PreserveOneJiraOnlyResult` with real draft/SQL
services and only test substitutes for external interfaces. It never constructs
corporate HTTP clients. `SdmRecoveryTests` covers actual detail handlers without
adding a UI package. Canonical current results/launcher are in the SDM UI handoff.

`browser/resource-opening.cjs <playwright-core> <UI> <API> <evidence> <before|after>`
uses only published loopback Demo hosts and synthetic Resources fixtures. Both
fresh Chrome profiles explicitly remove `--disable-popup-blocking`; only the allow
profile grants this loopback site popups. It asserts actual target page counts,
URLs and opener isolation, not mocked call counts. After mode covers non-admin
personal groups, stale-version recovery, owner isolation, partial resolution,
keyboard tour nonmutation and responsive themes. Profiles/screenshots stay outside
Git. This is not proof of corporate GPO, VDI or destination authentication.

Dashboard/SDM browser journeys reuse the foreground Demo hosts, paired Simulation
providers and the existing isolated LocalDB migration harness. They do not install
providers or alter production eligibility. `tests/browser/management-journey.cjs`
accepts a task-local Playwright path, UI/API loopback URLs, a fresh
`SecureOps_ResourcesV1_` database and an evidence directory. Its SQL fixtures are
synthetic, append-only and refused on repeat insertion. A test-owned transaction
locks workflow history to exercise report loading, timeout and recovery; it is
rolled back in `finally`. No proxy or runtime failure-injection route is used.

Replay order: create a fresh database with `Test-ResourceCatalogueSql.ps1`, run
the opt-in SQL tests (which also persist the evaluated blocked fixture), then
start foreground API/UI hosts on unused loopback ports. The API uses Demo auth,
Demo access compatibility, Mock identity, paired Simulation source/Jira, and SQL
for Access, Audit, OperationalRecords and SessionSecurity. Supply the guarded
integrated-auth LocalDB connection through local process configuration. The two
UI hosts use the same API and the existing `platform-admin` / `team-lead` Demo
actors. No runtime configuration is written to the repository.

Run `management-journey.cjs`, `sdm-journey.cjs`, then `reporting-access.cjs` with
the arguments in their headers. The latter two temporarily assign synthetic
application roles through the supported local API and restore the original roles.
They verify the API user belongs to the explicitly guarded SQL database first.
Use a fresh database and fresh Simulation API process for a complete replay:
completed/uncertain transfers are never reset. `WASAS_REUSE_FIXTURES=1` reuses
management window fixtures; `WASAS_CAPTURE_RESULTS=1` captures persisted SDM
results without another publication. Browser captures await responsive drawer
closure and inspect desktop/mobile layouts at 1440x900 and 390x844.

Resource catalogue coverage adds capability/ownership, URL/content policy,
visibility/archive, search/order/page, stale writes, private defaults and API
serialization. ResourceSqlTests are explicitly opt-in via
SECUREOPS_SQL_TEST_CONNECTION, accept only the isolated Resource V1 LocalDB
instance and test-prefixed database, and report skipped/NOT RUN when absent.
The local harness in scripts/powershell/Test-ResourceCatalogueSql.ps1 validates
the actual migration upgrade and then runs SQL round-trip, concurrency,
transactional audit rollback, append-only, and SDM persistence tests. Corporate
SQL/AD/HTTP endpoints remain forbidden. Offline SQL asset assertions are separate.

In Use recovery acceptance uses `tests/browser/in-use-recovery.cjs` with arguments
`<playwright-core> <ui-loopback> <api-loopback> <fresh-evidence-directory> <proxy-port>`.
Use the same fresh LocalDB harness and foreground Demo/paired Simulation composition
above, with controlled writes and source close disabled. `ReadOnlyIntegrationMode`
is false only in this local Simulation process; its true mode requires the distinct
Test/real-adapter profile. Never alter shipped validators/configuration to combine them.
Point the UI API base address to the unused loopback proxy port. The script owns
that proxy and injects 503/transport/held responses only for In Use requests; it
adds no runtime endpoints and records no authentication headers. It changes only
synthetic fixtures in the task database. Existing fixtures/archives are retained.
The final session-expiry browser check uses actual application-session revocation;
`ApplicationSessionServiceTests` separately verifies idle/absolute clocks.

For reporter acceptance, stop only that task UI and restart it directly against
the same API. Opt into `InUse_ReporterAcceptance_NewFixtureOnly_PreservesExistingData`
with a new synthetic `SECUREOPS_INUSE_ACCEPTANCE_SOURCE_ID` in its guarded range;
run `in-use-reporter.cjs` with the existing header arguments and that OR code.
It now completes answer conflict acceptance, persisted reopening and four-server
workbook download/archive. Fake-HTTP mapping/SQL tests and local browser acceptance
do not prove corporate integration. See the existing In Use canonical handoff in
`src/SecureOps.Ui/README.md` for the latest results and remaining external gates.

All identity, Swagger, authorization, forwarded-header, and SQL schema tests are local. They use mocks/fakes, offline assertions, or the explicitly guarded disposable LocalDB facility. They must not contact corporate AD, PAM, LDAP, SQL Server, IIS, or load balancers. Real provider validation is a separately authorized test-server activity.

Release validation additionally checks the published Active Directory dependency closure, manifest hashes, and ZIP paths without contacting a domain controller.

`SccmFailureEvidenceTests` uses a temporary FileSystem drive in a real Restricted
in-process PowerShell runspace. It verifies built-in Management/Utility availability,
private-drive continuity across staged commands and sanitized ErrorRecord metadata;
it never imports ConfigurationManager or contacts SCCM. Worker hosting tests also
cover explicit absolute content-root diagnostics and rejected disabled/incomplete
profiles before dispatch. Published runtime/profile checks and their exact TRX
identities are recorded under E-05 in the single integrated activation register.

Operational Record/Jira workflow tests use deterministic fakes. They cover manual-review fallback, exact requester resolution, preview, audit, repeated/concurrent create requests, unknown Jira outcomes, persisted Jira plus source-close failure, retry, cancellation, ProblemDetails, authorization, and offline SQL uniqueness. The opt-in Resource SQL suite also validates SDM persistence in isolated LocalDB. No tests contact live Jira, the Operational Record source, corporate SQL Server, AD, IIS, or PowerShell remoting.
## E-08 In Use activity checks

Focused tests cover unchanged answers across workflow progression, real server
changes remaining stale, four-server corporate workbook dimensions/answers, trusted
and unresolved attribution, and activity/OR/manual evidence separation. Isolated
SQL tests additionally cover exact count/page tracking, verified-activity reporting,
transactional confirmation, unknown/no-retry and retained duplicate fencing.
Browser runner selectors follow the new activity/save wording; they are prepared,
not executed under the recorded host restriction. No synthetic rendering is
reported as browser acceptance. Evidence is linked from the canonical delivery
register, not a second checklist here.

In Use list tests compare memory/SQL creation-date ordering before pagination,
unknown dates last, equal-date/code stable ties, and minimal activity reconciliation
using labelled synthetic source observations. Missing rows preserve answers and
block current eligibility, never imply completion. Active or manually confirmed
executions remain verification-pending; a missing parent blocks the next mutation.
UI component logic verifies sort URL/load/refresh persistence and status labels;
these checks are not browser acceptance. See the current canonical register for
the final source identity, exact TRX files and unrepeated historical evidence.
