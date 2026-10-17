# ADR-0018: Deterministic SDM Evaluation Foundation

**Status:** Accepted for the explicitly authorized backend foundation
**Date:** 2026-09-06

## Decision

### Bounded Pilot Policy Proposal, 2026-09-10

Implementation is authorized; corporate policy acceptance/activation is NOT.
Proposed `WASAS-SDM-PILOT-2026.09-v1`: approve exactly one numeric source ID,
its SHA-256 source fingerprint, exact configured source scope, mapping version,
expiry and decision reference in server-owned configuration. A type selection or
prose cannot grant eligibility. Only ServerRequest uses the evidenced mapping;
SoftwareInstallation and appended ServerRetirement=7 remain mapping-blocked.
Retirement means request tracking, never decommissioning. No second approver is
invented. Source/current-content, exact requester and authenticated reporter,
capabilities, archive-independent Jira linkage/claims and write fences still apply.
Policy changes are part of the preview fingerprint and rechecked before create.
Positive evaluation is persisted through existing transactional evaluation/audit.
Configuration defaults empty: no corporate record becomes eligible on upgrade.
The v1 evaluator remains the default; stored v1 results cannot acquire eligibility
by deserialization. Migration 013 replaces the 009 negative-only CHECK in a
transaction; existing runtime grants suffice. No applied migration is rewritten.

Business decision required: accept this exact one-record tracking-only rule,
including optional structured server references, or specify which missing
information must block it. Type-specific mappings, searchable remote correlation
and create-result acceptance remain separate evidence requirements. No remote
exactly-once or unknown-create search contract is claimed.

`WASAS-SDM-2026.09-v1` separates recommendation, human approval, Jira publication readiness, and external-write eligibility. This bounded Operational Record milestone does not activate the Phase 6 analysis engine.

V1 has no positive category policy. Current seven-cell corporate records remain `NeedsManualReview`, `JiraEligible=false`, and `SdmCandidateRecommended=false`. Existing enums and workflow states stay frozen. `CategorySupported` means a recognized category enum, not approved SDM policy; `CategoryPolicyPending` always blocks publication.

The pure Domain evaluator accepts validation/attestation facts, never source prose, requester identities, localized labels, relation IDs, availability, credentials, or a clock. Missing group/DCC/category attestation stays unknown. Infrastructure references are supporting evidence only. Requester/reporter and approval readiness are separate blockers; evaluation never resolves identities or calls source/Jira clients. Runtime approval remains absent.

Migration 009 adds bounded nullable evaluation evidence to the current record and append-only workflow history. Existing rows mean unevaluated. SQL serializes evaluation with workflow transitions and commits evidence, history, and audit together. Unchanged canonical input preserves the timestamp and emits no evaluation history/audit. InMemory is a local substitute only. Progressed, transferred, and reconciliation workflow states are preserved.

## Canonicalization Contract

The input hash is lowercase framework SHA-256 with no new package. Canonical input is a compact UTF-8 JSON array, in this fixed position order:

1. Ruleset, source fingerprint, synthetic, contradictory, provider supported.
2. Active, group attestation, DCC attestation, ID/code/title/description validity.
3. Category, server/IP presence, requester presence/resolved/ambiguous, reporter resolved.
4. Approval granted, writes disabled, already transferred, reconciliation, source changed, evaluation stale.

Booleans are JSON booleans; unknown attestation is null; category is its frozen integer or null. No culture, time, random value, actor, correlation ID, collection ordering, or raw source text enters the evaluation input. Fingerprints accept exactly 64 lowercase hex characters. Invalid fingerprints become null plus contradictory evidence in the hash; arbitrary text cannot enter it.

The source fingerprint is lowercase SHA-256 of the UTF-8 existing source concurrency token. Explicit tokens use the existing `source:` prefix and trimmed token. Otherwise the source freshness boundary hashes compact UTF-8 JSON in fixed order: `source-state-v2`, source ID, OR code, title, description, requester, UTC created time, environment, server, application, open boolean, UTC modified time. That token retains the `sha256:` prefix. Null stays null; strings use framework JSON escaping; timestamps use framework UTC DateTimeOffset serialization. This replaces ambiguous newline joins. Only digests leave that boundary, never a duplicate canonical input or raw payload. Old fallback tokens require refresh after binary upgrade and fail closed on mismatch.

Reason codes and blocker subsets are unique and ordinal-sorted, independent of traversal order. Source ID validation requires positive Int64 with ASCII digits and invariant parsing. OR code is `OR-` plus ASCII digits, bounded to 64 characters. Title/description must be nonblank and bounded to 500/8000 characters. Unexpected control characters invalidate text; CR/LF/tab remain permitted.

Observed fingerprint changes latch `sourceChanged` and `evaluationStale` until a future explicitly designed reevaluation/approval milestone. Refresh cannot clear the latch or authorize Jira. New semantics require a new reviewed ruleset identifier.

If refresh outruns evaluation persistence, API reads overlay stale/source-changed flags and ordered blockers on the prior evidence. Its original input hash/time remain unchanged; the read does not fabricate a new evaluation or audit event.

## Consequences and Deferred Work

DBA review/execution of migration 009 is required before SQL-backed binary upgrade. Its nullable fields are additive and old binaries can ignore them. Existing migrations, append-only triggers, grants, runtime settings, and deployed TEST state remain unchanged. SQL tests are offline contracts; they do not prove live constraint or transaction behavior.

Native ETag/conditional updates, positive category policy, structured attestations, human approval endpoint/persistence, and external-write activation remain pending. Action Center consumes additive evidence and cannot infer approval from reason text. Synthetic provider harnesses remain separate from corporate evaluation and cannot supply corporate eligibility evidence.

## Amendment 1, 2026-10-03: Advisory request-type suggestion

Owner decision: the operator no longer starts from an empty "operator declaration" select. The read
model adds `suggestedRequestType` (`RequestTypeSuggester`, rules `WASAS-REQUEST-TYPE-HINT-2026.10-v1`):
explicit Turkish/English keywords in the title or description point to exactly one of ServerRequest,
SoftwareInstallation or ServerRetirement, and the matched source words are returned so the UI can show
why. Words for two types, or none, produce no suggestion. Server phrases ("yeni sunucu kurulumu") are
consumed before installation words so they do not vote twice.

This does not reopen "the evaluator never uses source prose": the suggestion is computed at read time
outside `SdmEvaluationInput`, is not persisted, hashed, audited or used for classification, and grants
no eligibility. The review request still carries the type the operator confirmed or changed, which
remains an operator declaration (`OperatorDeclarationOnly`) and is still checked by the pilot policy
and mapping rules above. New words or semantics require a new rule version.
