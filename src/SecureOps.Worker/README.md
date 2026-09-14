# SecureOps.Worker

.NET foreground console host for the existing Hangfire job server. Windows Service
hosting, registration, installation and unattended operation are deferred, not verified.

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

## Foreground Hosting

Use the matched Worker payload, .NET 8 `Microsoft.NETCore.App`, and the approved
server-owned configuration. No SDK is required. Start from the payload directory
so its existing configuration is loaded; do not copy local fixtures or keys:

```powershell
Set-Location -LiteralPath '<approved Worker payload directory>'
dotnet .\SecureOps.Worker.dll
```

Keep the foreground session alive; Ctrl+C requests graceful host/Hangfire shutdown.
No service or scheduler is installed by these commands. API source submissions
persist, but collection cannot complete without this host and its matching queue.
Controlled TEST must therefore name the foreground operator/session and recovery
procedure. See the current Turkish runbook in `docs/24-api-test-deployment-readiness.md`.
