# ADR-0010: Authentication-Independent Access and Command Safety

**Status:** Accepted
**Date:** 2026-08-12

## Context

Windows Negotiate is the current corporate authentication source, while corporate OIDC inputs are not yet approved. Direct authorization from authentication claims would couple application permissions to either provider and would let newly authenticated users inherit operational access without an application approval record. Concurrent Jira workflows also require controls stronger than browser state or process-local locks.

## Decision

Authorization follows this provider-neutral chain:

`Authentication source -> Corporate principal -> SecureOps access approval -> Application role -> Capability`

First-seen principals become `Pending` and receive no operational capabilities. Approved administrators decide requests, assign reviewed roles, and can disable access. Capability policies revalidate application status on every protected request. Demo compatibility requires explicit Demo/Test authentication and `Access:DemoCompatibilityEnabled=true`. OIDC is not implemented; a resolver accepts a future issuer/subject pair without changing access records or authorization policies.

Command endpoints use a durable idempotency execution record plus a bounded actor claim. Operational Record workflows re-fetch and compare the strongest source version immediately before Jira creation and source close. SQL constraints remain the final duplicate barrier.

## Consequences

- AD group claims no longer grant application capabilities directly.
- Negotiate remains the production authentication handler; no application cookie is issued today.
- Future OIDC must supply approved issuer/subject, cookie/logout, and claims contracts before implementation.
- Disabled users lose protected capabilities on their next request.
- SQL providers require the offline `003-platform-access-concurrency-hardening.sql` DBA migration.
- In-memory stores are deterministic local/Test substitutes only; restart durability requires SQL.
