# SecureOps.Domain

Pure C# domain model. **No external dependencies.**

## What goes here

- Entities: `Alert`, `AlertEvent`, `DiagnosticJob`, `DiagnosticResult`, `Server`, `User`, `AuditEvent`.
- Value objects: `AlertSeverity`, `AlertType`, `AlertStatus`, `DiagnosticJobStatus`.
- Domain events (in-memory, raised by entities; consumed by application services).
- Domain services that encapsulate pure business rules.

## What does NOT go here

- EF Core mappings or DbContext (see `SecureOps.Infrastructure`).
- ASP.NET Core types (`HttpContext`, controllers).
- HTTP clients.
- PowerShell invocation.
- File I/O.
- SQL.

## Phase

Populated incrementally during Phase 1. See `docs/04-domain-model.md` for the target shape and `plans/PHASE-1-readonly-diagnostic-mvp.md` for the task list.
