# ADR-0012: Corporate Turuncu Hat and Jira Adapters

**Status:** Accepted for disabled-by-default implementation; real TEST activation remains contract-gated
**Date:** 2026-08-23

## Context

ADR-0009 established the durable Operational Record to Jira workflow and deferred real adapters. Sanitized legacy evidence now proves the minimum login, query, Jira search/create, BPM activity query, and BPM update request shapes. It does not prove complete response envelopes, session expiry semantics, remote Jira idempotency, conditional source updates, or correlation headers.

## Decision

- Add explicit `TuruncuHat` and `Corporate` providers behind `IOperationalRecordClient`, `IRequesterResolver`, and `IJiraClient`.
- Keep both providers disabled unless selected by validated server-owned configuration. Never fall back to `Fake`.
- Use typed `HttpClient` instances with default platform TLS validation, explicit connect/request timeouts, bounded responses, cancellation, and no credential logging.
- Cache Turuncu Hat sessions for an explicitly configured lifetime behind a thread-safe single-flight manager. Never expose or log a session value.
- Own query grammar in the backend. Parse only the evidenced `QueryResult.Items[index][0].Value` projection, skip malformed items without failing the whole import, reject duplicate source IDs or OR codes, and HTML-decode title/description.
- Treat the source-created timestamp as unknown because the evidenced OR query does not select one. The domain/API/SQL contract makes it nullable instead of fabricating a value.
- Resolve Jira users by one unique exact stable-name match first, then one unique exact display-name fallback. Never fuzzy-match or choose among duplicates.
- Map Jira fields from strongly typed configuration. The SecureOps actor remains audit identity and is never substituted for source requester, integration identity, assignee, or reporter.
- Do not automatically retry Jira create. Transport failures, timeouts after dispatch, invalid success responses, and HTTP 5xx are unknown outcomes and enter existing reconciliation-required state.
- After a confirmed Jira key is persisted, query exactly one configured BPM activity, update it, and require an evidenced `UpdateResult.Success=true`. Zero or multiple activities fail close-only completion without creating another Jira.
- Preserve database command fencing, claims, source freshness, one transfer per OR, key-first persistence, close-only retry, and reconciliation from ADR-0009/0010.
- Expose only `Configured`, `Disabled`, or `Unavailable` provider diagnostics to Admin. Do not expose URLs, credentials, sessions, usernames, or remote bodies.

## Activation Gate

The code can be tested against deterministic sanitized fixtures. Real external TEST remains blocked until the samples listed in `docs/26-enterprise-turuncu-hat-jira-adapters.md` are reviewed. Unknown remote idempotency means reconciliation remains manual after an ambiguous Jira create.

## Rejected Alternatives

- Porting PowerShell structure into C#: rejected because it combines secrets, wire calls, matching, and workflow state.
- Using import time as source creation time: rejected because it fabricates source data and destabilizes freshness fingerprints.
- Blind retry of create or source update: rejected because either write may already have succeeded.
- Selecting the first activity or Jira user: rejected because response ordering is not a business invariant.
- Disabling platform TLS validation: rejected.

## References

- ADR-0009
- ADR-0010
- `docs/22-operational-record-jira-workflow.md`
- `docs/integrations/turuncu-hat-jira-legacy-parity.md`
- `docs/26-enterprise-turuncu-hat-jira-adapters.md`
