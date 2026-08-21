# ADR-0010: Authentication-Independent Access and Command Safety

**Status:** Accepted
**Date:** 2026-08-12

## Context

Windows Negotiate is the current corporate authentication source, while corporate OIDC inputs are not yet approved. Direct authorization from authentication claims would couple application permissions to either provider and would let newly authenticated users inherit operational access without an application approval record. Concurrent Jira workflows also require controls stronger than browser state or process-local locks.

## Decision

Authorization follows this provider-neutral chain:

`Authentication source -> Corporate principal -> SecureOps access approval -> Application role -> Capability`

First-seen principals become `Pending` and receive no operational capabilities. Approved administrators decide requests, assign reviewed roles, and can disable access. Capability policies revalidate application status on every protected request. Demo compatibility requires explicit Demo/Test authentication and `Access:DemoCompatibilityEnabled=true`. OIDC is not implemented; a resolver accepts a future issuer/subject pair without changing access records or authorization policies.

A rejection is a durable terminal decision for its request. The user remains `Pending` and has no operational capabilities, but ordinary `GET /access/me` reconciliation returns the latest rejected request and never creates a replacement. No reapplication, cooling-off, or administrator-reset policy is approved; therefore no reapplication endpoint exists. A future reapplication mechanism requires an explicit policy and must preserve all prior requests and audit history.

Authorized administrators can list and read backend-owned access-user projections, including current roles, backend-derived capabilities, latest request, full request history, and explicit mutation versions. Optional display profile fields reuse the configured exact-match identity provider; unresolved or unavailable enrichment is `null` and is never fabricated. Access request decisions and user mutations require the corresponding version and return distinct validation, lifecycle, concurrency, authorization, and self-approval errors.

Command endpoints use a durable idempotency execution record plus a bounded actor claim. Operational Record workflows re-fetch and compare the strongest source version immediately before Jira creation and source close. SQL constraints remain the final duplicate barrier.

Source refresh may update bounded source data but cannot reapply classification after the workflow advances beyond initial classification states. Unknown Jira outcomes remain reconciliation-blocked across reads, command replay, and new command keys. Deterministic failure and overlap verification uses test-host-only `IJiraClient` replacements; no runtime failure-injection contract exists.

## Consequences

- AD group claims no longer grant application capabilities directly.
- Negotiate remains the production authentication handler; no application cookie is issued today.
- Future OIDC must supply approved issuer/subject, cookie/logout, and claims contracts before implementation.
- Disabled users lose protected capabilities on their next request.
- SQL providers require offline migrations through `004-access-read-model-and-versioning.sql`; the application never applies them.
- In-memory stores are deterministic local/Test substitutes only; restart durability requires SQL.
- Rejected users cannot silently reapply through ordinary application access.
