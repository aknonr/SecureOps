# tests/

Two test projects mirroring `src/`.

Current coverage includes Phase 1A IdentityLookup, audit hardening, Operational Record/Jira classification and workflow behavior, authorization, SQL contracts, and release packaging. Phase 1 diagnostic, Hangfire, and JEA tests are still planned.

Access lifecycle coverage proves one Pending request per unknown principal, protected-capability denial, configured bootstrap Admin authorization and audit, Admin approval, capability assignment, immediate disable denial, non-admin denial, and idempotent canonical role replacement.

## In Use V1

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

All identity, Swagger, authorization, forwarded-header, and SQL schema tests are local. They use mocks/fakes, offline assertions, or the explicitly guarded disposable LocalDB facility. They must not contact corporate AD, PAM, LDAP, SQL Server, IIS, or load balancers. Real provider validation is a separately authorized test-server activity.

Release validation additionally checks the published Active Directory dependency closure, manifest hashes, and ZIP paths without contacting a domain controller.

Operational Record/Jira workflow tests use deterministic fakes. They cover manual-review fallback, exact requester resolution, preview, audit, repeated/concurrent create requests, unknown Jira outcomes, persisted Jira plus source-close failure, retry, cancellation, ProblemDetails, authorization, and offline SQL uniqueness. The opt-in Resource SQL suite also validates SDM persistence in isolated LocalDB. No tests contact live Jira, the Operational Record source, corporate SQL Server, AD, IIS, or PowerShell remoting.
