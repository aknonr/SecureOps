# ADR-0003 — Worker Service vs Standalone Script

**Status:** Accepted
**Date:** 2026-05
**Decision makers:** Project owner

## Context

Diagnostic jobs triggered by alarms must execute reliably and persist results. Options:

1. PowerShell scripts run by Task Scheduler.
2. A .NET console app run on demand.
3. A long-running Windows Service hosting Hangfire (chosen).
4. A serverless function (no on-prem equivalent in scope).
5. An ASP.NET Core API doing the work inline (rejected — long jobs in HTTP requests).

## Decision

A **.NET Worker Service registered as a Windows Service**, hosting **Hangfire** with SQL Server storage.

The Worker:
- Picks up jobs from the Hangfire queue.
- Executes diagnostic modules.
- Persists results.
- Writes audit entries.
- Dispatches notifications (Phase 3+).

The API does **not** execute diagnostic work inline. It enqueues a Hangfire job and returns immediately.

## Alternatives Considered

### Task Scheduler + PowerShell Script

Rejected:
- Hard to coordinate concurrency.
- No durable queue.
- No retry semantics.
- Hard to observe.
- Hard to test in CI.

### .NET Console App invoked on demand

Rejected:
- Coordinating multiple alarms requires custom queuing.
- Process startup overhead per alarm.
- No durable state across runs.

### API does work inline

Rejected:
- Long-running HTTP requests are fragile (timeouts, client disconnects).
- Scaling the API for work is wasteful — separate concerns.
- Difficult to retry safely.

### Azure Functions / serverless

Rejected:
- Out of scope: on-premise environment.
- Not available with current organizational stack.

## Consequences

### Positive

- Durable queue survives Worker restarts.
- Hangfire dashboard provides operational visibility.
- Retry semantics out of the box.
- Decoupled from HTTP — long jobs do not block API.
- Testable: jobs are normal C# classes.

### Negative

- Two processes to deploy and monitor (API + Worker).
- Hangfire SQL Server polling adds some DB load.
- Slightly more complex local development (two processes).

### Neutral

- Worker scaling is independent of API scaling.

## Implementation Notes

- Worker registered as a Windows Service via `sc create` or the .NET hosting integration.
- Hangfire queues: `diagnostic`, `notification`, `default`.
- Worker count: `Environment.ProcessorCount * 2`.
- Worker service account: `CONTOSO\svc-secureops`.

## References

- `docs/03-architecture.md`
- `docs/agent-guides/030-worker-service.md`
