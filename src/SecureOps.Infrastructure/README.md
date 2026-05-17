# SecureOps.Infrastructure

External integrations and data access.

## Namespaces

- `Data/` — EF Core `SecureOpsDbContext`, entity configurations.
- `Audit/` — `IAuditWriter` and SQL implementation (Dapper).
- `PowerShell/` — `IPowerShellRunner` JEA implementation.
- `Diagnostic/` — `IDiagnosticModule` implementations, `DiagnosticRunner`, `DiagnosticModuleRegistry`.
- `Alerts/` — Alert normalization, repository.
- `Integrations/` — External system adapters with interface + mock + real implementations:
  - `SolarWinds/` — Monitoring platform.
  - `Pam/` — BeyondTrust-style PAM (Phase 4+).
  - `Teams/` — Teams webhook (Phase 3+).
  - `Mail/` — SMTP relay (Phase 3+).
  - `Snapshot/` — Virtualization snapshot inspector (Phase 5+).
  - `Mocks/` — Mock implementations for each.
- `Notifications/` — Notification dispatch, rule engine (Phase 3+).
- `Compliance/` — Local admin inventory, drift detection (Phase 5+).
- `Analysis/` — Rule evaluation engine, `AnalysisFinding` (Phase 6+).
- `Ai/` — Data masker, retriever, LLM client, AI audit (Phase 7+).
- `Remediation/` — Approval workflow, snapshot verification, execution (Phase 8+).
- `Jobs/` — Hangfire job classes.

## Dependencies

- `SecureOps.Domain`
- `SecureOps.Shared`

## Phase

Populated incrementally starting Phase 1.
