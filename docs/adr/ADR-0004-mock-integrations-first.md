# ADR-0004 — Mock Integrations First

**Status:** Accepted
**Date:** 2026-05
**Decision makers:** Project owner

## Context

The system integrates with multiple external systems:
- Monitoring platform (SolarWinds-style)
- PAM (BeyondTrust-style)
- Active Directory
- Microsoft Teams
- SMTP relay
- Ticketing system
- Virtualization platform (Phase 5+)

Each integration depends on another team for setup (service accounts, network rules, webhook configuration). Each delay blocks the developer.

A single developer cannot afford to be blocked on multiple parallel external dependencies.

## Decision

**Every external integration starts as a mock implementation behind an interface.** The real adapter is built later, when the external system is ready and the design has been proven against mocks.

Pattern:

```csharp
public interface IMonitoringPlatformClient { ... }
public sealed class MockMonitoringPlatformClient : IMonitoringPlatformClient { ... }
public sealed class SolarWindsClient : IMonitoringPlatformClient { ... }

services.AddSingleton<IMonitoringPlatformClient>(sp =>
    options.UseMock ? new MockMonitoringPlatformClient() : new SolarWindsClient(...));
```

Configuration toggle `UseMock: true` makes the mock active. The Worker logs a clear `"MOCK IN USE"` warning at startup for any mocked integration.

## Alternatives Considered

### Build against real systems from day 1

Rejected:
- Single developer is blocked on every external dependency.
- Test environments often don't have access to real systems.
- Real systems can change behavior between dev and prod.

### No integration abstraction, hardcoded calls

Rejected:
- Untestable.
- Tightly coupled.
- Changing integration provider requires touching everything.

### Use a shared "dev/prod" flag without separate classes

Rejected:
- Mixes concerns inside the adapter.
- Mock logic and real logic interleave; either gets ugly.

## Consequences

### Positive

- Developer is never blocked on external system access.
- Unit and integration tests run without external dependencies.
- Local development is fast and reliable.
- Real adapter can be built in parallel with other features.
- Integration tests reproduce production behavior using mocks with canned data.

### Negative

- Two implementations to maintain per integration.
- Real adapter may surface bugs the mock didn't predict.
- Risk of "works in dev, fails in prod" if mock drifts from reality.

### Neutral

- Mocks become a useful testing tool long-term.

## Implementation Notes

- Mocks live in `SecureOps.Infrastructure.Integrations.Mocks`.
- Real adapters live in `SecureOps.Infrastructure.Integrations.<Provider>`.
- Configuration in `appsettings.json` per integration: `"UseMock": true/false`.
- A startup banner lists which integrations are mocked.
- A health endpoint reports the same.

### Mitigations Against Mock Drift

- Contract tests against `contracts/schemas/*.schema.json` apply to both mock and real.
- Smoke tests against real systems in a separate test project, run when access is available.
- Real adapters are kept simple — they translate, they don't add logic.

## References

- `docs/06-integrations.md`
- `.cursor/rules/030-worker-service-rules.mdc`
