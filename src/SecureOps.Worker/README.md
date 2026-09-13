# SecureOps.Worker

.NET Worker Service hosting the Hangfire job server. Registered as a Windows Service.

The default-off announcement source composition uses one configured Hangfire/SQL queue,
bounded execution leases and startup/minutely dispatch recovery. API and Worker must share
the same dedicated source database, preinstalled Hangfire schema and queue. Runtime schema
preparation is rejected; no email is sent. Fixture-backed process interruption/restart is
locally exercised; corporate adapter/service-installation acceptance remains separate.
See `docs/contracts/planned-announcement-source-acceptance.md` for configuration and evidence.

## Responsibilities

- Host Hangfire job server.
- Execute diagnostic modules (via `IPowerShellRunner` JEA).
- Persist diagnostic results.
- Write audit entries.
- Dispatch notifications (Phase 3+).
- Run scheduled jobs (rule evaluation Phase 6+, embedding refresh Phase 7+, retention rotation Phase 4+).

## Does NOT

- Serve HTTP traffic.
- Render UI.
- Call directly into the API or UI processes.

## Communication

Worker and API communicate only via:
- Shared SQL Server database.
- Hangfire queue.

No direct in-memory calls, no message broker, no HTTP between them.

## Dependencies

- `SecureOps.Domain`
- `SecureOps.Shared`
- `SecureOps.Infrastructure`

## Service Installation

Phase 1 Sprint 6 produces the install script. Outline:

```powershell
sc.exe create SecureOpsWorker binPath= "C:\Apps\SecureOps\Worker\SecureOps.Worker.exe" start= auto obj= "CONTOSO\svc-secureops"
sc.exe description SecureOpsWorker "SecureOps Hangfire job host."
sc.exe start SecureOpsWorker
```

See `docs/runbooks/04-deployment.md` (Phase 1).
