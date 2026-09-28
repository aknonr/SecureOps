# SecureOps.Infrastructure

Archive integrity follow-up after sealed E-08: `InvalidDataException` now maps to
`InUseArchiveIntegrityFailed`, independently of `IOException`. Corrupt historical
envelopes remain blocked before download/audit authorization and are never repaired
in place. New synthetic cases exercise bytes, size, identity, preparer, retention
and malformed JSON through both archive and service. Supplied archives are private
review inputs, not repository fixtures; see the canonical IU-07-D follow-up.

In Use manual-verification continuation (2026-09-22): new intents freeze Manual
mode and stop Unconfirmed after BPM acknowledgement. Final source readback alone
is no longer required; real dispatch still lacks attachment/concurrency contracts.
Manual attestation uses the existing SQL access lock, exact OR/operation/revision,
trusted confirmer and append-only audit; it never releases the duplicate fence or
creates Closure/Verified evidence. No new schema or flag. See ADR-0020 and IU-06.

The 03b0c04 continuation adds a SQL 024 archive catalogue. Verified envelope
metadata is indexed atomically with its existing receipt; exact duplicates are
no-ops and metadata conflicts fail. Search/count/page run under current access
checks and the administration lock, not filesystem scans. Explicit historical
indexing is bounded to 25 selected versions and safely repeatable.
`InUseReporterResolver` uses a server-reviewed exact IdentityScope/UserReference
crosswalk to an existing UserId, then rechecks current eligibility. No mapping
ships, and no name matching/account provisioning occurs. The known mutation
client is composed only with the real source provider; it is NOT registered as
the completion transport. Missing conditional/readback contracts still prevent
real execution. See `docs/post-rc626-continuation-tr.md` and ADR-0020.

Post-rc6.26 In Use recovery uses the existing versioned JSON aggregate and
administration/execution transaction boundary. Discard is not a source lifecycle:
review-only SQL status remains compatible, while queries/reporting apply Discarded
separately. InvalidatedReviewsThrough blocks proposal reuse without rewriting
append-only reviews. API/Worker must stay matched; older writers cannot preserve
new lifecycle metadata safely. Those lifecycle repairs alone introduced no migration;
the later report catalogue requires additive 024. No corporate grant was applied.
Corporate XLSX output retains four legacy sheets; provenance stays in archive
EvidenceSheets. See `docs/post-rc626-repair-tr.md` and ADR-0020.

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

The SCCM collection adapter uses the matching Microsoft.PowerShell.SDK 7.4.18
in-process host, including Management/Utility modules needed by New-PSDrive and
Select-Object. Engine-only hosting failed a local real-runspace regression test.
Roslyn workspace/compiler and schema-test dependencies are aligned to that SDK;
this does not change target execution policy, module installation or permissions.
Its staged command failures expose bounded ErrorRecord metadata, not raw messages,
target objects or source payloads. Selected Worker diagnostics uses the same read
path and does not start corporate workflows. See integrated-test-activation E-05.
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
# Workflow snapshot extension

SQL 023 adds bounded reporting cuts and verified In Use archive receipts.
`SqlWorkflowReportStore` materializes permitted facts under serializable isolation;
aggregates, pages and Excel share the cut and filters. Archive receipts are written
only after envelope integrity/commit, including repair on re-download. Frozen
execution bytes remain in SQL; no Worker filesystem archive permission is added.
Historical receipt/provenance gaps remain labelled. See ADR-0023 and the integrated
activation runbook; current source adapter contract gaps are not solved by fixtures.
## In Use activity semantics (2026-09-22, E-08)

New intents use `WasasActivityManual`, not whole-OR closure. Acknowledgement and
unknown mutation outcomes stop without retry; authoritative BPM verification may
complete the activity while preserving the duplicate fence. Only Closure/Verified
supports OR-closed reporting. SQL tracking projects verified activity events with
the same authorized count/page query. Manual events never enter that projection.
Review freshness excludes parent workflow/title changes, but execution retains
the full source/version/review guards. New report attribution uses exact trusted
application profiles; no source-display-name matching or extra directory query.
Corporate transport remains blocked on the precise IU-05 contracts in
`docs/integrated-test-activation.md`. No new migration; existing catalogue 024
remains the reviewed, target-unverified delta. See ADR-0020 for historical semantics.

In Use lists default to stable Code/ordinal Id (`Sort=code`). Optional, deferred
`Sort=oldest` orders source creation ascending; `newest` reverses
known dates only. Unknown/invalid dates stay last; binary code and ordinal GUID
break ties before SQL paging. No schema change. `pending` and `verification` views
reuse normalized activity evidence and the durable active-execution fence. Missing
refresh rows retain answers and the prior observation, never imply completion.
The real parser still has no parent creation or completed-activity read contract;
fixture reconciliation tests do not activate that corporate integration.
