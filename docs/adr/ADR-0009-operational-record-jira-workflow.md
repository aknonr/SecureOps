# ADR-0009 - Durable Operational Record to Jira Workflow

**Status:** Accepted for backend foundation; real adapters pending approval  
**Date:** 2026-08-12  
**Decision makers:** Project owner

## Context

A legacy operator-driven PowerShell workflow queries active operational records, creates a Jira task, then closes/updates the source record. Jira creation can succeed while the source update fails, so a naive retry can create duplicates. The existing script is not available in this repository and its fixed mappings are not approved classification rules.

## Decision

SecureOps will replace the workflow with typed backend boundaries, a preview-before-create API, server-side capability policies, append-only audit, and SQL-backed durable state.

- Controllers call services, never external HTTP or PowerShell.
- Local/default source, Jira, and requester providers are fakes.
- Classification fails closed to `NeedsManualReview` until approved rules exist.
- Jira creation requires an explicit authorized POST after preview.
- A deterministic source-record/mapping idempotency key is protected by SQL uniqueness.
- The Jira issue key is persisted before source close/update.
- Retry resumes source close when a Jira key exists.
- Unknown Jira outcomes and interrupted create ownership require reconciliation; automatic recreation is blocked.
- Real external write adapters require a later approval and contract review.
- Reporter policy is explicit: `ProjectDefault` preserves legacy behavior; `AuthenticatedOperator` resolves the server-authenticated actor through the bounded exact Jira user-search boundary, exposes the verified Jira username in preview, and fails closed before create when resolution is absent or ambiguous.
- The Jira integration credential authenticates REST calls only. It is never used as an operator-reporter fallback, and Jira rejection of the explicit reporter is returned as a stable actionable failure.

## Consequences

### Independent Source Close, 2026-09-07

Source close is separately controlled by default-off `SourceCloseEnabled`, below
the existing global write fences. The reviewed draft binds that intent into its
fingerprint; create acquisition persists it before external dispatch. Jira-only
success remains `JiraCreated`, with a durable key and `SourceCloseRequested=false`.
It is neither a close failure nor `Completed`. Retry/replay/restart cannot upgrade
that intent when configuration changes. No automatic continuation or future
close-intent transition is introduced. Legacy transfers default to no close intent.
Migration 011 is additive and must precede the new SQL-backed binaries.

The owner confirms Sunucu Talebi and Uygulama Kurulumu as request types, represented
by existing ServerRequest and SoftwareInstallation values. Operator declaration
is not source attestation, infrastructure scope, identity validation, permission,
or positive eligibility. No keyword classifier or separate approver is authorized.
Application-installation Jira labels remain unresolved and cannot reuse SunucuTalep.

September 2026 hardening binds the reviewed draft fingerprint to the source token
and exact emitted summary/description. A source refresh or older persisted
fingerprint cannot silently authorize different content. Key persistence after
remote success uses a bounded cancellation scope independent of browser lifetime.
If commit acknowledgement fails, the durable state is preserved and the result
requires reconciliation; neither a retryable create failure nor exactly-once
delivery is inferred. SQL transaction rollback and fresh-service retry exclusion
are exercised using a test-owned failure trigger in isolated LocalDB.

The backend contracts and workflow can be tested without credentials or live systems. SQL is required for restart-safe production behavior; InMemory remains development-only. Until real remote reconciliation semantics are proven, some uncertain failures require operator/administrator intervention rather than an automatic retry.

## Rejected Alternatives

- Executing the legacy PowerShell from the API: rejected because it mixes credentials, UI, integrations, and business rules.
- Display-name fuzzy matching: rejected because it can assign work to the wrong person.
- Creating Jira during import: rejected because creation requires explicit authorization and preview.
- Memory-only idempotency: rejected because it does not survive restart or multiple processes.
- Assuming Jira idempotency: rejected until enterprise Jira semantics are proven.

## References

- `docs/22-operational-record-jira-workflow.md`
- `sql/schema/002-operational-record-jira-workflow.sql`
- `docs/05-security-model.md`
- `docs/06-integrations.md`
