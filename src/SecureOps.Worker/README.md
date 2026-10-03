# SecureOps.Worker

.NET native Windows Service or foreground console host for the existing Hangfire
job server. The 2026-09-21 source adds AddWindowsService; actual SCM installation,
logoff independence and crash recovery remain acceptance gates, not local claims.
See `docs/worker-service-operations-tr.md`, reached through the current Turkish
entry and single `docs/integrated-test-activation.md` register.

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
The console needs the .NET 10 runtime (packages built before 2026-10-02: .NET 8), the same private DB/schema/queue/profile/relay policy as
API, its actual Windows runtime identity, and private config/assets/log ACLs. It
does not inherit IIS web.config or AppPool identity. Native hosting does not change
these fences, send retries, actor attribution or SQL recovery contracts.

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

Use the matched Worker payload, .NET 10 `Microsoft.NETCore.App` (.NET 8 for pre-2026-10-02 packages), and the approved
server-owned configuration. No SDK is required. New source loads configuration
from AppContext.BaseDirectory in console, service and diagnostics modes; only
appsettings.Test.json is supported without requiring appsettings.json. Existing
environment/CLI precedence is retained; do not copy local fixtures or keys:

```powershell
Set-Location -LiteralPath '<approved Worker payload directory>'
dotnet .\SecureOps.Worker.dll --environment Test
```

Keep the foreground session alive; Ctrl+C requests graceful host/Hangfire shutdown.
No service or scheduler is installed by these commands. API source submissions
persist, but collection cannot complete without this host and its matching queue.
Until service acceptance, name the foreground operator/session and recovery owner.
Service mode requires explicit environment, configured Hangfire server and an
existing absolute local WorkerHosting:DataDirectory. This directory holds a
FileShare.None process lock and bounded JSON lifecycle logs (14 x 10 MiB), not audit
or report archives. Console uses the same lock/logs when configured; diagnostics
bypasses both and never starts the host. The lock cannot fence rc6.26, another
directory or another machine; durable SQL leases remain authoritative. Stop the
old console before handover. The host shutdown budget is 90 seconds; uncertain
outcomes remain subject to existing lease reconciliation, not automatic retry.

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
The foreground operating window remains necessary until native service acceptance.
The owner now reports matched API/Worker settings and one matching Worker on
rc6.26, not a successful SCCM/TH collection. See the current operator entry and
the service procedure; historical rc6.26 guidance is not successor applicability.

## Selected SCCM Diagnostic

New source supports `--sccm-diagnostics --SccmDiagnosticProfile NonProd` with an
explicit absolute `--contentRoot` for the normal installed configuration. It is
mutually exclusive with `--diagnostics`. Only an enabled, configured allowlisted
profile is accepted. This performs one bounded read through the real collection
client; no Hangfire host/job, SQL, service lookup, mail or closure is started.
Output contains runtime/engine hash, module-path availability booleans, counts and
sanitized failure metadata, never device names or raw ErrorRecord bodies. Exit 2
means failed/partial, not a successful source journey. Normal diagnostics remains
read-only configuration/readiness inspection and does not query a collection.
Run only in the approved operator window under the normal effective identity and
startup overrides. The current Turkish entry contains the procedure; do not run
legacy scripts as probes or replace the installed Worker with a diagnostic build.
