# SecureOps.Domain

Pure C# domain model. **No external dependencies.**

## What goes here

- Entities: `Alert`, `AlertEvent`, `DiagnosticJob`, `DiagnosticResult`, `Server`, `User`, `AuditEvent`, `ApplicationSession`, and the Operational Record/Jira workflow aggregate.
- Value objects: `AlertSeverity`, `AlertType`, `AlertStatus`, `DiagnosticJobStatus`, `OperationalRecordClassification`, and `OperationalRecordWorkflowState`.
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

The Operational Record/Jira aggregate and pure SDM v1 evaluator are documented
in `docs/22-operational-record-jira-workflow.md` and ADR-0018. Positive SDM policy
remains deferred; evaluation receives identity-free facts and has no I/O or clock.
