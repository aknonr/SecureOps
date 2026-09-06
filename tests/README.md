# tests/

Two test projects mirroring `src/`.

Current coverage includes Phase 1A IdentityLookup, audit hardening, Operational Record/Jira classification and workflow behavior, authorization, SQL contracts, and release packaging. Phase 1 diagnostic, Hangfire, and JEA tests are still planned.

Access lifecycle coverage proves one Pending request per unknown principal, protected-capability denial, configured bootstrap Admin authorization and audit, Admin approval, capability assignment, immediate disable denial, non-admin denial, and idempotent canonical role replacement.

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
