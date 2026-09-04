# ADR-0012: Corporate Turuncu Hat and Jira Adapters

**Status:** Accepted for disabled-by-default implementation; real TEST activation remains contract-gated
**Date:** 2026-08-23

## Context

ADR-0009 established the durable Operational Record to Jira workflow and deferred real adapters. Sanitized legacy evidence proves the minimum login, query, Jira search/create, BPM activity query, and BPM update request shapes. Controlled evidence added Jira Basic authentication and create metadata, one exact Jira user-search result, the Turuncu Hat query envelope and keyed cells, and one matching BPM activity. It does not prove write response envelopes, session expiry semantics, remote Jira idempotency, conditional source updates, or correlation headers.

## Decision

- Add explicit `TuruncuHat` and `Corporate` providers behind `IOperationalRecordClient`, `IRequesterResolver`, and `IJiraClient`.
- Keep both providers disabled unless selected by validated server-owned configuration. Never fall back to `Fake`.
- Use typed `HttpClient` instances with default platform TLS validation, explicit connect/request timeouts, bounded responses, cancellation, and no credential logging.
- Cache Turuncu Hat sessions for an explicitly configured lifetime behind a thread-safe single-flight manager. Never expose or log a session value.
- Own query grammar in the backend. Map corporate source rows by exact semantic keys: scalar values from `SET.id`, `SET.p_code`, `SET.p_name`, and `SET.p_description`, and the optional requester display value from `KEY.p_rel_requester`. Do not collapse `SET.` and `KEY.` prefixes: `SET.p_rel_requester` is an internal relation representation and `num` is row metadata, so neither maps to an Operational Record field. Allow bounded, non-conflicting extra keyed cells, reject missing or duplicated required keys and conflicting duplicates, and preserve exact-count positional compatibility only for legacy keyless fixtures. Skip malformed items without failing the whole import, reject duplicate source IDs or OR codes, and HTML-decode title/description.
- Treat the source-created timestamp as unknown because the evidenced OR query does not select one. The domain/API/SQL contract makes it nullable instead of fabricating a value.
- Resolve Jira users by one unique exact stable-name match first, then one unique exact display-name fallback. Never fuzzy-match or choose among duplicates.
- Map Jira fields from strongly typed configuration. The SecureOps actor remains separate from source requester and integration identity. Assignment defaults to the Jira project; an assignee may be emitted only through one exact deployment-verified actor mapping and participates in the draft fingerprint. Reporter remains project-controlled and is not create-set because reviewed metadata omits it.
- Do not automatically retry Jira create. Transport failures, timeouts after dispatch, invalid success responses, and HTTP 5xx are unknown outcomes and enter existing reconciliation-required state.
- After a confirmed Jira key is persisted, query exactly one configured BPM activity, update it, and require an evidenced `UpdateResult.Success=true`. Zero or multiple activities fail close-only completion without creating another Jira.
- Preserve database command fencing, claims, source freshness, one transfer per OR, key-first persistence, close-only retry, and reconciliation from ADR-0009/0010.
- Expose only `Configured`, `Disabled`, or `Unavailable` provider diagnostics to Admin. Do not expose URLs, credentials, sessions, usernames, or remote bodies.
- For the real-data TEST read-only gate, require an explicit `OperationalRecords:ReadOnlyIntegrationMode=true` with the complete Turuncu Hat/Corporate pair. Block create/retry before workflow mutation and block both corporate write adapters before HTTP dispatch while preserving source reads, Jira user resolution, and preview.

## Activation Gate

The code can be tested against deterministic sanitized fixtures. Real external TEST remains blocked until the samples listed in `docs/26-enterprise-turuncu-hat-jira-adapters.md` are reviewed. Unknown remote idempotency means reconciliation remains manual after an ambiguous Jira create.

## Rejected Alternatives

- Porting PowerShell structure into C#: rejected because it combines secrets, wire calls, matching, and workflow state.
- Using import time as source creation time: rejected because it fabricates source data and destabilizes freshness fingerprints.
- Blind retry of create or source update: rejected because either write may already have succeeded.
- Selecting the first activity or Jira user: rejected because response ordering is not a business invariant.
- Inferring assignee from AD, display name, or integration identity: rejected because no deterministic operator-to-Jira mapping is proven.
- Emitting reporter from existing-issue observations: rejected because reporter is absent from create metadata for the authenticated integration user.
- Disabling platform TLS validation: rejected.

## References

- ADR-0009
- ADR-0010
- `docs/22-operational-record-jira-workflow.md`
- `docs/integrations/turuncu-hat-jira-legacy-parity.md`
- `docs/26-enterprise-turuncu-hat-jira-adapters.md`
