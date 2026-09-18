# SecureOps.Worker

.NET foreground console host for the existing Hangfire job server. Windows Service
hosting, registration, installation and unattended operation are deferred, not verified.

The default-off announcement source composition uses one configured Hangfire/SQL queue,
bounded execution leases and startup/minutely dispatch recovery. API and Worker must share
the same dedicated source database, preinstalled Hangfire schema and queue. Runtime schema
preparation is rejected. Source jobs never send email. Fixture-backed process interruption/restart is
locally exercised; corporate adapter/service-installation acceptance remains separate.
See `docs/contracts/planned-announcement-source-acceptance.md` for configuration and evidence.

The owner-authorized operations continuation also registers default-off
AnnouncementMail commands on this same server/queue. Mail requires separate
Enabled/SelfTestEnabled/SendEnabled fences and persisted narrow capabilities.
No automatic SMTP retry: startup/minutely recovery re-enqueues only undispatched
Queued intents and classifies expired Dispatching as Unknown. Original initiator
survives Worker restart; current authorization is checked again before dispatch.
SQL intent/audit precedes SMTP, recorded acceptance does not prove inbox delivery.
See the current planned-announcements contract and Turkish upgrade runbook.
The console needs .NET 8, the same private DB/schema/queue/profile/relay policy as
API, its actual Windows runtime identity, and private config/assets/log ACLs. It
does not inherit IIS web.config or AppPool identity. No Windows Service support.

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

## In Use Follow-up

The same Hangfire host performs bounded In Use execution and durable outbox
recovery when explicitly enabled. Normal deployments keep `InUseCompletion`
disabled; no extra service/host is installed. Step leases, original initiator,
immutable artifact and append-only evidence are in additive SQL 022. A lost
mutation response is Unknown, not permission for a queue retry. Corporate target,
attachment reconciliation and final OR-state contracts remain blocking. See
`docs/inuse-v2-followup.md` and `docs/inuse-v2-upgrade-tr.md`.
# Integrated activation continuation

Workflow reporting reads persisted Worker evidence, never dispatches work. In Use
execution continues to consume frozen SQL workbook bytes, not a new loose-file
archive. API archive migration does not require UI/Worker access to that directory.
Heartbeat/queue readiness is separate from source, upload, closure and SMTP results.
Foreground hosting still needs a named session/operator, operating window, restart
and monitoring responsibility; no Windows Service or unattended availability was
introduced. See `docs/integrated-activation-tr.md` for current activation limits.
