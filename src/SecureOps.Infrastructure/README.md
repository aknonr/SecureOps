# SecureOps.Infrastructure

External integrations and data access.

Combined announcement persistence requires reviewed migrations 014-018 and separately
provisioned Hangfire. See `docs/contracts/planned-announcement-integration.md` for grants.

`Resources/` provides the shared link catalogue and personal preferences with
capability-checked services, SQL transactions and a local in-memory substitute.
It requires migration 010 when Access persistence is SqlServer; see its README.

`InUse/` provides independent bounded read-only discovery, persisted local review,
audited assignment and managed text-only XLSX preparation. SQL persistence requires
012. The existing Turuncu Hat transport supplies only evidenced root relationships;
unknown server/owner contracts remain explicit. Discovery never writes to source;
the separate default-off completion executor is described below.
See ADR-0020 and the canonical In Use handoff in `src/SecureOps.Ui/README.md`.

## Namespaces

Current implemented Phase 1A namespaces:
- `Audit/` contains audit writer abstractions plus InMemory, File, queued, and SQL Server persistence implementations.
- `Identity/` contains the IdentityLookup service, exact account normalizer, read-only AD provider, provider guard, and mock PAM resolver hook.
- `OperationalRecords/` contains source/Jira/requester boundaries, fail-closed classification, preview and transfer services, durable repositories, and fake local adapters.
- `Sessions/` contains provider-neutral lifecycle service plus in-memory and SQL authoritative session repositories.

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

SQL-backed Access, Audit, Operational Record, command idempotency, reporting, and application-session providers share `ConnectionStrings:SecureOpsDb`. They require the applicable migrations through 007 and the object-level runtime grants documented in `docs/24-api-test-deployment-readiness.md`; the application never executes those migrations.

Post-rc6.22 In Use history/execution adds SQL 022 to the current installed 001-021
chain. `InUse/Execution` keeps immutable reviewed bytes separate from execution
versions, uses the existing Hangfire host and revalidates current authority before
each new step. The enabled synthetic transport is local-only. Known corporate
mutation HTTP shapes are tested without network but are not registered for
dispatch while verified target/readback contracts are missing. Server history
uses stable source identity, not hostname/IP. See `docs/inuse-v2-followup.md`.
