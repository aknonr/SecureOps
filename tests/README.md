# tests/

Two test projects mirroring `src/`.

Current coverage is focused on Phase 1A IdentityLookup and audit hardening. Phase 1 diagnostic, SQL schema, Hangfire, and JEA tests are still planned.

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

Slower, broader. Real SQL via Testcontainers, real Web API host via `WebApplicationFactory`.

Subfolders:
- `Api/` — webhook flow, controller routing, authorization.
- `Worker/` — Hangfire job orchestration.
- `Sql/` — schema, audit trigger, append-only enforcement.
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

See `.cursor/rules/090-testing-and-quality-rules.mdc`:
- No `Thread.Sleep` waits.
- No real external services in unit tests.
- No hard-coded user paths.
- No asserting on log message text.
- Each test isolated (no shared mutable state).
