# SecureOps.Infrastructure

External integrations and data access.

## Namespaces

Current implemented Phase 1A namespaces:
- `Audit/` contains audit writer abstractions plus InMemory, File, queued, and SQL Server persistence implementations.
- `Identity/` contains the IdentityLookup service, exact account normalizer, read-only AD provider, provider guard, and mock PAM resolver hook.
- `OperationalRecords/` contains source/Jira/requester boundaries, fail-closed classification, preview and transfer services, durable repositories, and fake local adapters.

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

## Current state

Phase 1A audit and identity lookup infrastructure is implemented. The Operational Record to Jira backend foundation is implemented with fake integrations and optional SQL persistence; real integration adapters and approved classification rules are pending.

See `Identity/README.md`, `Audit/README.md`, and `OperationalRecords/README.md` for provider selection, local-test boundaries, configuration, and controlled-runtime blockers.
