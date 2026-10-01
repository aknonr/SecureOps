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

## Operational delivery continuation (2026-09-21)

The owner now authorizes implementing native Windows Service hosting, replacing
the temporary foreground-only operating model. This does not authorize target
installation or broaden remote effects. Use the existing WindowsServices package
and the same Hangfire server/SQL leases, not a wrapper or a second scheduler.
Console and read-only diagnostics remain available. All modes load configuration
from the executable directory, with the existing environment/CLI precedence.
Service mode requires an explicit environment and a pre-provisioned absolute local
WorkerHosting:DataDirectory for bounded lifecycle logs and a process lock. Console
mode uses that same lock when configured; diagnostics never acquires it or writes
logs. This local lock is not a remote idempotency/concurrency guarantee and cannot
fence an old rc6.26 console process. Stop the old console before service handover.
No uncertain mail/upload is retried by changing lifetime or SCM recovery policy.
Actual SCM/logoff/restart and corporate job acceptance remain separately recorded
in the single integrated-test-activation.md register. Operator procedures are in
worker-service-operations-tr.md, linked from the current Turkish entry.

For selected read-only SCCM diagnosis, an explicit absolute content-root argument
may reuse the installed configuration from a separate diagnostic payload. Default
root remains executable-relative. The in-process PowerShell host now uses its
matching SDK rather than the engine alone: an isolated test demonstrated missing
built-in Management commands. Runtime policy and remote permissions are unchanged;
this local dependency defect is not presumed to explain the target's generic
ActionPreferenceStopException. Staged safe ErrorRecord metadata is required first.

- `docs/03-architecture.md`
- `docs/agent-guides/030-worker-service.md`
